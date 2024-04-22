using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace ChatPeer;

public class ReliableUdp {
    private readonly Socket _socket;
    private uint _acknowledged = 1;
    private byte[]? _waitingForAck;
    private readonly ConcurrentQueue<byte[]> _incoming = new();
    private readonly ConcurrentQueue<(byte[], IPEndPoint)> _outgoing = new();
    private readonly List<uint> _sent = new();
    private IPEndPoint _peer;
    
    private object _sentLock = new();

    public ReliableUdp(Socket socket, IPEndPoint peer) {
        _socket = socket;
        _peer = peer;
        _socket.ReceiveTimeout = -1;
        
        Thread sendThread = new(ReceivePackets);
        sendThread.Start();
        
        Thread receiveThread = new(SendPackets);
        receiveThread.Start();
    }

    private void ReceivePackets() {
        while (true) {
            byte[] outputBuffer = new byte[1024];
            int length = _socket.Receive(outputBuffer);
            
            Array.Resize(ref outputBuffer, length);

            if (length == 1 && outputBuffer[0] == 69) {  // Holepunch packet
                continue;  // Drop it
            }

            if (_waitingForAck != null && outputBuffer.SequenceEqual(_waitingForAck)) {
                _waitingForAck = null;
                continue;
            }
            
            byte[] checksum = MD5.HashData(outputBuffer);
            _socket.SendTo(checksum, _peer);
            uint checksumInt = BitConverter.ToUInt32(checksum);
            if (_acknowledged == checksumInt) {  // Not new data
                continue;
            }
            _acknowledged = checksumInt;
            
            _incoming.Enqueue(outputBuffer);
        }
    }
    
    private void SendPackets() {
        while (true) {
            if (_outgoing.TryDequeue(out (byte[], IPEndPoint) info)) {
                byte[] data = info.Item1;
                IPEndPoint endPoint = info.Item2;
                byte[] checksum = MD5.HashData(data);
                
                while (true) {  // Go until ack
                    _socket.SendTo(data, endPoint);
                    
                    _waitingForAck = checksum;
                    Stopwatch sw = new();
                    sw.Start();
                    while (_waitingForAck != null && sw.ElapsedMilliseconds < 1000) {
                        Thread.Yield();
                    }

                    if (_waitingForAck == null) {
                        lock (_sentLock) {
                            _sent.Add(BitConverter.ToUInt32(checksum));
                        }
                        break;
                    }
                }
            }

            Thread.Yield();
        }
    }
    
    /// <summary>
    /// Send function that sends data to an endpoint, this function should be reliable.
    /// The data will be delivered to the endpoint.
    /// </summary>
    /// <param name="data"></param>
    /// <param name="endPoint"></param>
    /// <returns>True if the packet was sent successfully, otherwise false.</returns>
    public bool SendTo(byte[] data, IPEndPoint endPoint, int timeout = -1) {
        _outgoing.Enqueue((data, endPoint));
        
        uint checksum = BitConverter.ToUInt32(MD5.HashData(data));
        Stopwatch sw = Stopwatch.StartNew();
        while (timeout == -1 || sw.ElapsedMilliseconds < timeout) {
            lock (_sentLock) {
                if (_sent.Contains(checksum)) {
                    return true;
                }
            }
            
            Thread.Yield();
        }

        return false;
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="outputBuffer"></param>
    /// <param name="timeout"></param>
    /// <returns>-1 is there was a timeout, otherwise the length of the received packet.</returns>
    public int Receive(byte[] outputBuffer, int timeout = -1) {
        Stopwatch sw = Stopwatch.StartNew();
        while (timeout == -1 || sw.ElapsedMilliseconds < timeout) {
            if (!_incoming.TryDequeue(out byte[] data)) continue;
            Array.Copy(data, outputBuffer, data.Length);
            return data.Length;
        }

        return -1;
    }
    
}