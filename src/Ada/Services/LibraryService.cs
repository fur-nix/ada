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
        // Türkçe karakterler katlanır (ö→o, ı→i, ç→c…): "gorev" → "Görev Yöneticisi" bulunur
        var n = Fold(name); var f = Fold(q);
        if (f.Length == 0) return 0;
        if (n.StartsWith(f, StringComparison.Ordinal)) return 3;
        int i = n.IndexOf(f, StringComparison.Ordinal);
        if (i < 0) return 0;
        // kelime başında mı?
        for (; i >= 0; i = n.IndexOf(f, i + 1, StringComparison.Ordinal))
            if (i == 0 || n[i - 1] is ' ' or '-' or '.' or '_' or '(') return 2;
        return 1;
    }

    /// <summary>Küçük harf + aksansız (ı/İ → i).</summary>
    public static string Fold(string s)
    {
        Span<char> buf = s.Length <= 256 ? stackalloc char[s.Length] : new char[s.Length];
        int k = 0;
        foreach (var ch in s)
        {
            char c = ch switch
            {
                'ı' or 'İ' or 'I' => 'i', 'ğ' or 'Ğ' => 'g', 'ü' or 'Ü' => 'u', 'ş' or 'Ş' => 's',
                'ö' or 'Ö' => 'o', 'ç' or 'Ç' => 'c', 'â' or 'Â' => 'a', 'î' or 'Î' => 'i', 'û' or 'Û' => 'u',
                _ => char.ToLowerInvariant(ch)
            };
            buf[k++] = c;
        }
        return new string(buf[..k]);
    }
}

/// <summary>
/// Spotlight dosya araması: kullanıcı klasörlerinin (Masaüstü, Belgeler, İndirilenler, Resimler, Müzik, Videolar) adları.
/// İlk kullanımda arka planda taranır; 5 dk kullanılmazsa bellekten atılır.
/// </summary>
public sealed class FileIndex
{
    public sealed record Entry(string Name, string Path, string Folder);
    public IReadOnlyList<Entry> All { get; private set; } = Array.Empty<Entry>();
    public bool Loading { get; private set; }
    DateTime _lastUse = DateTime.Now;
    const int MaxFiles = 20000;
    static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    { "node_modules", ".git", "bin", "obj", ".vs", "__pycache__", ".venv", "venv", "AppData", "$RECYCLE.BIN", ".cache", "packages" };

    public void EnsureLoaded()
    {
        _lastUse = DateTime.Now;
        if (Loading || All.Count > 0) return;
        Loading = true;
        Task.Run(() =>
        {
            var list = new List<Entry>(4096);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(home, "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            };
            foreach (var root in roots.Distinct()) Walk(root, 0, list);
            Dispatcher.Post(() => { All = list; Loading = false; });
        });
    }

    static void Walk(string dir, int depth, List<Entry> list)
    {
        if (list.Count >= MaxFiles || depth > 5 || !Directory.Exists(dir)) return;
        var folder = System.IO.Path.GetFileName(dir);
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                var name = System.IO.Path.GetFileName(f);
                if (name.StartsWith('.') || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || name == "desktop.ini") continue;
                list.Add(new Entry(name, f, folder));
                if (list.Count >= MaxFiles) return;
            }
            foreach (var d in Directory.EnumerateDirectories(dir))
            {
                var n = System.IO.Path.GetFileName(d);
                if (n.StartsWith('.') || SkipDirs.Contains(n)) continue;
                var attr = File.GetAttributes(d);
                if ((attr & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0) continue;
                list.Add(new Entry(n, d, folder));
                Walk(d, depth + 1, list);
            }
        }
        catch { }
    }

    public List<Entry> Search(string q, int max)
    {
        _lastUse = DateTime.Now;
        q = q.Trim();
        if (q.Length < 2) return new();
        var tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
        var best = new List<(Entry e, int s)>();
        foreach (var e in All)
        {
            int s = AppIndex.Score(e.Name, q, tr);
            if (s > 0) best.Add((e, s));
        }
        return best.OrderByDescending(x => x.s).ThenBy(x => x.e.Name.Length).Take(max).Select(x => x.e).ToList();
    }

    /// <summary>Uzun süre kullanılmadıysa dizini bırak (bellek).</summary>
    public void Trim()
    {
        if (!Loading && All.Count > 0 && DateTime.Now - _lastUse > TimeSpan.FromMinutes(5)) All = Array.Empty<Entry>();
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
        try { b = _g.LoadImage(File.ReadAllBytes(file), (int)(300 * _g.Scale), out _); }
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
