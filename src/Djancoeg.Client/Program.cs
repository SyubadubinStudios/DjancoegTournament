using System.Globalization;
using Djancoeg.Client;

// Djancoeg Tournament - Ultimate Indonesian Chaos Battle Arena.
// Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.
var opts = new AppOptions();
for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : "";
    switch (args[i])
    {
        case "--shot": opts = opts with { Shot = Next() }; break;
        case "--out": opts = opts with { ShotOut = Next() }; break;
        case "--delay": opts = opts with { ShotDelay = float.Parse(Next(), CultureInfo.InvariantCulture) }; break;
        case "--arena": opts = opts with { Arena = Next() }; break;
        case "--connect": opts = opts with { Connect = Next() }; break;
        case "--lang": opts = opts with { Language = Next() }; break;
        case "--novsync": opts = opts with { NoVsync = true }; break;
        case "--size":
        {
            var p = Next().Split('x');
            opts = opts with { Width = int.Parse(p[0], CultureInfo.InvariantCulture), Height = int.Parse(p[1], CultureInfo.InvariantCulture) };
            break;
        }
        case "--help":
            Console.WriteLine("DjancoegTournament [--connect host:port] [--lang en|id] [--size 1600x900] [--shot scene --out file.png [--arena id] [--delay s]]");
            return 0;
    }
}
using var app = new App(opts);
app.Run();
return 0;
