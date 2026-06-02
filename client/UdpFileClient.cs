using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

public class UdpFileClient
{
    public static void Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: mono UdpFileClient.exe <hostname> <port> <file-list>");
            return;
        }

        string hostname = args[0];
        int port = int.Parse(args[1]);
        string[] filenames = File.ReadAllLines(args[2]);

        IPEndPoint serverEndPoint = new IPEndPoint(Dns.GetHostAddresses(hostname)[0], port);

        foreach(string filename in filenames)
        {
            Console.WriteLine(filename);
            DownloadFile(hostname, serverEndPoint, filename);
        }
    }

    static async Task DownloadFile(string hostname, IPEndPoint serverEndPoint, string filename)
    {
        string reply;
        byte[] sendBytes;

        // Create control socket
        UdpClient controlSocket = new UdpClient();
        
        // Build and send DOWNLOAD message
        string downloadMsg = $"DOWNLOAD {filename}";
        byte[] downloadBytes = Encoding.ASCII.GetBytes(downloadMsg);
        controlSocket.Send(downloadBytes, downloadBytes.Length, serverEndPoint);

        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        byte[] responseBytes = controlSocket.Receive(ref remoteEndPoint);
        string response = Encoding.ASCII.GetString(responseBytes);

        string[] parts = response.Split(' ');
        
        if(parts[0] == "ERR" || parts[0] != "OK")
        {
            Console.WriteLine(response);
            return;
        }

        long fileSize = long.Parse(parts[3]);
        int transferPort = int.Parse(parts[5]);

        // Create data socket
        UdpClient dataSocket = new UdpClient();

        // Set dataEndPoint to server IP but with the transferPort from OK
        IPEndPoint dataEndPoint = new IPEndPoint(serverEndPoint.Address, transferPort);

        // Buffer to store the whole file as we receive it
        byte[] fileBuffer = new byte[fileSize];
        long bytesReceived = 0;

        Console.WriteLine(filename + " 0%");

        while(bytesReceived < fileSize)
        {
            long start = bytesReceived;
            long end = Math.Min(start + 999, fileSize - 1);

            reply = $"FILE {filename} GET START {start} END {end}";
            sendBytes = Encoding.ASCII.GetBytes(reply);
            dataSocket.Send(sendBytes, sendBytes.Length, dataEndPoint);

            int tries = 0;
            while (tries < 5)
            {
                dataSocket.Client.ReceiveTimeout = (1 + tries) * 1000;

                try
                {
                    byte[] replyBytes = dataSocket.Receive(ref remoteEndPoint);
                    reply = Encoding.ASCII.GetString(replyBytes);
                    string[] replyParts = reply.Split(' ');

                    if(long.Parse(replyParts[4]) == start && long.Parse(replyParts[6]) == end)
                    {
                        byte[] chunkData = Convert.FromBase64String(replyParts[8]);
                        Array.Copy(chunkData, 0, fileBuffer, start, chunkData.Length);
                        dataEndPoint = remoteEndPoint;
                        break;
                    }
                    else
                    {
                        //ignore
                    }
                }
                catch (SocketException)
                {
                    // timed out — flush and retry
                    dataSocket.Client.ReceiveTimeout = 1;
                    try { dataSocket.Receive(ref remoteEndPoint); } catch { }

                    tries++;
                    dataSocket.Send(sendBytes, sendBytes.Length, dataEndPoint);
                }                
            }

            if (tries == 5)
            {
                Console.WriteLine($"ERROR {filename} too many retries");
                return;
            }

            bytesReceived += (end - start + 1);
            int progress = (int)(bytesReceived * 100 / fileSize);
            Console.WriteLine($"{filename} {progress}%");
        }

        reply = $"FILE {filename} CLOSE";
        sendBytes = Encoding.ASCII.GetBytes(reply);
        dataSocket.Send(sendBytes, sendBytes.Length, dataEndPoint);

        dataSocket.Receive(ref dataEndPoint);

        File.WriteAllBytes(filename, fileBuffer);
        Console.WriteLine($"OK {filename}");
        dataSocket.Close();
        controlSocket.Close();
    }
}
