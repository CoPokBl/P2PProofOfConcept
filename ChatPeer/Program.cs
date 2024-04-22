using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ChatPeer;

internal class Program {
    public static bool Debug = false;
    
    public static int Main(string[] args) {
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);


        if (args.Length < 3) {
            Console.WriteLine("Usage: ChatPeer <holepuncherIp> <holepuncherPort> <host|roomCode> [debug]");
            return 1;
        }
        
        Debug = args is [_, _, _, "debug"];

        // Read ip and port from args
        string serverIp = args[0];
        int serverPort = int.Parse(args[1]);
        IPEndPoint holePuncherEndpoint = new(IPAddress.Parse(serverIp), serverPort);
        IPEndPoint? peerEndpoint;

        bool host = args[2] == "host";

        if (host) {
            // Ask server for a code
            byte[] sendBytes = [0];
            socket.SendTo(sendBytes, holePuncherEndpoint);
    
            byte[] receiveBytes = new byte[4];
            socket.Receive(receiveBytes);
    
            uint code = BitConverter.ToUInt32(receiveBytes);
            Console.WriteLine($"Code: {code}");
    
            // Get our new peer
            Console.WriteLine("Waiting for peer to connect... ");
            byte[] peerBytes = new byte[6];
            socket.Receive(peerBytes);
            Console.Write("Done");
    
            ushort port = BitConverter.ToUInt16(peerBytes);
            byte[] ipBytes = peerBytes[2..];
            IPAddress ip = new(ipBytes);
    
            peerEndpoint = new IPEndPoint(ip, port);
        }

        else {
            // Connect to server
            ulong targetCode = ulong.Parse(args[2]);
            byte[] codeBytes = BitConverter.GetBytes(targetCode);
            byte[] sendBytes = new byte[1 + codeBytes.Length];
            sendBytes[0] = 1;
            codeBytes.CopyTo(sendBytes, 1);
    
            socket.SendTo(sendBytes, holePuncherEndpoint);
    
            byte[] receiveBytes = new byte[6];
            socket.Receive(receiveBytes);
    
            ushort port = BitConverter.ToUInt16(receiveBytes);
            byte[] ipBytes = receiveBytes[2..];
            IPAddress ip = new(ipBytes);
    
            peerEndpoint = new IPEndPoint(ip, port);
        }

        Console.Write($"Attempting hole punch... ");
        socket.SendTo([69], peerEndpoint);  // Punch the hole, there will be no response
        Thread.Sleep(100);
        Console.WriteLine("Done (Hopefully)");

        // WE ARE NOW CONNECTED TO PEER
        // THESE PACKETS GO DIRECTLY TO THE PEER AND NOT THROUGH THE HOLEPUNCHER

        socket.ReceiveTimeout = 5000;
        ReliableUdp reliableUdp = new(socket, peerEndpoint);

        // Generate RSA asymmetric key for encryption
        RSA rsa = RSA.Create();
        byte[] rsaPublicKey = rsa.ExportSubjectPublicKeyInfo();
        byte[] rsaPrivateKey = rsa.ExportRSAPrivateKey();

        Console.WriteLine("My public key: " + Convert.ToBase64String(rsaPublicKey));

        Console.WriteLine("Attempting to negotiate keys... ");
        Console.WriteLine("Peer IP: " + peerEndpoint.Address);
        Console.WriteLine("Peer Port: " + peerEndpoint.Port);

        byte[] peerPublicKey = new byte[rsaPublicKey.Length];

        // Host goes first with sending keys
        if (host) {
            Console.Write("Sending public key to peer... ");
            bool success = reliableUdp.SendTo(rsaPublicKey, peerEndpoint, 5000);
            if (!success) {
                Console.WriteLine("Timeout");
                Console.WriteLine("Could not establish connection, exiting...");
                Environment.Exit(1);
            }
            Console.WriteLine("Done");
        }
        else {  // Client waits for host to send keys
            Console.Write("Waiting for public key from host... ");
            int length = reliableUdp.Receive(peerPublicKey, 5000);
            if (length == -1) {
                Console.WriteLine("Timeout");
                Console.WriteLine("Could not establish connection, exiting...");
                Environment.Exit(1);
            }
            Console.WriteLine("Done");
            Console.WriteLine("Host public key: " + Convert.ToBase64String(peerPublicKey));
        }


        // Now the peer sends their public key
        if (!host) {
            Console.Write("Sending public key to host... ");
            reliableUdp.SendTo(rsaPublicKey, peerEndpoint);
            Console.WriteLine("Done");
        }
        else {  // Host waits for client to send keys
            Console.Write("Waiting for public key from client... ");
            reliableUdp.Receive(peerPublicKey);
            Console.WriteLine("Done");
            Console.WriteLine("Client public key: " + Convert.ToBase64String(peerPublicKey));
        }

        socket.ReceiveTimeout = -1;

        string inpState = "";
        List<(string, string)> messages = [];

        Thread msgListener = new(() => {
            Console.WriteLine("Listening for messages...");
            while (true) {
                byte[] receiveBytes = new byte[1024];
                int length = reliableUdp.Receive(receiveBytes);
                Array.Resize(ref receiveBytes, length);
        
                // Decrypt message
                try {
                    byte[] decryptedBytes = rsa.Decrypt(receiveBytes, RSAEncryptionPadding.OaepSHA256);
                    messages.Add(("Friend", Encoding.UTF8.GetString(decryptedBytes)));
                    PrintScreen();
                }
                catch (Exception e) {
                    Console.WriteLine(e);
                    Console.WriteLine("Failed to decrypt message, dropping packet");
                    Console.WriteLine("PacketLength: " + receiveBytes.Length);
                }
            }
        });
        msgListener.Start();


        // Send messages
        RSA clientRsa = RSA.Create();
        clientRsa.ImportSubjectPublicKeyInfo(peerPublicKey, out _);

        while (true) {
            Console.Write("You: ");
            ConsoleKeyInfo key = Console.ReadKey(false);
            if (key.Key == ConsoleKey.Enter) {
                Console.WriteLine();
                string message = inpState;
                inpState = "";
        
                // Encrypt message
                byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        
                byte[] encryptedBytes = clientRsa.Encrypt(messageBytes, RSAEncryptionPadding.OaepSHA256);
                Console.WriteLine($"Sending {encryptedBytes.Length} bytes...");
                reliableUdp.SendTo(encryptedBytes, peerEndpoint);
        
                messages.Add(("You", message));
                PrintScreen();
            }
            else {
                inpState += key.KeyChar;
            }
        }


        return 0;

        void PrintScreen() {
            Console.Clear();
            Console.WriteLine("Chatting with peer...");
            foreach ((string sender, string message) in messages) {
                Console.WriteLine(sender + ": " + message);
            }
            Console.WriteLine("> " + inpState);
        }
    }
}