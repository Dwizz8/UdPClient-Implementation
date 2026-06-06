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
        //checks if the correct number of arguments are passed
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: mono UdpFileClient.exe <hostname> <port> <file-list>");
            return;
        }

        //parses all the lines
        string hostname = args[0];
        int port = int.Parse(args[1]);

        //reads all the filenames to download, one per line
        string[] filenames = File.ReadAllLines(args[2]);

        //creates the server end point from the hostname
        IPEndPoint serverEndPoint = new IPEndPoint(Dns.GetHostAddresses(hostname)[0], port);

        //download each file one at a time
        foreach(string filename in filenames)
        {
            Console.WriteLine(filename);
            DownloadFile(hostname, serverEndPoint, filename);
        }
    }

    static void DownloadFile(IPEndPoint serverEndPoint, string filename)
    {
        // Phase 1 - Send DOWNLOAD and get OK/ERR

        //create a UDP socket for the control channel
        UdpClient controlSocket = new UdpClient();
        byte[] downloadBytes = Encoding.ASCII.GetBytes($"DOWNLOAD {filename}");
        
        //send DOWNLOAD <filename> to the servers listener port
        controlSocket.Send(downloadBytes, downloadBytes.Length, serverEndPoint);

        //blank endpoint, will be filled in by Receive with the servers address
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        
        //set 5 second timeout on control socket in case server is unreachable
        controlSocket.Client.ReceiveTimeout = 5000;
        byte[] responseBytes;
        try
        {
            //gets the ERR response on the control socket
            responseBytes = controlSocket.Receive(ref remoteEndPoint);
        }
        catch (SocketException)
        {
            Console.WriteLine($"ERROR {filename} no response from server");
            controlSocket.Close();
            return;
        }

        //decode the response from bytes to string
        string response = Encoding.ASCII.GetString(responseBytes).Trim();
        string[] parts = response.Split(' ');

        //if the server say file not found, print the ERR line and stop
        if (parts[0] == "ERR")
        {
            Console.WriteLine(response);
            controlSocket.Close();
            return;
        }

        //also checks if it says neither "ERR" or "OK"
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
                        
                        //no statement for when its a stale packet, just ignores
                    }
                    catch (SocketException)
                    {
                        //flush stale packets before retransmit
                        dataSocket.Client.ReceiveTimeout = 1;
                        try { dataSocket.Receive(ref senderEndPoint); } catch { }

                        //increment tries and attempts to receive again
                        tries++;
                        dataSocket.Send(getMsg, getMsg.Length, dataEndPoint); // retransmit
                    }
                }

                //if you get a matching reply don't retry
                if (chunkReceived) break;
            }

            //if all 5 of the attempts fail give up
            if (!chunkReceived)
            {
                Console.WriteLine($"ERROR {filename} too many retries");
                dataSocket.Close();
                controlSocket.Close();
                return;
            }

            //update how many bytes we have and print progress
            bytesReceived += (end - start + 1);
            int progress = (int)(bytesReceived * 100 / fileSize);
            Console.WriteLine($"{filename} {progress}%");
        }

        // Phase 4 - Close and save
        byte[] closeMsg = Encoding.ASCII.GetBytes($"FILE {filename} CLOSE");
        dataSocket.Send(closeMsg, closeMsg.Length, dataEndPoint);

        //wait for CLOSE_OK
        dataSocket.Client.ReceiveTimeout = 5000;
        try
        {
            dataSocket.Receive(ref dataEndPoint);
        }
        catch (SocketException)
        {
            //if CLOSE_OK is lost, protocol does not recover so just save it anyway
        }

        //write the completed bytes to disk
        File.WriteAllBytes(filename, fileBuffer);
        Console.WriteLine($"OK {filename}");

        dataSocket.Close();
        controlSocket.Close();
    }
}
