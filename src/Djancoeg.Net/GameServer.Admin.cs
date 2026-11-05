using System.Globalization;
using System.Net;
using System.Text;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Core.Simulation;

namespace Djancoeg.Net;

/// <summary>Admin console, rcon, votes, bans, persistent stats and the leaderboard.</summary>
public sealed partial class GameServer
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed class Vote
    {
        public required string Description;
        public required Func<MatchSettings> Apply;
        public readonly HashSet<SocketAddress> Yes = [];
        public readonly HashSet<SocketAddress> No = [];
        public double Expires;
    }

    private Vote? _vote;
    private bool _voteDirty;

    public const string HelpText =
        "Commands: status | kick <id|name> [reason] | ban <id|name|ip> [reason] | unban <ip> | bans | map <arena> [mode] | " +
        "mode <ffa|tdm|koth|ctf|race> | bots <n> | skill <0-1> | restart | say <text> | password <text|off> | top [n] | help";

    /// <summary>
    /// Runs an admin command. <paramref name="by"/> is null for the console. Returns the text to show the caller.
    /// Must run on the server thread (console input is queued via <see cref="EnqueueCommand"/>).
    /// </summary>
    public string Execute(string line, string? by) => ExecuteAs(line, by is null ? null : _clients.Values.FirstOrDefault(c => c.Name == by));

    private string ExecuteAs(string line, Client? by)
    {
        var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "";
        string cmd = parts[0].ToLowerInvariant(), arg = parts.Length > 1 ? parts[1] : "";
        string who = by?.Name ?? "console";
        switch (cmd)
        {
            case "help": return HelpText;
            case "status": return Status();
            case "say":
                if (arg.Length == 0) return "Usage: say <text>";
                ServerSay("[" + who + "] " + arg);
                return "";
            case "kick":
            case "ban":
            {
                var (target, reason) = SplitTarget(arg);
                if (cmd == "ban" && IPAddress.TryParse(target, out var ipOnly) && FindClient(target) is null)
                {
                    AddBan(ipOnly.ToString(), reason);
                    return $"Banned {ipOnly}.";
                }
                var c = FindClient(target);
                if (c is null) return $"No player '{target}'. Use 'status' to list players.";
                if (cmd == "ban") AddBan(UdpTransport.ToEndPoint(c.Address).Address.ToString(), $"{c.Name}: {reason}");
                string msg = cmd == "ban" ? $"Banned by {who}" : $"Kicked by {who}";
                if (reason.Length > 0) msg += ": " + reason;
                SendDisconnect(c, cmd == "ban" ? RejectCode.Banned : RejectCode.Kicked, msg);
                Drop(c, cmd == "ban" ? "was banned" : "was kicked");
                ServerSay($"{c.Name} {(cmd == "ban" ? "was banned" : "was kicked")}.");
                return $"{c.Name} {(cmd == "ban" ? "banned" : "kicked")}.";
            }
            case "unban":
                if (!_bans.Remove(arg)) return $"{arg} is not banned.";
                SaveBans();
                return $"Unbanned {arg}.";
            case "bans": return _bans.Count == 0 ? "No bans." : "Banned: " + string.Join(", ", _bans);
            case "map":
            {
                var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0 || _content.ArenaIndexOf(p[0]) < 0) return "Arenas: " + string.Join(", ", _content.Arenas.Select(a => a.Id));
                var settings = World.Settings with { ArenaId = p[0] };
                if (p.Length > 1)
                {
                    if (TryParseMode(p[1], out var m)) settings = settings with { Mode = m };
                    else return "Modes: ffa, tdm, koth, ctf, race";
                }
                ChangeMatch(settings);
                ServerSay($"{who}: next arena {World.Arena.Id} ({World.Settings.Mode}).");
                return "";
            }
            case "mode":
                if (!TryParseMode(arg, out var mode)) return "Modes: ffa, tdm, koth, ctf, race";
                ChangeMatch(World.Settings with { Mode = mode });
                ServerSay($"{who}: mode is now {mode}.");
                return "";
            case "bots":
            {
                if (!int.TryParse(arg, Inv, out int n) || n < 0 || n > SimConstants.MaxPlayers) return "Usage: bots <0-32>";
                SetSettings(World.Settings with { BotCount = n });
                World.BalanceBots();
                return $"Bots: {n}.";
            }
            case "skill":
            {
                if (!float.TryParse(arg, Inv, out float s) || s < 0 || s > 1) return "Usage: skill <0-1>";
                SetSettings(World.Settings with { BotSkill = s });
                return $"Bot skill: {s:0.00} (applies to new bots).";
            }
            case "restart":
                ChangeMatch(World.Settings);
                ServerSay($"{who} restarted the match.");
                return "";
            case "password":
                _config.Password = arg is "" or "off" ? null : arg;
                return PasswordProtected ? "Password set." : "Password removed.";
            case "top":
            {
                int n = int.TryParse(arg, Inv, out int x) ? Math.Clamp(x, 1, 20) : 5;
                var top = Stats.Top(n);
                if (top.Count == 0) return "No stats yet.";
                var sb = new StringBuilder();
                for (int i = 0; i < top.Count; i++) sb.Append(Inv, $"{i + 1}. {top[i].Name} {top[i].Kills}K/{top[i].Deaths}D {top[i].Wins}W  ");
                return sb.ToString().TrimEnd();
            }
            default:
                return $"Unknown command '{cmd}'. {HelpText}";
        }
    }

    /// <summary>Bot count/skill changes apply to the running match without restarting it.</summary>
    private void SetSettings(MatchSettings s) => World.UpdateSettings(s);

    private string Status()
    {
        var sb = new StringBuilder();
        sb.Append(Inv, $"{World.Settings.ServerName} | {World.Arena.Id} | {World.Settings.Mode} | {World.TimeLeft:0}s left | {_clients.Count} clients\n");
        foreach (var c in _clients.Values)
            sb.Append(Inv, $"  [{(c.IsSpectator ? "spec" : c.Slot.ToString(Inv))}] {c.Name} {UdpTransport.ToEndPoint(c.Address)} {c.RttMs:0}ms{(c.IsAdmin ? " admin" : "")}\n");
        return sb.ToString().TrimEnd();
    }

    private static (string target, string reason) SplitTarget(string arg)
    {
        var p = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (p.Length > 0 ? p[0] : "", p.Length > 1 ? p[1] : "");
    }

    private Client? FindClient(string target)
    {
        if (target.Length == 0) return null;
        if (int.TryParse(target, Inv, out int slot)) return _clients.Values.FirstOrDefault(c => c.Slot == slot);
        if (IPAddress.TryParse(target, out var ip)) return _clients.Values.FirstOrDefault(c => UdpTransport.ToEndPoint(c.Address).Address.Equals(ip));
        return _clients.Values.FirstOrDefault(c => c.Name.Equals(target, StringComparison.OrdinalIgnoreCase))
            ?? _clients.Values.FirstOrDefault(c => c.Name.Contains(target, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseMode(string s, out GameModeKind mode)
    {
        mode = s.ToLowerInvariant() switch
        {
            "ffa" or "tawuran" => GameModeKind.FreeForAll,
            "tdm" or "perang" => GameModeKind.TeamDeathmatch,
            "koth" or "raja" => GameModeKind.KingOfTheHill,
            "ctf" or "gorengan" => GameModeKind.CaptureTheFlag,
            "race" or "pinang" => GameModeKind.Race,
            _ => (GameModeKind)255,
        };
        return (byte)mode != 255;
    }

    // ------------------------------------------------------------------ chat commands (rcon, votes, stats)

    private void HandleChatCommand(Client c, string text)
    {
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return;
        string cmd = parts[0].ToLowerInvariant(), arg = parts.Length > 1 ? parts[1] : "";
        switch (cmd)
        {
            case "login":
                if (!string.IsNullOrEmpty(_config.RconPassword) && arg == _config.RconPassword)
                {
                    c.IsAdmin = true;
                    ServerSay("Admin access granted. Type !help for commands.", c);
                    Log($"* {c.Name} logged in as admin");
                }
                else ServerSay("Wrong admin password.", c);
                return;
            case "vote": StartVote(c, arg); return;
            case "yes": case "y": CastVote(c, true); return;
            case "no": case "n": CastVote(c, false); return;
            case "top": case "stats":
                ServerSay(ExecuteAs("top 5", null), c);
                return;
            case "help" when !c.IsAdmin:
                ServerSay("!vote map <arena> | !vote mode <mode> | !vote restart | !yes | !no | !top | !login <password>", c);
                return;
        }
        if (!c.IsAdmin) { ServerSay("Admin only. Use !login <password> first, or !vote.", c); return; }
        string output = ExecuteAs(text, c);
        Log($"* {c.Name}: !{text}");
        if (output.Length > 0) foreach (var line in output.Split('\n')) ServerSay(line, c);
    }

    private int HumanCount => _clients.Values.Count(x => !x.IsSpectator);

    private void StartVote(Client c, string arg)
    {
        if (!_config.AllowVotes) { ServerSay("Voting is disabled on this server.", c); return; }
        if (c.IsSpectator) { ServerSay("Spectators can't start votes.", c); return; }
        if (_vote is not null) { ServerSay("A vote is already running: " + _vote.Description, c); return; }
        var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string kind = p.Length > 0 ? p[0].ToLowerInvariant() : "";
        Vote? v = null;
        if (kind == "map" && p.Length > 1 && _content.ArenaIndexOf(p[1]) >= 0)
        {
            string id = p[1];
            v = new Vote { Description = $"{c.Name}: change arena to {id}", Apply = () => World.Settings with { ArenaId = id } };
        }
        else if (kind == "mode" && p.Length > 1 && TryParseMode(p[1], out var m))
            v = new Vote { Description = $"{c.Name}: change mode to {m}", Apply = () => World.Settings with { Mode = m } };
        else if (kind == "restart")
            v = new Vote { Description = $"{c.Name}: restart the match", Apply = () => World.Settings };
        if (v is null) { ServerSay("Usage: !vote map <arena> | !vote mode <ffa|tdm|koth|ctf|race> | !vote restart", c); return; }
        v.Expires = Now + 30;
        v.Yes.Add(c.Address);
        _vote = v;
        ServerSay("[VOTE] " + v.Description + " - press F1 (yes) / F2 (no), or type !yes / !no");
        Log("[vote] " + v.Description);
        _voteDirty = true;
        UpdateVote();
    }

    private void CastVote(Client c, bool yes)
    {
        if (_vote is null || c.IsSpectator) return;
        _vote.Yes.Remove(c.Address); _vote.No.Remove(c.Address);
        (yes ? _vote.Yes : _vote.No).Add(c.Address);
        _voteDirty = true;
        UpdateVote();
    }

    private void UpdateVote()
    {
        var v = _vote;
        if (v is null)
        {
            if (_voteDirty) { BroadcastVote(null); _voteDirty = false; }
            return;
        }
        int humans = Math.Max(1, HumanCount);
        int needed = humans / 2 + 1;
        int yes = v.Yes.Count(a => _clients.ContainsKey(a)), no = v.No.Count(a => _clients.ContainsKey(a));
        if (yes >= needed)
        {
            _vote = null;
            ServerSay("[VOTE] Passed: " + v.Description);
            BroadcastVote(null);
            ChangeMatch(v.Apply());
            return;
        }
        if (no > humans - needed || Now > v.Expires)
        {
            _vote = null;
            ServerSay("[VOTE] Failed: " + v.Description);
            BroadcastVote(null);
            return;
        }
        BroadcastVote(new VoteInfo(v.Description, yes, no, needed, (int)Math.Max(0, v.Expires - Now)));
    }

    private void BroadcastVote(VoteInfo? info)
    {
        var w = Inner(PacketType.VoteStatus);
        w.Bool(info is not null);
        if (info is not null) { w.Str(info.Description); w.U8((byte)info.Yes); w.U8((byte)info.No); w.U8((byte)info.Needed); w.U8((byte)info.SecondsLeft); }
        foreach (var c in _clients.Values) SendSecure(c, w.Written);
    }


    // ------------------------------------------------------------------ bans

    private string? BansPath => _config.DataDirectory is null ? null : Path.Combine(_config.DataDirectory, "bans.txt");

    private void LoadBans()
    {
        if (BansPath is not { } path || !File.Exists(path)) return;
        foreach (var line in File.ReadAllLines(path))
        {
            var ip = line.Split('#', 2)[0].Trim();
            if (IPAddress.TryParse(ip, out _)) _bans.Add(ip);
        }
    }

    private void AddBan(string ip, string note)
    {
        _bans.Add(ip);
        SaveBans(note.Length > 0 ? (ip, note) : null);
    }

    private void SaveBans((string ip, string note)? newEntry = null)
    {
        if (BansPath is not { } path) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var notes = new Dictionary<string, string>();
            if (File.Exists(path))
                foreach (var line in File.ReadAllLines(path))
                {
                    var p = line.Split('#', 2);
                    if (p.Length == 2) notes[p[0].Trim()] = p[1].Trim();
                }
            if (newEntry is { } e) notes[e.ip] = e.note;
            File.WriteAllLines(path, _bans.Select(b => notes.TryGetValue(b, out var n) ? $"{b} # {n}" : b));
        }
        catch (IOException) { }
    }

    // ------------------------------------------------------------------ stats

    private void TrackStats()
    {
        int n = World.Events.ReadSince(_statsCursor, _statsScratch);
        for (int i = 0; i < n; i++)
        {
            ref readonly var e = ref _statsScratch[i];
            _statsCursor = e.Id;
            switch (e.Type)
            {
                case GameEventType.Kill:
                    if ((e.Flags & (byte)KillFlags.Suicide) == 0) Stats.Update(KeyOf(e.B), s => s.Kills++);
                    Stats.Update(KeyOf(e.A), s => s.Deaths++);
                    break;
                case GameEventType.Streak:
                {
                    int streak = e.Value;
                    Stats.Update(KeyOf(e.A), s => s.BestStreak = Math.Max(s.BestStreak, streak));
                    break;
                }
                case GameEventType.FlagCaptured: Stats.Update(KeyOf(e.A), s => s.Captures++); break;
                case GameEventType.RaceGoal: Stats.Update(KeyOf(e.A), s => s.RaceGoals++); break;
                case GameEventType.MatchEnd: RecordMatchEnd(); break;
            }
        }
        foreach (var c in _clients.Values) if (!c.IsSpectator) c.PlayAccum += Core.SimConstants.TickDelta;
    }

    private string KeyOf(int slot) => (uint)slot < (uint)_bySlot.Length && _bySlot[slot] is { } c ? c.PlayerKey : "";

    private void RecordMatchEnd()
    {
        var w = World;
        foreach (var c in _clients.Values)
        {
            if (c.IsSpectator) continue;
            bool won = w.IsTeamMode ? w.Winner >= 0 && w.Players[c.Slot].Team == w.Winner : w.Winner == c.Slot;
            Stats.Update(c.PlayerKey, s => { s.Matches++; if (won) s.Wins++; });
        }
        FlushPlayTime();
        Stats.Save();
    }

    private void FlushPlayTime()
    {
        foreach (var c in _clients.Values)
        {
            if (c.IsSpectator || c.PlayAccum <= 0) continue;
            double add = c.PlayAccum;
            Stats.Update(c.PlayerKey, s => s.PlaySeconds += add);
            c.PlayAccum = 0;
        }
    }

    private void SendLeaderboard(Client c)
    {
        var top = Stats.Top(10);
        var mine = Stats.Get(c.PlayerKey);
        var w = Inner(PacketType.LeaderboardResponse);
        bool includeMe = mine is not null && !top.Contains(mine);
        w.U8((byte)(top.Count + (includeMe ? 1 : 0)));
        for (int i = 0; i < top.Count; i++) WriteEntry(ref w, i + 1, top[i], top[i] == mine);
        if (includeMe) WriteEntry(ref w, Stats.RankOf(c.PlayerKey), mine!, true);
        SendSecure(c, w.Written);
    }

    private static void WriteEntry(ref NetWriter w, int rank, PlayerStats s, bool you)
    {
        w.U16((ushort)rank); w.Str(s.Name);
        w.U32((uint)s.Kills); w.U32((uint)s.Deaths); w.U16((ushort)s.Wins); w.U16((ushort)s.Matches);
        w.U16((ushort)s.Captures); w.U16((ushort)s.RaceGoals); w.U8((byte)Math.Min(255, s.BestStreak));
        w.U32((uint)(s.PlaySeconds / 60)); w.Bool(you);
    }
}
