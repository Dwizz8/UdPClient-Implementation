using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

public class FileTransferWorker
{
    public class Job
    {
        public string Filename;
        public IPEndPoint ClientEndpoint;
        public UdpClient TransferSocket;

        public Job(string filename, IPEndPoint clientEndpoint, UdpClient transferSocket)
        {
            Filename = filename;
            ClientEndpoint = clientEndpoint;
            TransferSocket = transferSocket;
        }
    }

    // Implement Run — see assignment specification
    // (Job: Filename, ClientEndpoint, TransferSocket).
    public static void Run(object jobObject)
    {
        string[] parts;
        string filename;
        int start;
        int end;
        string returnData;
        byte[] receiveBytes;
        string reply;
        byte[] chunk;
        string base64data;
        byte[] sendBytes;

        Job job = (Job)jobObject;

        string filePath = UdpFileServer.FilePath(job.Filename);
        if (!File.Exists(filePath))
        {
            //Sends error on the control port
            UdpFileServer.SendControlReply(job.ClientEndpoint, $"ERR {job.Filename} NOT_FOUND");
            job.TransferSocket.Close();
            return;
        }
        
        //Sends the OK with file size and transfer port
        long fileSize = new FileInfo(filePath).Length;  
        int transferPort = UdpFileServer.PublicPort(job.TransferSocket);
        UdpFileServer.SendControlReply(job.ClientEndpoint, $"OK {job.Filename} SIZE {fileSize} PORT {transferPort}");

        //Declares the port
        IPEndPoint RemoteIpEndPoint = new IPEndPoint(IPAddress.Any, 0);
        byte[] fileBytes = File.ReadAllBytes(filePath);

        try
        {
            while(true)
            {
                //The port receives the FILE GET from the client through the transfer socket
                receiveBytes = job.TransferSocket.Receive(ref RemoteIpEndPoint);

                //Converted to string format (encodes)
                returnData = Encoding.ASCII.GetString(receiveBytes); 

                //I need to do error checks for this?
                parts = returnData.Split(' ');
                filename = parts[1];

                //Will break the loop as soon as the close download is found
                if(parts[2] == "CLOSE")
                {
                    //Sends back the error 
                    reply = $"FILE {filename} CLOSE_OK";
                    byte[] closeBytes = Encoding.ASCII.GetBytes(reply);
                    job.TransferSocket.Send(closeBytes, closeBytes.Length, RemoteIpEndPoint);
                    job.TransferSocket.Close();
                    break;
                }

                start = int.Parse(parts[4]);
                end = int.Parse(parts[6]);
                
                //Declares a byte array of length (end - start + 1)
                chunk = new byte[end - start + 1];

                //Copies the contents of fileBytes into the chunk array, starting from start, and encodes it to base 64
                Array.Copy(fileBytes, start, chunk, 0, end - start + 1);
                base64data = Convert.ToBase64String(chunk);

                //Builds the reply and sends it via the TransferSocket (UDPClient)
                reply = $"FILE {filename} OK START {start} END {end} DATA {base64data}";
                sendBytes = Encoding.ASCII.GetBytes(reply);
                job.TransferSocket.Send(sendBytes, sendBytes.Length, RemoteIpEndPoint);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }
}
