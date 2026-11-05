using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Djancoeg.Core.Localization;

namespace Djancoeg.Modding;

/// <summary>One published package in an online Workshop index.</summary>
public sealed record WorkshopEntry
{
    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = new();
    public LocalizedText Description { get; set; } = new();
    public string Author { get; set; } = "";
    public string Version { get; set; } = "";
    public string File { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public int Weapons { get; set; }
    public int Characters { get; set; }
    public int Arenas { get; set; }
    public DateTime Updated { get; set; }
    /// <summary>Name of the upload token that first published this package (only it may update it).</summary>
    public string UploadedBy { get; set; } = "";
}

public sealed record WorkshopIndex
{
    public string Name { get; set; } = "Djancoeg Workshop";
    public List<WorkshopEntry> Packages { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(WorkshopIndex))]
public sealed partial class WorkshopJsonContext : JsonSerializerContext;

/// <summary>
/// Publishes .djmod packages into a Workshop folder (packages + index.json) and serves it over a minimal
/// HTTP/1.1 server: <c>GET /index.json</c>, <c>GET /packages/&lt;file&gt;.djmod</c> and, for holders of an upload
/// token listed in <c>tokens.txt</c> (<c>name:secret</c> per line), <c>POST /upload</c> with the package as the body.
/// No dependencies.
/// </summary>
public sealed class WorkshopServer : IDisposable
{
    public const int DefaultPort = 27951;
    public const int MaxUploadBytes = 32 * 1024 * 1024;
    public const string TokensFile = "tokens.txt";
    private readonly string _dir;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public Action<string>? Log { get; set; }

    public WorkshopServer(string directory, int port = DefaultPort, IPAddress? bind = null)
    {
        _dir = Path.GetFullPath(directory);
        Directory.CreateDirectory(Path.Combine(_dir, "packages"));
        if (!File.Exists(IndexPath(_dir))) WriteIndex(_dir, new WorkshopIndex());
        _listener = new TcpListener(bind ?? IPAddress.Any, port);
    }

    public static string IndexPath(string dir) => Path.Combine(dir, "index.json");

    public void Start()
    {
        _listener.Start();
        _loop = Task.Run(() => AcceptLoop(_cts.Token));
        Log?.Invoke($"Workshop serving {_dir} on http://0.0.0.0:{Port}/index.json");
    }

    private async Task AcceptLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }
            _ = Task.Run(() => Handle(client, token), token);
        }
    }

    private async Task Handle(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 5000;
                var stream = client.GetStream();
                var buf = new byte[4096];
                int n = 0;
                // Read the request head (we never accept bodies).
                while (n < buf.Length)
                {
                    int r = await stream.ReadAsync(buf.AsMemory(n), token).ConfigureAwait(false);
                    if (r <= 0) break;
                    n += r;
                    if (Encoding.ASCII.GetString(buf, 0, n).Contains("\r\n\r\n", StringComparison.Ordinal)) break;
                }
                var head = Encoding.ASCII.GetString(buf, 0, n);
                var line = head.Split("\r\n", 2)[0].Split(' ');
                if (line.Length >= 2 && line[0] == "POST" && line[1].Split('?')[0] == "/upload")
                {
                    await HandleUpload(stream, buf, n, head, token).ConfigureAwait(false);
                    return;
                }
                if (line.Length < 2 || (line[0] != "GET" && line[0] != "HEAD")) { await Respond(stream, 405, "text/plain", "Method not allowed"u8.ToArray(), token).ConfigureAwait(false); return; }
                string path = Uri.UnescapeDataString(line[1].Split('?')[0]);
                bool headOnly = line[0] == "HEAD";
                if (path is "/" or "/index.json")
                {
                    await Respond(stream, 200, "application/json", await File.ReadAllBytesAsync(IndexPath(_dir), token).ConfigureAwait(false), token, headOnly).ConfigureAwait(false);
                    return;
                }
                if (path.StartsWith("/packages/", StringComparison.Ordinal))
                {
                    string file = path["/packages/".Length..];
                    // Only plain package file names: no separators, no traversal.
                    if (file.Length == 0 || file.Contains('/') || file.Contains('\\') || file.Contains("..", StringComparison.Ordinal) || !file.EndsWith(ModLoader.PackageExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        await Respond(stream, 404, "text/plain", "Not found"u8.ToArray(), token).ConfigureAwait(false);
                        return;
                    }
                    string full = Path.Combine(_dir, "packages", file);
                    if (!File.Exists(full)) { await Respond(stream, 404, "text/plain", "Not found"u8.ToArray(), token).ConfigureAwait(false); return; }
                    await Respond(stream, 200, "application/zip", await File.ReadAllBytesAsync(full, token).ConfigureAwait(false), token, headOnly).ConfigureAwait(false);
                    return;
                }
                await Respond(stream, 404, "text/plain", "Not found"u8.ToArray(), token).ConfigureAwait(false);
            }
            catch (IOException) { }
            catch (OperationCanceledException) { }
        }
    }

    private async Task HandleUpload(NetworkStream stream, byte[] buf, int n, string head, CancellationToken token)
    {
        string? Header(string name)
        {
            foreach (var l in head.Split("\r\n"))
            {
                int c = l.IndexOf(':');
                if (c > 0 && l[..c].Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) return l[(c + 1)..].Trim();
            }
            return null;
        }
        var auth = Header("Authorization");
        string? uploader = auth is not null && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? CheckToken(_dir, auth[7..].Trim()) : null;
        if (uploader is null) { await Respond(stream, 401, "text/plain", "Upload token missing or not valid."u8.ToArray(), token).ConfigureAwait(false); return; }
        if (!long.TryParse(Header("Content-Length"), out long length) || length <= 0 || length > MaxUploadBytes)
        {
            await Respond(stream, 413, "text/plain", Encoding.UTF8.GetBytes($"Package must be 1 byte to {MaxUploadBytes / 1024 / 1024} MB."), token).ConfigureAwait(false);
            return;
        }
        // Part of the body may already be in the header buffer.
        int bodyStart = head.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
        var body = new byte[length];
        int have = Math.Min(n - bodyStart, (int)length);
        Array.Copy(buf, bodyStart, body, 0, have);
        while (have < length)
        {
            int r = await stream.ReadAsync(body.AsMemory(have), token).ConfigureAwait(false);
            if (r <= 0) break;
            have += r;
        }
        if (have < length) { await Respond(stream, 400, "text/plain", "Upload was cut off."u8.ToArray(), token).ConfigureAwait(false); return; }
        var tmp = Path.Combine(Path.GetTempPath(), "djancoeg-upload-" + Guid.NewGuid().ToString("N") + ModLoader.PackageExtension);
        try
        {
            await File.WriteAllBytesAsync(tmp, body, token).ConfigureAwait(false);
            WorkshopEntry e;
            lock (_publishLock) e = Publish(tmp, _dir, uploader);
            Log?.Invoke($"Upload by {uploader}: {e.Id} v{e.Version}");
            await Respond(stream, 200, "application/json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new WorkshopIndex { Name = "upload", Packages = [e] }, WorkshopJsonContext.Default.WorkshopIndex)), token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            await Respond(stream, ex is UnauthorizedAccessException ? 409 : 400, "text/plain", Encoding.UTF8.GetBytes(ex.Message), token).ConfigureAwait(false);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    private readonly object _publishLock = new();

    /// <summary>Returns the token's name when <paramref name="secret"/> matches a line in tokens.txt (constant-time compare).</summary>
    public static string? CheckToken(string directory, string secret)
    {
        var path = Path.Combine(directory, TokensFile);
        if (!File.Exists(path) || secret.Length == 0) return null;
        var given = Encoding.UTF8.GetBytes(secret);
        string? match = null;
        foreach (var raw in File.ReadAllLines(path))
        {
            var l = raw.Trim();
            int c = l.IndexOf(':');
            if (l.Length == 0 || l[0] == '#' || c <= 0) continue;
            var expected = Encoding.UTF8.GetBytes(l[(c + 1)..]);
            if (expected.Length == given.Length && CryptographicOperations.FixedTimeEquals(expected, given)) match = l[..c];
        }
        return match;
    }

    /// <summary>Creates a random upload token for <paramref name="name"/> and appends it to tokens.txt. Returns the secret.</summary>
    public static string AddToken(string directory, string name)
    {
        if (name.Length == 0 || name.Contains(':') || name.Contains('\n')) throw new ArgumentException("Token name must be non-empty and contain no ':'.", nameof(name));
        Directory.CreateDirectory(directory);
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        File.AppendAllText(Path.Combine(directory, TokensFile), $"{name}:{secret}{Environment.NewLine}");
        return secret;
    }

    private static async Task Respond(NetworkStream stream, int code, string type, byte[] body, CancellationToken token, bool headOnly = false)
    {
        string reason = code switch { 200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 404 => "Not Found", 405 => "Method Not Allowed", 409 => "Conflict", 413 => "Payload Too Large", _ => "Error" };
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\nAccess-Control-Allow-Origin: *\r\n\r\n");
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        if (!headOnly) await stream.WriteAsync(body, token).ConfigureAwait(false);
    }

    /// <summary>Validates a package and adds (or updates) it in the Workshop folder's index.</summary>
    public static WorkshopEntry Publish(string packagePath, string directory, string uploadedBy = "")
    {
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(Path.Combine(directory, "packages"));
        ModManifest manifest;
        using (var zip = ZipFile.OpenRead(packagePath))
        {
            var entry = zip.GetEntry(ModLoader.ManifestFile) ?? throw new InvalidDataException($"Package has no {ModLoader.ManifestFile}.");
            using var s = entry.Open();
            manifest = JsonSerializer.Deserialize(s, ModJsonContext.Default.ModManifest) ?? throw new InvalidDataException("Empty manifest.");
        }
        var errors = ModLoader.Validate(manifest).ToList();
        if (errors.Count > 0) throw new InvalidDataException("Mod is not valid: " + string.Join("; ", errors));

        var existing = ReadIndex(directory).Packages.FirstOrDefault(p => p.Id == manifest.Id);
        if (uploadedBy.Length > 0 && existing is { UploadedBy.Length: > 0 } && existing.UploadedBy != uploadedBy)
            throw new UnauthorizedAccessException($"'{manifest.Id}' belongs to another uploader.");
        if (uploadedBy.Length == 0 && existing is not null) uploadedBy = existing.UploadedBy;

        string fileName = $"{manifest.Id}-{manifest.Version}{ModLoader.PackageExtension}";
        string dest = Path.Combine(directory, "packages", fileName);
        File.Copy(packagePath, dest, overwrite: true);
        var bytes = File.ReadAllBytes(dest);
        var index = ReadIndex(directory);
        index.Packages.RemoveAll(p => p.Id == manifest.Id);
        var e = new WorkshopEntry
        {
            Id = manifest.Id, Name = manifest.Name, Description = manifest.Description, Author = manifest.Author, Version = manifest.Version,
            File = fileName, Size = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            Weapons = manifest.Weapons.Count, Characters = manifest.Characters.Count, Arenas = manifest.Arenas.Count + manifest.ArenaFiles.Count,
            Updated = DateTime.UtcNow, UploadedBy = uploadedBy,
        };
        index.Packages.Add(e);
        index.Packages.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        WriteIndex(directory, index);
        return e;
    }

    public static WorkshopIndex ReadIndex(string directory)
    {
        var path = IndexPath(directory);
        if (!File.Exists(path)) return new WorkshopIndex();
        return JsonSerializer.Deserialize(File.ReadAllText(path), WorkshopJsonContext.Default.WorkshopIndex) ?? new WorkshopIndex();
    }

    private static void WriteIndex(string directory, WorkshopIndex index) =>
        File.WriteAllText(IndexPath(directory), JsonSerializer.Serialize(index, WorkshopJsonContext.Default.WorkshopIndex));

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        try { _loop?.Wait(1000); } catch (AggregateException) { }
        _cts.Dispose();
    }
}

/// <summary>Downloads Workshop packages over HTTP(S), verifies their SHA-256 and installs them.</summary>
public sealed class WorkshopClient(HttpClient http)
{
    public async Task<WorkshopIndex> FetchIndexAsync(string baseUrl, CancellationToken token = default)
    {
        var json = await http.GetStringAsync(Combine(baseUrl, "index.json"), token).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, WorkshopJsonContext.Default.WorkshopIndex) ?? new WorkshopIndex();
    }

    /// <summary>Downloads, verifies and installs one package. Throws <see cref="InvalidDataException"/> on a hash mismatch.</summary>
    public async Task<ModInfo> InstallAsync(string baseUrl, WorkshopEntry entry, string modsDir, CancellationToken token = default)
    {
        if (entry.File.Contains('/') || entry.File.Contains('\\') || entry.File.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("Unsafe package name.");
        var bytes = await http.GetByteArrayAsync(Combine(baseUrl, "packages/" + Uri.EscapeDataString(entry.File)), token).ConfigureAwait(false);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Download is corrupted (SHA-256 mismatch).");
        var tmp = Path.Combine(Path.GetTempPath(), "djancoeg-" + Guid.NewGuid().ToString("N") + ModLoader.PackageExtension);
        await File.WriteAllBytesAsync(tmp, bytes, token).ConfigureAwait(false);
        try { return ModLoader.Install(tmp, modsDir); }
        finally { File.Delete(tmp); }
    }

    /// <summary>Uploads a .djmod to a Workshop server. Throws <see cref="HttpRequestException"/> with the server's message on failure.</summary>
    public async Task<WorkshopEntry> UploadAsync(string baseUrl, string packagePath, string uploadToken, CancellationToken token = default)
    {
        using var content = new ByteArrayContent(await File.ReadAllBytesAsync(packagePath, token).ConfigureAwait(false));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        using var req = new HttpRequestMessage(HttpMethod.Post, Combine(baseUrl, "upload")) { Content = content };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", uploadToken);
        using var res = await http.SendAsync(req, token).ConfigureAwait(false);
        var text = await res.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"{(int)res.StatusCode}: {text}", null, res.StatusCode);
        var idx = JsonSerializer.Deserialize(text, WorkshopJsonContext.Default.WorkshopIndex);
        return idx?.Packages.FirstOrDefault() ?? throw new InvalidDataException("Unexpected server reply.");
    }

    private static Uri Combine(string baseUrl, string path) => new(new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/"), path);
}
