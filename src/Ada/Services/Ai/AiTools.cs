using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Ada.Core;
using Ada.Data;

namespace Ada.Services.Ai;

/// <summary>Bir araç tanımı: adı, açıklaması, JSON şeması, çalıştırıcı. Destructive ise önce onay istenir.</summary>
public sealed class ToolDef
{
    public required string Name;
    public required string Description;
    public required JsonNode Schema;
    public bool Destructive;
    /// <summary>Çağrı başına ek onay koşulu (ör. çalışma klasörü dışı, Win kısayolu). true → onay iste.</summary>
    public Func<JsonObject, bool>? Gate;
    /// <summary>Onay kutusunda gösterilecek kısa özet (yıkıcı araçlarda).</summary>
    public Func<JsonObject, string>? Preview;
    public required Func<JsonObject, CancellationToken, Task<string>> Run;
}

/// <summary>Asistanın bilgisayarda iş görmesini sağlayan araçlar; hepsi çalışma klasörüne sınırlı.</summary>
public sealed class AiTools
{
    readonly Func<string> _workDir;
    public AiTools(Func<string> workDir) => _workDir = workDir;

    public static JsonObject Obj(params (string name, string type, string desc, bool required)[] props)
    {
        var p = new JsonObject();
        var req = new JsonArray();
        foreach (var (n, t, d, r) in props)
        {
            p[n] = new JsonObject { ["type"] = t, ["description"] = d };
            if (r) req.Add(n);
        }
        return new JsonObject { ["type"] = "object", ["properties"] = p, ["required"] = req };
    }

    static string S(JsonObject a, string k) => a[k] switch { null => "", var n => n.GetValueKind() == System.Text.Json.JsonValueKind.String ? n.GetValue<string>() : n.ToString() };
    static int Int(JsonObject a, string k)
    {
        var n = a[k];
        if (n == null) return 0;
        try { return n.GetValueKind() == System.Text.Json.JsonValueKind.Number ? n.GetValue<int>() : int.TryParse(n.ToString(), out var v) ? v : 0; }
        catch { return 0; }
    }

    public static readonly HashSet<string> ComputerTools = new() { "screenshot", "click", "type_text", "press_keys", "scroll", "wait" };

    /// <summary>Yol çalışma klasörünün içinde mi? Dışarıdaki yollar onay ister.</summary>
    public string Resolve(string path) => Resolve(path, out _);

    public bool Outside(string path) { Resolve(path, out var o); return o; }

    public string Resolve(string path, out bool outside)
    {
        var root = _workDir().TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(root, path));
        // ayraç ekleyerek önek hilesini engelle: C:\Work, C:\Workgizli'yi kapsamamalı
        outside = !(full.Equals(root, StringComparison.OrdinalIgnoreCase)
                    || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        return full;
    }

    public List<ToolDef> Build()
    {
        var tools = new List<ToolDef>
        {
            new()
            {
                Name = "list_dir", Description = "Bir klasördeki dosya ve alt klasörleri listeler (boyutlarıyla).",
                Schema = Obj(("path", "string", "Klasör yolu (boş = çalışma klasörü)", false)),
                Run = (a, _) =>
                {
                    var dir = Resolve(S(a, "path").Length > 0 ? S(a, "path") : ".");
                    if (!Directory.Exists(dir)) return Task.FromResult("Klasör yok: " + dir);
                    var sb = new StringBuilder(dir + "\n");
                    foreach (var d in Directory.EnumerateDirectories(dir).Take(200)) sb.Append("[KLASÖR] ").AppendLine(Path.GetFileName(d));
                    foreach (var f in Directory.EnumerateFiles(dir).Take(500))
                    { var fi = new FileInfo(f); sb.Append(StatsService.Bytes(fi.Length).PadLeft(9)).Append("  ").AppendLine(Path.GetFileName(f)); }
                    return Task.FromResult(sb.ToString());
                },
            },
            new()
            {
                Name = "read_file", Description = "Bir metin dosyasının içeriğini okur (ilk 100 KB).",
                Schema = Obj(("path", "string", "Dosya yolu", true)),
                Gate = a => Outside(S(a, "path")), // çalışma klasörü dışını okumak onay ister
                Preview = a => "Dış konumdan oku: " + Resolve(S(a, "path")),
                Run = (a, _) =>
                {
                    var f = Resolve(S(a, "path"));
                    if (!File.Exists(f)) return Task.FromResult("Dosya yok: " + f);
                    var bytes = File.ReadAllBytes(f);
                    if (bytes.Length > 100_000) bytes = bytes[..100_000];
                    return Task.FromResult(Encoding.UTF8.GetString(bytes));
                },
            },
            new()
            {
                Name = "write_file", Description = "Bir dosyaya içerik yazar (varsa üzerine yazar). Kod yazdırmak için kullan.",
                Schema = Obj(("path", "string", "Dosya yolu", true), ("content", "string", "Yazılacak içerik", true)),
                Destructive = true,
                Preview = a => "Dosya yaz: " + Resolve(S(a, "path")) + $"  ({S(a, "content").Length} karakter)",
                Run = (a, _) =>
                {
                    var f = Resolve(S(a, "path"));
                    Directory.CreateDirectory(Path.GetDirectoryName(f)!);
                    File.WriteAllText(f, S(a, "content"));
                    return Task.FromResult("Yazıldı: " + f);
                },
            },
            new()
            {
                Name = "delete_path", Description = "Bir dosyayı ya da klasörü geri dönüşüm kutusuna taşır (kalıcı silmez).",
                Schema = Obj(("path", "string", "Silinecek yol", true)),
                Destructive = true,
                Preview = a => "Geri dönüşüm kutusuna taşı: " + Resolve(S(a, "path")),
                Run = (a, _) =>
                {
                    var p = Resolve(S(a, "path"));
                    if (!File.Exists(p) && !Directory.Exists(p)) return Task.FromResult("Yol yok: " + p);
                    Recycle.Delete(p);
                    return Task.FromResult("Geri dönüşüm kutusuna taşındı: " + p);
                },
            },
            new()
            {
                Name = "scan_junk", Description = "Geçici dosya ve önbellek klasörlerini tarar; ne kadar yer boşaltılabileceğini raporlar (hiçbir şey silmez).",
                Schema = Obj(),
                Run = (_, ct) => Task.Run(() => JunkCleaner.Scan(ct), ct),
            },
            new()
            {
                Name = "clean_junk", Description = "Geçici dosyaları ve tarayıcı önbelleklerini geri dönüşüm kutusuna taşıyarak yer açar.",
                Schema = Obj(),
                Destructive = true,
                Preview = _ => "Geçici dosyalar ve önbellekler temizlenecek (geri dönüşüm kutusuna)",
                Run = (_, ct) => Task.Run(() => JunkCleaner.Clean(ct), ct),
            },
            new()
            {
                Name = "security_scan", Description = "Hızlı güvenlik taraması: başlangıçta çalışan programlar, dinlenen ağ portları, çalışan şüpheli süreçler ve Defender durumu.",
                Schema = Obj(),
                Run = (_, ct) => SecurityScan.Run(ct),
            },
            new()
            {
                Name = "run_powershell", Description = "Bir PowerShell komutu çalıştırır ve çıktısını döndürür. Sistem işleri, kod derleme/çalıştırma için.",
                Schema = Obj(("command", "string", "PowerShell komutu", true)),
                Destructive = true,
                Preview = a => "PowerShell: " + S(a, "command"),
                Run = async (a, ct) =>
                {
                    var psi = new ProcessStartInfo("powershell.exe",
                        "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " + Quote(S(a, "command")))
                    { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = _workDir() };
                    using var p = Process.Start(psi)!;
                    var outp = await p.StandardOutput.ReadToEndAsync(ct);
                    var err = await p.StandardError.ReadToEndAsync(ct);
                    await p.WaitForExitAsync(ct);
                    var r = (outp + err).Trim();
                    return r.Length > 8000 ? r[..8000] + "\n…(kısaltıldı)" : r.Length == 0 ? $"(çıktı yok, çıkış kodu {p.ExitCode})" : r;
                },
            },
            new()
            {
                Name = "open_path", Description = "Bir dosyayı, klasörü, uygulamayı ya da bağlantıyı Windows'ta açar.",
                Schema = Obj(("target", "string", "Yol, uygulama ya da https:// adresi", true)),
                // http(s) ve ms-settings bağlantıları doğrudan; dosya/program/şema açmak onay ister (keyfi exe/komut çalıştırmayı engeller)
                Gate = a => !IsSafeOpen(S(a, "target")),
                Preview = a => "Aç: " + S(a, "target"),
                Run = (a, _) => { Native.Win32.ShellExecuteW(IntPtr.Zero, "open", S(a, "target"), null, null, 1); return Task.FromResult("Açıldı: " + S(a, "target")); },
            },
        };

        // ---- Bilgisayar kullanımı (ekran + fare + klavye) ----
        tools.Add(new ToolDef
        {
            Name = "screenshot", Description = "Ekranın şu anki görüntüsünü alır ve sana gönderir (bilgisayarda bir şey yapmadan önce ekrana bakmak için).",
            Schema = Obj(),
            Run = (_, _) => Task.FromResult("[ekran görüntüsü ekte]"),
        });
        tools.Add(new ToolDef
        {
            Name = "click", Description = "Ekranda bir noktaya tıklar. Koordinatlar sana gönderilen görüntünün pikselleri (sol üst 0,0).",
            Schema = Obj(("x", "integer", "Yatay piksel", true), ("y", "integer", "Dikey piksel", true), ("button", "string", "left/right/double", false)),
            Run = (a, _) =>
            {
                var (sx, sy) = ComputerControl.ToScreen(Int(a, "x"), Int(a, "y"), ComputerControl.LastImgW, ComputerControl.LastImgH);
                var btn = S(a, "button");
                ComputerControl.Click(sx, sy, btn == "right" ? "right" : "left", btn == "double" ? 2 : 1);
                return Task.FromResult($"{btn} tık: {Int(a, "x")},{Int(a, "y")}");
            },
        });
        tools.Add(new ToolDef
        {
            Name = "type_text", Description = "Klavyeden metin yazar (önce yazılacak alana tıkla). \\n Enter demektir.",
            Schema = Obj(("text", "string", "Yazılacak metin", true)),
            Run = (a, _) => { ComputerControl.TypeText(S(a, "text")); return Task.FromResult("yazıldı"); },
        });
        tools.Add(new ToolDef
        {
            Name = "press_keys", Description = "Kısayol tuşu basar: enter, tab, esc, ctrl+c, alt+tab, ctrl+a gibi.",
            Schema = Obj(("keys", "string", "Tuş kombinasyonu", true)),
            // Win tuşlu kombinasyonlar (Win+R çalıştır kutusu vb.) PowerShell onayını atlayabilir → onay iste
            Gate = a => { var k = S(a, "keys").ToLowerInvariant(); return k.Contains("win") || k.Contains("meta") || k.Contains("super"); },
            Preview = a => "Kısayol tuşu: " + S(a, "keys"),
            Run = (a, _) => { ComputerControl.Hotkey(S(a, "keys")); return Task.FromResult("basıldı: " + S(a, "keys")); },
        });
        tools.Add(new ToolDef
        {
            Name = "scroll", Description = "Fare tekerleğiyle kaydırır. Pozitif yukarı, negatif aşağı.",
            Schema = Obj(("amount", "integer", "Tık sayısı (ör. -3)", true)),
            Run = (a, _) => { ComputerControl.Scroll(Int(a, "amount")); return Task.FromResult("kaydırıldı"); },
        });
        tools.Add(new ToolDef
        {
            Name = "wait", Description = "Belirtilen milisaniye kadar bekler (sayfa/uygulama yüklenmesini beklemek için).",
            Schema = Obj(("ms", "integer", "Milisaniye (en fazla 8000)", true)),
            Run = async (a, ct) => { await Task.Delay(Math.Clamp(Int(a, "ms"), 0, 8000), ct); return "beklendi"; },
        });

        tools.Add(new ToolDef
        {
            Name = "web_search", Description = "İnternette arama yapar ve ilk sonuçların başlık + özet + bağlantısını döndürür.",
            Schema = Obj(("query", "string", "Arama sorgusu", true)),
            Run = (a, ct) => WebSearch.Run(S(a, "query"), ct),
        });
        tools.Add(new ToolDef
        {
            Name = "fetch_url", Description = "Bir web sayfasının metin içeriğini getirir.",
            Schema = Obj(("url", "string", "https:// adresi", true)),
            Run = (a, ct) => WebSearch.Fetch(S(a, "url"), ct),
        });
        return tools;
    }

    static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";

    /// <summary>Onaysız açılabilecek güvenli hedef: http(s) ya da ms-settings/ms-screenclip bağlantısı. Dosya/exe/şema değil.</summary>
    static bool IsSafeOpen(string target)
    {
        var t = target.Trim();
        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return true;
        if (t.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("ms-screenclip:", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
