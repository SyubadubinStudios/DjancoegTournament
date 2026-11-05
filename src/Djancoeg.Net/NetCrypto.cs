using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Djancoeg.Net;

/// <summary>
/// Ephemeral ECDH (NIST P-256) key pair used once per connection. Both sides derive two AES-256-GCM
/// keys (client-to-server and server-to-client) with HKDF-SHA256, salted with the server's connect cookie.
/// </summary>
public sealed class KeyExchange : IDisposable
{
    private readonly ECDiffieHellman _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>SubjectPublicKeyInfo encoding (91 bytes for P-256).</summary>
    public byte[] PublicKey { get; }

    public KeyExchange() => PublicKey = _ecdh.ExportSubjectPublicKeyInfo();

    private static readonly byte[] Info = Encoding.ASCII.GetBytes("djancoeg-tournament v4 channel");

    /// <summary>Derives the secure channel. Returns null if the peer key is malformed.</summary>
    public SecureChannel? Derive(ReadOnlySpan<byte> peerPublicKey, ReadOnlySpan<byte> salt, bool isServer)
    {
        try
        {
            using var peer = ECDiffieHellman.Create();
            peer.ImportSubjectPublicKeyInfo(peerPublicKey, out _);
            byte[] secret = _ecdh.DeriveRawSecretAgreement(peer.PublicKey);
            Span<byte> keys = stackalloc byte[64];
            HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, keys, salt, Info);
            CryptographicOperations.ZeroMemory(secret);
            var c2s = keys[..32];
            var s2c = keys[32..];
            var channel = isServer ? new SecureChannel(send: s2c, receive: c2s, sendPrefix: 0x53525652) : new SecureChannel(send: c2s, receive: s2c, sendPrefix: 0x434C4E54);
            CryptographicOperations.ZeroMemory(keys);
            return channel;
        }
        catch (CryptographicException) { return null; }
    }

    public void Dispose() => _ecdh.Dispose();
}

/// <summary>
/// Authenticated encryption for one direction pair. Wire format of a sealed payload:
/// <c>[8-byte counter][ciphertext][16-byte tag]</c>; the 12-byte nonce is a 4-byte direction prefix plus the counter.
/// A 64-packet sliding window rejects replays and very old packets. Sealing/opening allocates nothing.
/// </summary>
public sealed class SecureChannel : IDisposable
{
    public const int CounterSize = 8;
    public const int TagSize = 16;
    public const int Overhead = CounterSize + TagSize;

    private readonly AesGcm _send, _receive;
    private readonly uint _sendPrefix, _receivePrefix;
    private ulong _sendCounter;
    private ulong _highest;
    private ulong _window;
    private bool _any;

    internal SecureChannel(ReadOnlySpan<byte> send, ReadOnlySpan<byte> receive, uint sendPrefix)
    {
        _send = new AesGcm(send, TagSize);
        _receive = new AesGcm(receive, TagSize);
        _sendPrefix = sendPrefix;
        _receivePrefix = sendPrefix == 0x53525652 ? 0x434C4E54u : 0x53525652u;
    }

    /// <summary>Encrypts <paramref name="plaintext"/> into <paramref name="output"/>; <paramref name="aad"/> is authenticated, not encrypted.</summary>
    public int Seal(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> aad, Span<byte> output)
    {
        ulong counter = ++_sendCounter;
        Span<byte> nonce = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(nonce, _sendPrefix);
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], counter);
        BinaryPrimitives.WriteUInt64LittleEndian(output, counter);
        var cipher = output.Slice(CounterSize, plaintext.Length);
        var tag = output.Slice(CounterSize + plaintext.Length, TagSize);
        _send.Encrypt(nonce, plaintext, cipher, tag, aad);
        return Overhead + plaintext.Length;
    }

    /// <summary>Decrypts and authenticates a sealed payload. Returns false for forgeries, replays and stale packets.</summary>
    public bool Open(ReadOnlySpan<byte> sealedData, ReadOnlySpan<byte> aad, Span<byte> plaintext, out int length)
    {
        length = 0;
        if (sealedData.Length < Overhead) return false;
        ulong counter = BinaryPrimitives.ReadUInt64LittleEndian(sealedData);
        if (_any && counter + 64 <= _highest) return false;
        if (_any && counter <= _highest && (_window & (1UL << (int)(_highest - counter))) != 0) return false;

        int n = sealedData.Length - Overhead;
        if (plaintext.Length < n) return false;
        Span<byte> nonce = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(nonce, _receivePrefix);
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], counter);
        try
        {
            _receive.Decrypt(nonce, sealedData.Slice(CounterSize, n), sealedData.Slice(CounterSize + n, TagSize), plaintext[..n], aad);
        }
        catch (AuthenticationTagMismatchException) { return false; }

        if (!_any || counter > _highest)
        {
            ulong shift = _any ? counter - _highest : 64;
            _window = shift >= 64 ? 0 : _window << (int)shift;
            _window |= 1;
            _highest = counter;
            _any = true;
        }
        else _window |= 1UL << (int)(_highest - counter);
        length = n;
        return true;
    }

    public void Dispose()
    {
        _send.Dispose();
        _receive.Dispose();
    }
}

/// <summary>Stateless connect cookies (anti-spoofing) and password proofs.</summary>
public sealed class CookieJar
{
    public const int CookieSize = 8 + 16;
    private readonly byte[] _secret = RandomNumberGenerator.GetBytes(32);

    /// <summary>Cookie = timestamp (8) + HMAC(secret, address || timestamp)[0..16].</summary>
    public void Issue(ReadOnlySpan<byte> address, long unixSeconds, Span<byte> cookie)
    {
        BinaryPrimitives.WriteInt64LittleEndian(cookie, unixSeconds);
        Mac(address, unixSeconds, cookie[8..CookieSize]);
    }

    public bool Verify(ReadOnlySpan<byte> address, ReadOnlySpan<byte> cookie, long nowUnixSeconds, int maxAgeSeconds = 10)
    {
        if (cookie.Length != CookieSize) return false;
        long ts = BinaryPrimitives.ReadInt64LittleEndian(cookie);
        if (ts > nowUnixSeconds + 1 || nowUnixSeconds - ts > maxAgeSeconds) return false;
        Span<byte> expected = stackalloc byte[16];
        Mac(address, ts, expected);
        return CryptographicOperations.FixedTimeEquals(expected, cookie[8..]);
    }

    private void Mac(ReadOnlySpan<byte> address, long ts, Span<byte> dest)
    {
        Span<byte> data = stackalloc byte[address.Length + 8];
        address.CopyTo(data);
        BinaryPrimitives.WriteInt64LittleEndian(data[address.Length..], ts);
        Span<byte> full = stackalloc byte[32];
        HMACSHA256.HashData(_secret, data, full);
        full[..16].CopyTo(dest);
    }

    /// <summary>Proof that the client knows the server password: HMAC-SHA256(SHA256(password), cookie).</summary>
    public static byte[] PasswordProof(string password, ReadOnlySpan<byte> cookie)
    {
        byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return HMACSHA256.HashData(key, cookie);
    }
}
