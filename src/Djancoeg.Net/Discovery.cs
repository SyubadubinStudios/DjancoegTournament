using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Djancoeg.Core.Content;

namespace Djancoeg.Net;

/// <summary>
/// Finds servers: LAN broadcast on the default port range, and optional master-server queries for
/// online play. Non-blocking: call <see cref="Poll"/> every frame while the browser is open.
/// </summary>
public sealed class ServerBrowser : IDisposable
{
    private readonly UdpTransport _transport = new(0, IPAddress.Any, broadcast: true);
    private readonly Dictionary<string, ServerInfo> _found = [];
    private readonly Dictionary<string, double> _sentAt = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private uint _nonce = (uint)Environment.TickCount;

    public IReadOnlyCollection<ServerInfo> Servers => _found.Values;

    /// <summary>Broadcasts a discovery request on the LAN (ports 27960-27963).</summary>
    public void RefreshLan()
    {
        for (int port = Protocol.DefaultPort; port < Protocol.DefaultPort + 4; port++)
            Ping(new IPEndPoint(IPAddress.Broadcast, port));
        // Also probe loopback so a local server always shows up.
        Ping(new IPEndPoint(IPAddress.Loopback, Protocol.DefaultPort));
    }

    /// <summary>Asks a master server for its list; each listed server is then pinged directly.</summary>
    public void RefreshOnline(string masterHostPort)
    {
        var parts = masterHostPort.Split(':');
        int port = parts.Length > 1 && int.TryParse(parts[1], out int p) ? p : Protocol.DefaultMasterPort;
        try
        {
            var addr = Dns.GetHostAddresses(parts[0]).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (addr is null) return;
            var w = new NetWriter(_transport.SendBuffer);
            Protocol.WriteHeader(ref w, PacketType.MasterQuery);
            _transport.Send(w.Written, new IPEndPoint(addr, port));
        }
        catch (SocketException) { }
    }

    public void Ping(IPEndPoint ep)
    {
        var w = new NetWriter(_transport.SendBuffer);
        Protocol.WriteHeader(ref w, PacketType.DiscoveryRequest);
        w.U32(++_nonce);
        _sentAt[ep.ToString()] = _clock.Elapsed.TotalMilliseconds;
        _transport.Send(w.Written, ep);
    }

    public void Clear() => _found.Clear();

    public void Poll(uint expectedSignature = 0)
    {
        for (int guard = 0; guard < 128; guard++)
        {
            int n = _transport.Receive(out var from);
            if (n <= 0) break;
            var r = new NetReader(_transport.ReceiveBuffer.AsSpan(0, n));
            var type = Protocol.ReadHeader(ref r);
            var src = UdpTransport.ToEndPoint(from);
            if (type == PacketType.DiscoveryResponse)
            {
                r.U32();
                string name = r.Str(), arena = r.Str();
                byte mode = r.U8(); int players = r.U8(); r.U8(); int max = r.U8();
                uint sig = r.U32(); int port = r.U16();
                if (r.Error) continue;
                var ep = new IPEndPoint(src.Address, port);
                var info = new ServerInfo(name, arena, mode, players, max, sig, ep);
                if (_sentAt.TryGetValue(ep.ToString(), out double t0) || _sentAt.TryGetValue($"{IPAddress.Broadcast}:{port}", out t0))
                    info.PingMs = (float)(_clock.Elapsed.TotalMilliseconds - t0);
                // Same machine answers on loopback and LAN: keep one entry per port+name.
                string key = IPAddress.IsLoopback(src.Address) || IsLocalAddress(src.Address) ? $"local:{port}" : ep.ToString();
                _found[key] = info;
            }
            else if (type == PacketType.MasterList)
            {
                int count = r.U16();
                for (int i = 0; i < count && !r.Error; i++)
                {
                    var ip = new IPAddress(r.U32());
                    int port = r.U16();
                    Ping(new IPEndPoint(ip, port));
                }
            }
        }
    }

    private static bool IsLocalAddress(IPAddress a)
    {
        try
        {
            return Dns.GetHostAddresses(Dns.GetHostName()).Any(x => x.Equals(a));
        }
        catch (SocketException) { return false; }
    }

    public void Dispose() => _transport.Dispose();
}

/// <summary>
/// Tiny master server for the online server browser. Game servers send heartbeats; clients
/// query the list. Entries expire after 60 seconds without a heartbeat.
/// </summary>
public sealed class MasterServer(int port = Protocol.DefaultMasterPort, Action<string>? log = null) : IDisposable
{
    private readonly UdpTransport _transport = new(port);
    private readonly Dictionary<IPEndPoint, double> _servers = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public int Port => _transport.LocalPort;
    public int Count => _servers.Count;

    public void Run(CancellationToken token)
    {
        log?.Invoke($"Master server listening on UDP {Port}");
        while (!token.IsCancellationRequested)
        {
            _transport.Poll(200_000);
            PumpOnce();
        }
    }

    public void PumpOnce()
    {
        double now = _clock.Elapsed.TotalSeconds;
        for (int guard = 0; guard < 256; guard++)
        {
            int n = _transport.Receive(out var from);
            if (n <= 0) break;
            var r = new NetReader(_transport.ReceiveBuffer.AsSpan(0, n));
            var type = Protocol.ReadHeader(ref r);
            var src = UdpTransport.ToEndPoint(from);
            if (type == PacketType.MasterHeartbeat)
            {
                int gamePort = r.U16();
                if (r.Error) continue;
                var ep = new IPEndPoint(src.Address, gamePort);
                if (!_servers.ContainsKey(ep)) log?.Invoke($"+ server {ep}");
                _servers[ep] = now;
            }
            else if (type == PacketType.MasterQuery)
            {
                var w = new NetWriter(_transport.SendBuffer);
                Protocol.WriteHeader(ref w, PacketType.MasterList);
                int countPos = w.Position; w.U16(0);
                ushort count = 0;
                foreach (var ep in _servers.Keys)
                {
                    if (w.Remaining < 6) break;
#pragma warning disable CS0618 // IPv4 address as uint is exactly what we want on the wire
                    w.U32((uint)ep.Address.Address);
#pragma warning restore CS0618
                    w.U16((ushort)ep.Port);
                    count++;
                }
                var buf = _transport.SendBuffer;
                buf[countPos] = (byte)count; buf[countPos + 1] = (byte)(count >> 8);
                _transport.Send(w.Written, from);
            }
        }
        foreach (var ep in _servers.Where(kv => now - kv.Value > 60).Select(kv => kv.Key).ToList())
        {
            _servers.Remove(ep);
            log?.Invoke($"- server {ep} expired");
        }
    }

    public void Dispose() => _transport.Dispose();
}

public static class ModeNames
{
    public static (string En, string Id) Of(GameModeKind m) => m switch
    {
        GameModeKind.FreeForAll => ("Free-for-All Brawl", "Tawuran Bebas"),
        GameModeKind.TeamDeathmatch => ("Village vs Village", "Perang Kampung"),
        GameModeKind.KingOfTheHill => ("King of the Warung", "Raja Warung"),
        GameModeKind.CaptureTheFlag => ("Snack Snatch", "Rebutan Gorengan"),
        GameModeKind.Race => ("Greased Pole Race", "Panjat Pinang"),
        _ => (m.ToString(), m.ToString()),
    };
}
