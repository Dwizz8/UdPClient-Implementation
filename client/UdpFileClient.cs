using System;
using System.IO;
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

    static void DownloadFile(IPEndPoint serverEndPoint, string filename)
    {
        // Phase 1 - Send DOWNLOAD and get OK/ERR
        UdpClient controlSocket = new UdpClient();
        byte[] downloadBytes = Encoding.ASCII.GetBytes($"DOWNLOAD {filename}");
        controlSocket.Send(downloadBytes, downloadBytes.Length, serverEndPoint);

        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        
        // set timeout on control socket in case server is unreachable
        controlSocket.Client.ReceiveTimeout = 5000;
        byte[] responseBytes;
        try
        {
            responseBytes = controlSocket.Receive(ref remoteEndPoint);
        }
        catch (SocketException)
        {
            Console.WriteLine($"ERROR {filename} no response from server");
            controlSocket.Close();
            return;
        }

        string response = Encoding.ASCII.GetString(responseBytes).Trim();
        string[] parts = response.Split(' ');

        if (parts[0] == "ERR")
        {
            Console.WriteLine(response);
            controlSocket.Close();
            return;
        }

        if (parts[0] != "OK")
        {
            Console.WriteLine($"ERROR {filename} unexpected response");
            controlSocket.Close();
            return;
        }

        long fileSize = long.Parse(parts[3]);
        int transferPort = int.Parse(parts[5]);

        // Phase 2 - Set up data socket
        UdpClient dataSocket = new UdpClient();
        IPEndPoint dataEndPoint = new IPEndPoint(serverEndPoint.Address, transferPort);
        byte[] fileBuffer = new byte[fileSize];
        long bytesReceived = 0;

        Console.WriteLine($"{filename} 0%");

        // Phase 3 - Download chunks
        while (bytesReceived < fileSize)
        {
            long start = bytesReceived;
            long end = Math.Min(start + 999, fileSize - 1);

            byte[] getMsg = Encoding.ASCII.GetBytes($"FILE {filename} GET START {start} END {end}");

            bool chunkReceived = false;

            for (int tries = 0; tries < 5; tries++)
            {
                // send the GET (or resend on retry)
                dataSocket.Send(getMsg, getMsg.Length, dataEndPoint);

                // wait (1 + tries) seconds for reply
                dataSocket.Client.ReceiveTimeout = (1 + tries) * 1000;

                // keep trying to receive until we get matching reply or timeout
                while (true)
                {
                    try
                    {
                        IPEndPoint senderEndPoint = new IPEndPoint(IPAddress.Any, 0);
                        byte[] replyBytes = dataSocket.Receive(ref senderEndPoint);
                        string replyStr = Encoding.ASCII.GetString(replyBytes).Trim();
                        string[] replyParts = replyStr.Split(' ');

                        // check its a FILE OK message with matching start/end
                        if (replyParts.Length >= 9 &&
                            replyParts[0] == "FILE" &&
                            replyParts[2] == "OK" &&
                            long.Parse(replyParts[4]) == start &&
                            long.Parse(replyParts[6]) == end)
                        {
                            byte[] chunkData = Convert.FromBase64String(replyParts[8]);
                            Array.Copy(chunkData, 0, fileBuffer, start, chunkData.Length);
                            dataEndPoint = senderEndPoint;
                            chunkReceived = true;
                            break;
                        }
                        // non matching — ignore and keep waiting
                    }
                    catch (SocketException)
                    {
                        // timed out — break inner loop and retry
                        break;
                    }
                }

                if (chunkReceived) break;
            }

            if (!chunkReceived)
            {
                Console.WriteLine($"ERROR {filename} too many retries");
                dataSocket.Close();
                controlSocket.Close();
                return;
            }

            bytesReceived += (end - start + 1);
            int progress = (int)(bytesReceived * 100 / fileSize);
            Console.WriteLine($"{filename} {progress}%");
        }

        // Phase 4 - Close and save
        byte[] closeMsg = Encoding.ASCII.GetBytes($"FILE {filename} CLOSE");
        dataSocket.Send(closeMsg, closeMsg.Length, dataEndPoint);

        // wait for CLOSE_OK
        dataSocket.Client.ReceiveTimeout = 5000;
        try
        {
            dataSocket.Receive(ref dataEndPoint);
        }
        catch (SocketException)
        {
            // spec says if CLOSE_OK is lost, protocol does not recover
            // just save the file anyway
        }

        File.WriteAllBytes(filename, fileBuffer);
        Console.WriteLine($"OK {filename}");

        dataSocket.Close();
        controlSocket.Close();
    }
}
