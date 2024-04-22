using System.Net;
using System.Net.Sockets;

namespace Holepuncher;

/*
Holepunch packets:

Create listener (0):
This packet should be used to start listening for a peer, a code will be given to the peer to connect to.
0

Connect to peer (1):
This packet should be used to connect to a peer, the code should be given by the peer.
1<code[6]>

*/

internal static class Program {
    private static readonly Dictionary<uint, IPEndPoint> ClientEndpoints = new();
    
    public static void Main(string[] args) {
        UdpClient udpServer = new(7653);
        while (true) {
            IPEndPoint remoteEp = new(IPAddress.Any, 0);
            byte[] data = udpServer.Receive(ref remoteEp);

            if (data.Length == 0) {
                continue;  // Drop packet
            }
            
            byte packetType = data[0];
            
            if (packetType == 0) {
                // Create listener
                // Generate a random code
                uint code = (uint) new Random().Next(0, 999999);
                ClientEndpoints[code] = remoteEp;
                
                byte[] codeBytes = BitConverter.GetBytes(code);
                udpServer.Send(codeBytes, codeBytes.Length, remoteEp);
            }
            
            else if (packetType == 1) {
                // Connect to peer
                if (!ClientEndpoints.TryGetValue(BitConverter.ToUInt32(data.AsSpan()[1..7]), out IPEndPoint? peerEp)) {
                    continue;  // Drop packet
                }

                // Give connecting peer the endpoint of the peer
                byte[] portBytes = BitConverter.GetBytes((ushort) peerEp.Port);
                byte[] ipBytes = peerEp.Address.GetAddressBytes();
                byte[] endPointBytes = new byte[2 + ipBytes.Length];
                portBytes.CopyTo(endPointBytes, 0);
                ipBytes.CopyTo(endPointBytes, 2);
                
                udpServer.Send(endPointBytes, endPointBytes.Length, remoteEp);
                
                // Give the host the endpoint of the connecting peer
                portBytes = BitConverter.GetBytes((ushort) remoteEp.Port);
                ipBytes = remoteEp.Address.GetAddressBytes();
                endPointBytes = new byte[2 + ipBytes.Length];
                portBytes.CopyTo(endPointBytes, 0);
                ipBytes.CopyTo(endPointBytes, 2);
                
                udpServer.Send(endPointBytes, endPointBytes.Length, peerEp);
            }

            // Else we drop the packet
            // Anyway free memory
            data = null!;
            remoteEp = null!;
            packetType = 0;
        }
        
        // ReSharper disable once FunctionNeverReturns
    }
    
}