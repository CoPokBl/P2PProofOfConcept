using System.Net;
using System.Net.Sockets;

namespace ChatPeer;

internal static class Program {
    public static bool Debug;
    
    public static int Main(string[] args) {
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        if (args.Length < 3) {
            Console.WriteLine("Usage: ChatPeer <holepuncherIp> <holepuncherPort> <host|roomCode> [debug]");
            return 1;
        }
        
        Debug = args is [_, _, _, "debug"];

        // Read ip and port from args
        string serverHost = args[0];
        if (serverHost == "localhost") serverHost = "127.0.0.1";  // Fix ::1 not working
        int serverPort = int.Parse(args[1]);
        
        IPEndPoint holePuncherEndpoint = new(Dns.GetHostAddresses(serverHost)[0], serverPort);
        
        Console.WriteLine("Holepuncher Address: " + holePuncherEndpoint.Address + ":" + serverPort);

        bool host = args[2] == "host";
        
        NatHolepunch holepunch = new(socket, holePuncherEndpoint);

        if (host) {
            uint code = holepunch.StartSession();
            Console.WriteLine($"Code: {code}");
        }
        else {
            // Connect to server
            ulong targetCode = ulong.Parse(args[2]);
            holepunch.JoinSession(targetCode);
        }

        IPEndPoint peerEndpoint = holepunch.GetPeer();
        Console.WriteLine("Peer IP: " + peerEndpoint.Address);
        Console.WriteLine("Peer Port: " + peerEndpoint.Port);

        // WE ARE NOW CONNECTED TO PEER
        // THESE PACKETS GO DIRECTLY TO THE PEER AND NOT THROUGH THE HOLEPUNCHER

        socket.ReceiveTimeout = 5000;
        ReliableUdp reliableUdp = new(socket, peerEndpoint);
        
        HeartBeater heartBeater = new(reliableUdp, peerEndpoint);
        heartBeater.Start();
        
        Console.Write("Waiting for contact... ");
        heartBeater.WaitForContact();
        Console.WriteLine("Done");

        Messager messager = new(reliableUdp, host, peerEndpoint);
        messager.NegotiateEncryption();

        socket.ReceiveTimeout = -1;

        string inpState = "";
        List<(string, string)> messages = [];

        messager.OnMessage += message => {
            messages.Add(("Friend", message));
            PrintScreen();
        };
        
        messager.ListenForMessages();

        // Send messages
        while (true) {
            ConsoleKeyInfo key = Console.ReadKey(false);
            if (key.Key == ConsoleKey.Enter) {
                Console.WriteLine();
                string message = inpState;
                inpState = "";
                
                messager.SendMessage(message);
                messages.Add(("You", message));
                PrintScreen();
            }
            else {
                inpState += key.KeyChar;
            }
        }


        void PrintScreen() {
            Console.Clear();
            Console.WriteLine("Chatting with peer...");
            foreach ((string sender, string message) in messages) {
                Console.WriteLine(sender + ": " + message);
            }
            // ReSharper disable once AccessToModifiedClosure
            // I know it's modified out of scope, that's the point
            Console.Write("> " + inpState);
        }
    }
}