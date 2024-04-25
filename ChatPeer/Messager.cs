using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace ChatPeer;

public class Messager(ReliableUdp reliableUdp, bool host, IPEndPoint peerEndpoint) {
    private const byte MessagePacketType = 1;
    public event Action<string>? OnMessage;
    private RSA? _rsa;
    private RSA? _peerRsa;

    public void NegotiateEncryption() {
        // Generate RSA asymmetric key for encryption
        _rsa = RSA.Create();
        byte[] rsaPublicKey = _rsa.ExportSubjectPublicKeyInfo();

        if (Program.Debug) {
            Console.WriteLine("My public key: " + Convert.ToBase64String(rsaPublicKey));
        }
        Console.WriteLine("Attempting to negotiate keys... ");

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
            if (Program.Debug) {
                Console.WriteLine("Host public key: " + Convert.ToBase64String(peerPublicKey));
            }
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
            if (Program.Debug) {
                Console.WriteLine("Client public key: " + Convert.ToBase64String(peerPublicKey));
            }
        }
        
        // Import peer's public key
        _peerRsa = RSA.Create();
        _peerRsa.ImportSubjectPublicKeyInfo(peerPublicKey, out _);
    }
    
    public void ListenForMessages() {
        Thread thread = new(MessageListener);
        thread.Start();
    }

    public void SendMessage(string message) {
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        
        byte[] encryptedBytes = _peerRsa!.Encrypt(messageBytes, RSAEncryptionPadding.OaepSHA256).Prepend(MessagePacketType).ToArray();
        Console.WriteLine($"Sending {encryptedBytes.Length} bytes...");
        reliableUdp.SendTo(encryptedBytes, peerEndpoint);
    }

    private void MessageListener() {
        Console.WriteLine("Listening for messages...");
        while (true) {
            byte[] receiveBytes = new byte[1024];
            int length = reliableUdp.Receive(receiveBytes);
            Array.Resize(ref receiveBytes, length);
        
            // Decrypt message
            try {
                byte[] decryptedBytes = _rsa!.Decrypt(receiveBytes, RSAEncryptionPadding.OaepSHA256);
                OnMessage?.Invoke(Encoding.UTF8.GetString(decryptedBytes));
            }
            catch (Exception e) {
                Console.WriteLine(e);
                Console.WriteLine("Failed to decrypt message, dropping packet");
                Console.WriteLine("PacketLength: " + receiveBytes.Length);
            }
        }
        // ReSharper disable once FunctionNeverReturns
    }
    
}