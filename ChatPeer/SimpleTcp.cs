using System.Net.Sockets;

namespace ChatPeer;

public class SimpleTcp {
    private readonly TcpClient _client;

    public SimpleTcp(TcpClient client) {
        _client = client;
    }
    
    public void Send(byte[] data) {
        _client.GetStream().Write(data);
    }
    
    public int Receive(byte[] buffer) {
        int length = _client.GetStream().Read(buffer);
        return length;
    }
    
}