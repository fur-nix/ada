using System.Runtime.InteropServices;
using Ada.Core;
using Ada.Data;
using Vortice.Direct2D1;

namespace Ada.Services;

/// <summary>Başlat menüsündeki uygulamalar (arama için) — arka planda bir kez taranır.</summary>
public sealed class AppIndex
{
    public sealed record Entry(string Name, string Path, string Kind);
    public IReadOnlyList<Entry> All { get; private set; } = Array.Empty<Entry>();
    bool _loading;

    public void EnsureLoaded()
    {
        if (_loading || All.Count > 0) return;
        _loading = true;
        Task.Run(() =>
        {
            var list = new List<Entry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                         Environment.GetFolderPath(Environment.SpecialFolder.Programs)
                     })
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                    {
                        var name = Path.GetFileNameWithoutExtension(f);
                        var low = name.ToLowerInvariant();
                        if (low.Contains("uninstall") || low.Contains("kaldır") || low.Contains("readme") || low.Contains("help")) continue;
                        if (!seen.Add(name)) continue;
                        var folder = Path.GetFileName(Path.GetDirectoryName(f)) ?? "";
                        list.Add(new Entry(name, f, folder.Contains("game", StringComparison.OrdinalIgnoreCase) || folder.Contains("oyun", StringComparison.OrdinalIgnoreCase) ? "Oyun" : "Uygulama"));
                    }
                }
                catch { }
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            Dispatcher.Post(() => { All = list; _loading = false; });
        });
    }

    /// <summary>Basit bulanık arama: başlangıç eşleşmesi > kelime başı > içerir.</summary>
    public static List<Entry> Search(IReadOnlyList<Entry> all, string q, int max)
    {
        q = q.Trim();
        if (q.Length == 0) return all.Take(max).ToList();
        var tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
        return all
            .Select(e => (e, score: Score(e.Name, q, tr)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score).ThenBy(x => x.e.Name.Length)
            .Take(max).Select(x => x.e).ToList();
    }

    public static int Score(string name, string q, System.Globalization.CultureInfo tr)
    {
        var cmp = tr.CompareInfo;
        const System.Globalization.CompareOptions o = System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace;
        if (cmp.IsPrefix(name, q, o)) return 3;
        foreach (var w in name.Split(' ', '-', '.', '_'))
            if (w.Length > 0 && cmp.IsPrefix(w, q, o)) return 2;
        return cmp.IndexOf(name, q, o) >= 0 ? 1 : 0;
    }
}

/// <summary>Duvar kağıdı klasörü, küçük resimler ve masaüstüne uygulama.</summary>
public sealed class WallpaperService
{
    readonly Gfx _g;
    public List<string> Files { get; } = new();
    readonly Dictionary<string, ID2D1Bitmap?> _thumbs = new();
    bool _scanned;

    public WallpaperService(Gfx g) => _g = g;

    public string Folder
    {
        get
        {
            var f = Store.Current.WallpaperFolder;
            if (!string.IsNullOrEmpty(f) && Directory.Exists(f)) return f;
            var pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            foreach (var c in new[] { "Wallpapers", "Duvar Kağıtları", "Duvar kağıtları" })
                if (Directory.Exists(Path.Combine(pics, c))) return Path.Combine(pics, c);
            var win = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Web\Wallpaper");
            return Directory.Exists(win) ? win : pics;
        }
    }

    public void Scan(bool force = false)
    {
        if (_scanned && !force) return;
        _scanned = true;
        Files.Clear();
        foreach (var b in _thumbs.Values) b?.Dispose();
        _thumbs.Clear();
        try
        {
            Files.AddRange(Directory.EnumerateFiles(Folder, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                            || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                .Take(40));
        }
        catch (Exception e) { Log.Error("Wallpaper.Scan", e); }
    }

    /// <summary>Küçük resim; kare başına sınırlı sayıda çözülür (UI donmasın). null + pending → sonra tekrar dene.</summary>
    public ID2D1Bitmap? Thumb(string file, ref int budget, out bool pending)
    {
        pending = false;
        if (_thumbs.TryGetValue(file, out var b)) return b;
        if (budget <= 0) { pending = true; return null; }
        budget--;
        try { b = _g.LoadImage(File.ReadAllBytes(file), (int)(420 * _g.Scale), out _); }
        catch { b = null; }
        _thumbs[file] = b;
        return b;
    }

    /// <summary>Panel kapanınca küçük resimleri bırak (bellek).</summary>
    public void Release()
    {
        foreach (var b in _thumbs.Values) b?.Dispose();
        _thumbs.Clear();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SystemParametersInfoW(uint action, uint param, string? pv, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW")] static extern unsafe bool SystemParametersInfoGet(uint action, uint param, char* pv, uint flags);

    public static unsafe string Current()
    {
        char* buf = stackalloc char[520];
        return SystemParametersInfoGet(0x73, 520, buf, 0) ? new string(buf) : "";
    }

    public static void Apply(string file) =>
        Task.Run(() => SystemParametersInfoW(0x14, 0, file, 0x1 | 0x2)); // SPI_SETDESKWALLPAPER, kaydet + yayınla
}
