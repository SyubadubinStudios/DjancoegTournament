using System.Net;
using System.Net.Sockets;

namespace Djancoeg.Net;

/// <summary>
/// Thin non-blocking UDP socket wrapper using the allocation-free <see cref="SocketAddress"/> APIs.
/// </summary>
public sealed class UdpTransport : IDisposable
{
    private readonly Socket _socket;
    private readonly SocketAddress _recvAddress;
    public byte[] ReceiveBuffer { get; } = new byte[2048];
    public byte[] SendBuffer { get; } = new byte[Protocol.MaxPacketSize];
    public int LocalPort => ((IPEndPoint)_socket.LocalEndPoint!).Port;
    public long BytesSent { get; private set; }
    public long BytesReceived { get; private set; }

    public UdpTransport(int port = 0, IPAddress? bind = null, bool broadcast = false)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
        if (OperatingSystem.IsWindows())
        {
            // Ignore ICMP "port unreachable" which would otherwise surface as ConnectionReset on the next receive.
            const int SioUdpConnReset = -1744830452;
            _socket.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
        }
        _socket.EnableBroadcast = broadcast;
        _socket.ReceiveBufferSize = 1 << 20;
        _socket.SendBufferSize = 1 << 20;
        _socket.Bind(new IPEndPoint(bind ?? IPAddress.Any, port));
        _recvAddress = new SocketAddress(AddressFamily.InterNetwork);
    }

    /// <summary>Receives one datagram. Returns its length, or 0 when nothing is pending. <paramref name="from"/> is reused.</summary>
    public int Receive(out SocketAddress from)
    {
        from = _recvAddress;
        try
        {
            if (_socket.Available <= 0) return 0;
            int n = _socket.ReceiveFrom(ReceiveBuffer.AsSpan(), SocketFlags.None, _recvAddress);
            BytesReceived += n;
            return n;
        }
        catch (SocketException e) when (e.SocketErrorCode is SocketError.WouldBlock or SocketError.ConnectionReset or SocketError.MessageSize)
        {
            return 0;
        }
    }

    /// <summary>Blocks up to <paramref name="microseconds"/> waiting for data.</summary>
    public bool Poll(int microseconds) => _socket.Poll(microseconds, SelectMode.SelectRead);

    public void Send(ReadOnlySpan<byte> data, SocketAddress to)
    {
        try
        {
            _socket.SendTo(data, SocketFlags.None, to);
            BytesSent += data.Length;
        }
        catch (SocketException) { /* unreachable peers are handled by timeouts */ }
    }

    public void Send(ReadOnlySpan<byte> data, IPEndPoint to) => Send(data, to.Serialize());

    public void Dispose() => _socket.Dispose();

    public static IPEndPoint ToEndPoint(SocketAddress a) => (IPEndPoint)new IPEndPoint(IPAddress.Any, 0).Create(a);
}
