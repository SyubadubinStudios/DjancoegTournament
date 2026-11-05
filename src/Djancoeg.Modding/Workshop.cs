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
/// read-only HTTP/1.1 server: <c>GET /index.json</c> and <c>GET /packages/&lt;file&gt;.djmod</c>. No dependencies.
/// </summary>
public sealed class WorkshopServer : IDisposable
{
    public const int DefaultPort = 27951;
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

    private static async Task Respond(NetworkStream stream, int code, string type, byte[] body, CancellationToken token, bool headOnly = false)
    {
        string reason = code switch { 200 => "OK", 404 => "Not Found", 405 => "Method Not Allowed", _ => "Error" };
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\nAccess-Control-Allow-Origin: *\r\n\r\n");
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        if (!headOnly) await stream.WriteAsync(body, token).ConfigureAwait(false);
    }

    /// <summary>Validates a package and adds (or updates) it in the Workshop folder's index.</summary>
    public static WorkshopEntry Publish(string packagePath, string directory)
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
            Updated = DateTime.UtcNow,
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

    private static Uri Combine(string baseUrl, string path) => new(new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/"), path);
}
