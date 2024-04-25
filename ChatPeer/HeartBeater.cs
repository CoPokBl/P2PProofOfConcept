using System.Diagnostics;
using System.Net;

namespace ChatPeer;

public class HeartBeater(ReliableUdp udp, IPEndPoint peer) {
    private const int HeartbeatWarningThreshold = 5000;
    
    public event Action? OnDegraded;
    public bool Degraded => _peerHeartbeatTimer.ElapsedMilliseconds > HeartbeatWarningThreshold;
    public bool HasContact;

    private readonly Stopwatch _peerHeartbeatTimer = new();

    public void Start() {
        Thread thread = new(Beat);
        thread.Start();
        
        udp.OnHeartbeat += RecordPeerHeartbeat;
        _peerHeartbeatTimer.Start();
    }
    
    public void WaitForContact() {
        while (!HasContact) {
            Thread.Sleep(100);
        }
    }
    
    private void Beat() {
        while (true) {
            udp.UnsafeSendTo([0], peer);
            Thread.Sleep(1000);
        }
    }
    
    private void RecordPeerHeartbeat() {
        if (Program.Debug) {
            Console.WriteLine("[HEARTBEAT] Received Peer Heartbeat");
        }
        HasContact = true;
        if (_peerHeartbeatTimer.ElapsedMilliseconds > HeartbeatWarningThreshold) {
            OnDegraded?.Invoke();
        }
        _peerHeartbeatTimer.Restart();
    }
    
}