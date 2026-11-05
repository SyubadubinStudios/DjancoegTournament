using System.Globalization;
using System.Net;
using System.Text.Json;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Modding;
using Djancoeg.Net;

namespace Djancoeg.Server;

/// <summary>Dedicated server, master server and modding command-line tools.</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        PrintBanner();
        if (args.Length > 0 && args[0] is "--help" or "-h" or "help") { PrintHelp(); return 0; }
        try
        {
            return args.Length > 0 && args[0] == "master" ? RunMaster(args[1..])
                 : args.Length > 0 && args[0] == "mod" ? RunModTool(args[1..])
                 : args.Length > 0 && args[0] == "workshop" ? RunWorkshop(args[1..])
                 : RunGameServer(args);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or IOException or InvalidDataException or System.Net.Sockets.SocketException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("  ____  _                                         ");
        Console.WriteLine(" |  _ \\(_) __ _ _ __   ___ ___   ___  __ _       ");
        Console.WriteLine(" | | | | |/ _` | '_ \\ / __/ _ \\ / _ \\/ _` |      ");
        Console.WriteLine(" | |_| | | (_| | | | | (_| (_) |  __/ (_| |      ");
        Console.WriteLine(" |____// |\\__,_|_| |_|\\___\\___/ \\___|\\__, | TOURNAMENT");
        Console.WriteLine("     |__/                            |___/        ");
        Console.ResetColor();
        Console.WriteLine($" {GameInfo.Tagline} - dedicated server v{GameInfo.Version}");
        Console.WriteLine($" {GameInfo.CreditId}");
        Console.WriteLine($" {GameInfo.CreditEn}");
        Console.WriteLine();
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
        Usage:
          djancoeg-server [options]                 Run a dedicated game server
          djancoeg-server master [--port N]         Run a master server (online server list)
          djancoeg-server mod new <id>              Create a mod template in ./mods/<id>
          djancoeg-server mod validate <dir>        Check a mod folder for errors
          djancoeg-server mod pack <dir> [outDir]   Package a mod as <id>-<version>.djmod
          djancoeg-server mod install <file.djmod>  Install a packaged mod into ./mods
          djancoeg-server mod list                  List installed mods
          djancoeg-server workshop serve [--dir d] [--port 27951]   Serve an online Workshop over HTTP
          djancoeg-server workshop publish <file.djmod> [--dir d]   Add a package to a Workshop folder

        Game server options:
          --config <file.json>   Load MatchSettings from JSON (see docs/en/server.md)
          --port <n>             UDP port (default 27960)
          --name <text>          Server name shown in the browser
          --arena <id>           pasar-malam | terminal-bus | sawah-ronda | atap-kampung | pelabuhan | stasiun-kereta | kantor-kelurahan | <mod arena>
          --rotation <a,b,c>     Arena rotation after each match
          --mode <m>             ffa | tdm | koth | ctf (Rebutan Gorengan) | race (Panjat Pinang)
          --score <n>            Score limit (frags / team frags / zone seconds)
          --time <seconds>       Time limit (0 = none)
          --bots <n>             Bots to fill the server with
          --skill <0..1>         Bot skill
          --max <n>              Max players (2-32)
          --friendly-fire        Enable team damage in tdm
          --master <host:port>   Advertise to a master server (online play)
          --mods <dir>           Mods folder (default ./mods)
          --no-mods              Run with built-in content only
          --password <text>      Require a password to join
          --rcon <text>          Admin password for "!login" in chat
          --data <dir>           Folder for stats.json and bans.txt (default ./data)
          --no-votes             Disable player votes (!vote)
          --no-creeps            Disable Arena Creeps (random monsters)

        While running, type admin commands into the console (help, status, kick, ban, map, mode, bots, say, top...).
        All traffic after the handshake is encrypted (ECDH P-256 + AES-256-GCM).
        """);
    }

    private static int RunGameServer(string[] args)
    {
        var opts = ParseOptions(args);
        var match = new MatchSettings();
        if (opts.TryGetValue("config", out var cfgPath))
            match = JsonSerializer.Deserialize(File.ReadAllText(cfgPath), ModJsonContext.Default.MatchSettings) ?? match;

        match = match with
        {
            ServerName = opts.GetValueOrDefault("name", match.ServerName),
            ArenaId = opts.GetValueOrDefault("arena", match.ArenaId),
            Mode = opts.TryGetValue("mode", out var m) ? ParseMode(m) : match.Mode,
            ScoreLimit = Int(opts, "score", match.ScoreLimit),
            TimeLimitSeconds = Int(opts, "time", match.TimeLimitSeconds),
            BotCount = Int(opts, "bots", match.BotCount),
            BotSkill = opts.TryGetValue("skill", out var sk) ? float.Parse(sk, CultureInfo.InvariantCulture) : match.BotSkill,
            MaxPlayers = Math.Clamp(Int(opts, "max", match.MaxPlayers), 2, SimConstants.MaxPlayers),
            FriendlyFire = opts.ContainsKey("friendly-fire") || match.FriendlyFire,
            Creeps = !opts.ContainsKey("no-creeps") && match.Creeps,
        };

        var content = ContentRegistry.CreateBuiltIn();
        if (!opts.ContainsKey("no-mods"))
            ModLoader.ApplyEnabled(content, opts.GetValueOrDefault("mods", ModLoader.DefaultModsDirectory), Console.WriteLine);
        if (content.ArenaIndexOf(match.ArenaId) < 0)
            throw new ArgumentException($"Unknown arena '{match.ArenaId}'. Available: {string.Join(", ", content.Arenas.Select(a => a.Id))}");

        var config = new ServerConfig
        {
            Match = match,
            Port = Int(opts, "port", Protocol.DefaultPort),
            ArenaRotation = opts.TryGetValue("rotation", out var rot) ? [.. rot.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)] : [],
            MasterServer = opts.GetValueOrDefault("master"),
            Password = opts.GetValueOrDefault("password"),
            RconPassword = opts.GetValueOrDefault("rcon"),
            DataDirectory = opts.GetValueOrDefault("data", Path.Combine(Environment.CurrentDirectory, "data")),
            AllowVotes = !opts.ContainsKey("no-votes"),
            Log = msg => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}"),
        };
        foreach (var a in config.ArenaRotation)
            if (content.ArenaIndexOf(a) < 0) throw new ArgumentException($"Unknown arena in rotation: '{a}'.");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        using var server = new GameServer(content, config);
        Console.WriteLine($"Content signature {content.Signature:X8}. Type 'help' for admin commands, Ctrl+C to stop.");
        // Admin console: lines from stdin are queued and executed on the server thread.
        var console = new Thread(() =>
        {
            try
            {
                string? line;
                while (!cts.IsCancellationRequested && (line = Console.ReadLine()) is not null)
                {
                    if (line.Trim() is "quit" or "exit") { cts.Cancel(); break; }
                    if (line.Trim().Length > 0) server.EnqueueCommand(line);
                }
            }
            catch (IOException) { }
        }) { IsBackground = true, Name = "AdminConsole" };
        console.Start();
        server.Run(cts.Token);
        server.Stop();
        Console.WriteLine("Server stopped. Sampai jumpa!");
        return 0;
    }

    private static int RunMaster(string[] args)
    {
        var opts = ParseOptions(args);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        using var master = new MasterServer(Int(opts, "port", Protocol.DefaultMasterPort), msg => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}"));
        master.Run(cts.Token);
        return 0;
    }

    private static int RunModTool(string[] args)
    {
        if (args.Length == 0) { PrintHelp(); return 1; }
        string modsDir = Path.Combine(Environment.CurrentDirectory, "mods");
        switch (args[0])
        {
            case "new" when args.Length > 1:
                Console.WriteLine($"Created {ModLoader.Scaffold(modsDir, args[1])}");
                return 0;
            case "validate" when args.Length > 1:
            {
                var info = ModLoader.Load(args[1]);
                if (info.IsValid)
                {
                    var m = info.Manifest!;
                    Console.WriteLine($"OK  {m.Id} v{m.Version}: {m.Weapons.Count} weapons, {m.Characters.Count} characters, {m.Arenas.Count} arenas");
                    return 0;
                }
                foreach (var e in info.Errors) Console.WriteLine($"ERR {e}");
                return 2;
            }
            case "pack" when args.Length > 1:
                Console.WriteLine($"Packed {ModLoader.Pack(args[1], args.Length > 2 ? args[2] : Environment.CurrentDirectory)}");
                return 0;
            case "install" when args.Length > 1:
            {
                var info = ModLoader.Install(args[1], modsDir);
                Console.WriteLine(info.IsValid ? $"Installed {info.Id} into {info.Directory}" : $"Installed with errors: {string.Join("; ", info.Errors)}");
                return info.IsValid ? 0 : 2;
            }
            case "list":
                foreach (var mod in ModLoader.Discover(modsDir))
                    Console.WriteLine($"{(mod.Enabled ? "[on] " : "[off]")} {mod.Id,-24} {(mod.IsValid ? "ok" : "INVALID: " + string.Join("; ", mod.Errors))}");
                return 0;
            default:
                PrintHelp();
                return 1;
        }
    }

    private static int RunWorkshop(string[] args)
    {
        if (args.Length == 0) { PrintHelp(); return 1; }
        var opts = ParseOptions(args.Skip(args[0] == "publish" ? 2 : 1).ToArray());
        string dir = opts.GetValueOrDefault("dir", Path.Combine(Environment.CurrentDirectory, "workshop"));
        switch (args[0])
        {
            case "publish" when args.Length > 1:
            {
                var e = WorkshopServer.Publish(args[1], dir);
                Console.WriteLine($"Published {e.Id} v{e.Version} ({e.Size / 1024} KB, sha256 {e.Sha256[..12]}...) to {dir}");
                return 0;
            }
            case "serve":
            {
                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, ev) => { ev.Cancel = true; cts.Cancel(); };
                using var ws = new WorkshopServer(dir, Int(opts, "port", WorkshopServer.DefaultPort)) { Log = Console.WriteLine };
                ws.Start();
                Console.WriteLine($"{WorkshopServer.ReadIndex(dir).Packages.Count} package(s). Press Ctrl+C to stop.");
                cts.Token.WaitHandle.WaitOne();
                return 0;
            }
            default:
                PrintHelp();
                return 1;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Unexpected argument '{args[i]}'. Use --help.");
            string key = args[i][2..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) d[key] = args[++i];
            else d[key] = "true";
        }
        return d;
    }

    private static int Int(Dictionary<string, string> o, string key, int fallback) =>
        o.TryGetValue(key, out var v) ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;

    private static GameModeKind ParseMode(string m) => m.ToLowerInvariant() switch
    {
        "ffa" or "tawuran" or "freeforall" => GameModeKind.FreeForAll,
        "tdm" or "perang" or "team" => GameModeKind.TeamDeathmatch,
        "koth" or "raja" or "kingofthehill" => GameModeKind.KingOfTheHill,
        "ctf" or "gorengan" or "capturetheflag" => GameModeKind.CaptureTheFlag,
        "race" or "pinang" or "panjat" => GameModeKind.Race,
        _ => throw new ArgumentException($"Unknown mode '{m}'. Use ffa, tdm, koth, ctf or race."),
    };
}
