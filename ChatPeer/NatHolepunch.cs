using System.Net;
using System.Net.Sockets;

namespace ChatPeer;

public class NatHolepunch(Socket socket, EndPoint holePuncherEndpoint) {
    private bool? _host;

    public uint StartSession() {
        if (_host != null) {
            throw new Exception("Session already exists");
        }
        
        _host = true;
        
        // Ask server for a code
        byte[] sendBytes = [0];
        socket.SendTo(sendBytes, holePuncherEndpoint);
    
        byte[] receiveBytes = new byte[4];
        Console.Write("Waiting for code... ");
        socket.Receive(receiveBytes);
        Console.WriteLine("Done");
    
        uint code = BitConverter.ToUInt32(receiveBytes);
        return code;
    }
    
    public void JoinSession(ulong targetCode) {
        if (_host != null) {
            throw new Exception("Session already exists");
        }
        
        _host = false;
        
        // Connect to server
        byte[] codeBytes = BitConverter.GetBytes(targetCode);
        byte[] sendBytes = new byte[1 + codeBytes.Length];
        sendBytes[0] = 1;
        codeBytes.CopyTo(sendBytes, 1);
    
        socket.SendTo(sendBytes, holePuncherEndpoint);
    }

    public IPEndPoint GetPeer() {
        if (_host == null) {
            throw new Exception("Session does not exist");
        }

        if (_host.Value) {
            // Get our new peer
            Console.Write("Waiting for peer to connect... ");
            byte[] peerBytes = new byte[6];
            socket.Receive(peerBytes);
            Console.WriteLine("Done");
    
            ushort port = BitConverter.ToUInt16(peerBytes);
            byte[] ipBytes = peerBytes[2..];
            IPAddress ip = new(ipBytes);
    
            return new IPEndPoint(ip, port);
        }
        else {
            // Get the host
            byte[] receiveBytes = new byte[6];
            socket.Receive(receiveBytes);
    
            ushort port = BitConverter.ToUInt16(receiveBytes);
            byte[] ipBytes = receiveBytes[2..];
            IPAddress ip = new(ipBytes);
    
            return new IPEndPoint(ip, port);
        }
    }
    
}