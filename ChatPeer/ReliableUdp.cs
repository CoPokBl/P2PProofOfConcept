using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace ChatPeer;

public class ReliableUdp {
    public event Action? OnHeartbeat;
    
    private readonly Socket _socket;
    private uint _acknowledged = 1;
    private byte[]? _waitingForAck;
    private readonly ConcurrentQueue<byte[]> _incoming = new();
    private readonly ConcurrentQueue<PendingSendMessage> _outgoing = new();
    private readonly List<string> _sent = [];
    private IPEndPoint _peer;
    
    private object _sentLock = new();
    private bool _doesContactExist;
    
    private static void Debug(string msg) {
        if (Program.Debug) {
            Console.WriteLine(msg);
        }
    }

    private void MadeContact() {
        if (!_doesContactExist) {
            Debug("Made contact with peer");
            _doesContactExist = true;
        }
    }

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
            MadeContact();
            
            Array.Resize(ref outputBuffer, length);

            byte packetType = outputBuffer[0];
            
            if (packetType == 0) {  // Heartbeat
                OnHeartbeat?.Invoke();
                continue;  // Drop it
            }
            if (packetType == 1) {  // Message
                // Proceed to message processing
            }
            else {
                continue;  // Invalid, drop it
            }
            
            // Remove first byte from output
            outputBuffer = outputBuffer[1..];

            // Message packet
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
            if (_outgoing.TryDequeue(out PendingSendMessage? info)) {
                byte[] data = info.Message;
                IPEndPoint endPoint = info.Peer;
                byte[] checksum = MD5.HashData(data);
                
                Debug($"[SEND] Sending packet with checksum (Safe: {info.Safe}, From: {_socket.LocalEndPoint}): " + BitConverter.ToUInt32(checksum) + " to " + endPoint.Address + ":" + endPoint.Port + "...");
                
                while (info.Safe) {  // Go until ack
                    _socket.SendTo(data, endPoint);
                    Debug("[SEND] Waiting for ack");
                    
                    _waitingForAck = checksum;
                    Stopwatch sw = Stopwatch.StartNew();
                    while (_waitingForAck != null && sw.ElapsedMilliseconds < 1000) {
                        Thread.Yield();
                    }

                    if (_waitingForAck == null) {
                        Debug("[SEND] Ack received");
                        lock (_sentLock) {
                            _sent.Add(info.Id);
                        }
                        break;
                    }

                    Debug("[SEND] Timeout, resending packet");
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
        PendingSendMessage msg = new() {
            Message = data,
            Peer = endPoint,
            Id = Guid.NewGuid().ToString()
        };
        _outgoing.Enqueue(msg);
        
        uint checksum = BitConverter.ToUInt32(MD5.HashData(data));
        Stopwatch sw = Stopwatch.StartNew();
        while (timeout == -1 || sw.ElapsedMilliseconds < timeout) {
            lock (_sentLock) {
                if (_sent.Contains(msg.Id)) {
                    return true;
                }
            }
            
            Thread.Yield();
        }

        return false;
    }
    
    public bool UnsafeSendTo(byte[] data, IPEndPoint endPoint) {
        PendingSendMessage msg = new() {
            Message = data,
            Peer = endPoint,
            Id = Guid.NewGuid().ToString(),
            Safe = false
        };
        _outgoing.Enqueue(msg);
        return true;
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