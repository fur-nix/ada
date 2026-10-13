using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Ada.Core;

namespace Ada.Services.Ai;

/// <summary>Dosya/klasörü geri dönüşüm kutusuna taşıma (kalıcı silme değil).</summary>
public static class Recycle
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT { public IntPtr hwnd; public uint wFunc; public string pFrom; public string? pTo; public ushort fFlags; public int fAnyOperationsAborted; public IntPtr hNameMappings; public string? lpszProgressTitle; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);

    public static void Delete(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = 3, // FO_DELETE
            pFrom = path + "\0\0",
            fFlags = 0x0040 | 0x0010 | 0x0004 | 0x0400, // ALLOWUNDO | NOCONFIRMATION | SILENT | NOERRORUI
        };
        SHFileOperationW(ref op);
    }
}

/// <summary>Geçici dosya ve önbellek tarama/temizleme.</summary>
public static class JunkCleaner
{
    static IEnumerable<(string name, string path)> Targets()
    {
        string tmp = Path.GetTempPath();
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        yield return ("Geçici dosyalar", tmp);
        yield return ("Windows geçici", Path.Combine(win, "Temp"));
        yield return ("Önbellek (INetCache)", Path.Combine(local, @"Microsoft\Windows\INetCache"));
        yield return ("Küçük resim önbelleği", Path.Combine(local, @"Microsoft\Windows\Explorer"));
        yield return ("Chrome önbellek", Path.Combine(local, @"Google\Chrome\User Data\Default\Cache"));
        yield return ("Edge önbellek", Path.Combine(local, @"Microsoft\Edge\User Data\Default\Cache"));
        yield return ("Crash dumpları", Path.Combine(local, "CrashDumps"));
    }

    static long DirSize(string dir, CancellationToken ct)
    {
        long sum = 0;
        try { foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) { ct.ThrowIfCancellationRequested(); try { sum += new FileInfo(f).Length; } catch { } } }
        catch { }
        return sum;
    }

    public static string Scan(CancellationToken ct)
    {
        var sb = new StringBuilder("Temizlenebilir alan:\n");
        long total = 0;
        foreach (var (name, path) in Targets())
        {
            if (!Directory.Exists(path)) continue;
            long s = DirSize(path, ct);
            if (s > 0) { sb.Append(StatsService.Bytes(s).PadLeft(10)).Append("  ").AppendLine(name); total += s; }
        }
        sb.Append("\nToplam: ").Append(StatsService.Bytes(total));
        sb.Append("\n(Temizlemek için clean_junk aracını kullan — dosyalar geri dönüşüm kutusuna taşınır.)");
        return sb.ToString();
    }

    public static string Clean(CancellationToken ct)
    {
        long freed = 0; int files = 0;
        foreach (var (_, path) in Targets())
        {
            if (!Directory.Exists(path)) continue;
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                try { var len = new FileInfo(f).Length; File.Delete(f); freed += len; files++; } catch { }
            }
            foreach (var d in Directory.EnumerateDirectories(path))
            {
                ct.ThrowIfCancellationRequested();
                try { long s = DirSize(d, ct); Directory.Delete(d, true); freed += s; } catch { }
            }
        }
        return $"{files}+ öğe temizlendi, yaklaşık {StatsService.Bytes(freed)} yer açıldı.";
    }
}

/// <summary>Hızlı güvenlik taraması (salt okunur).</summary>
public static class SecurityScan
{
    public static async Task<string> Run(CancellationToken ct)
    {
        var sb = new StringBuilder();
        // Defender durumu
        var def = await Ps("try { $m=Get-MpComputerStatus; \"Gerçek zamanlı koruma: $($m.RealTimeProtectionEnabled) | Son tarama: $($m.QuickScanAge) gün önce | İmza: $($m.AntivirusSignatureAge) gün\" } catch { 'Defender durumu okunamadı' }", ct);
        sb.Append("● Windows Defender: ").AppendLine(def.Trim());

        // Başlangıç programları
        var run = await Ps("Get-CimInstance Win32_StartupCommand | Select-Object -First 25 Name, Command | ForEach-Object { \"$($_.Name) => $($_.Command)\" }", ct);
        sb.AppendLine("\n● Başlangıçta çalışanlar:");
        sb.AppendLine(run.Trim().Length > 0 ? run.Trim() : "(yok)");

        // Dinlenen portlar
        sb.AppendLine("\n● Dışarıya açık dinlenen portlar:");
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            var lines = props.GetActiveTcpListeners()
                .Where(e => !IPAddress.IsLoopback(e.Address))
                .Select(e => $"{e.Address}:{e.Port}").Distinct().Take(30);
            var s = string.Join(", ", lines);
            sb.AppendLine(s.Length > 0 ? s : "(sadece yerel)");
        }
        catch { sb.AppendLine("(okunamadı)"); }

        // En çok CPU kullanan 5 süreç
        var proc = await Ps("Get-Process | Sort-Object CPU -Descending | Select-Object -First 5 Name, @{n='CPU';e={[int]$_.CPU}} | ForEach-Object { \"$($_.Name): $($_.CPU)s\" }", ct);
        sb.AppendLine("\n● En çok işlemci kullanan süreçler:");
        sb.AppendLine(proc.Trim());
        return sb.ToString();
    }

    static async Task<string> Ps(string cmd, CancellationToken ct)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command " + "\"" + cmd.Replace("\"", "\\\"") + "\"")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var o = await p.StandardOutput.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            return o;
        }
        catch (Exception e) { Log.Error("secscan", e); return "(hata)"; }
    }
}

/// <summary>DuckDuckGo HTML üzerinden basit web araması ve sayfa getirme (anahtar gerekmez).</summary>
public static class WebSearch
{
    static readonly HttpClient Http = MakeClient();
    static HttpClient MakeClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        h.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Ada/1.4");
        return h;
    }

    public static async Task<string> Run(string query, CancellationToken ct)
    {
        try
        {
            var html = await Http.GetStringAsync("https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(query), ct);
            var sb = new StringBuilder();
            int n = 0;
            foreach (Match m in Regex.Matches(html, "<a[^>]*class=\"result__a\"[^>]*href=\"([^\"]+)\"[^>]*>(.*?)</a>"))
            {
                var url = WebUtility.HtmlDecode(m.Groups[1].Value);
                var uddg = Regex.Match(url, @"uddg=([^&]+)");
                if (uddg.Success) url = Uri.UnescapeDataString(uddg.Groups[1].Value);
                var title = WebUtility.HtmlDecode(Regex.Replace(m.Groups[2].Value, "<.*?>", "")).Trim();
                sb.Append(++n).Append(". ").AppendLine(title);
                sb.Append("   ").AppendLine(url);
                if (n >= 6) break;
            }
            return sb.Length > 0 ? sb.ToString() : "Sonuç bulunamadı.";
        }
        catch (Exception e) { Log.Error("websearch", e); return "Arama başarısız: " + e.Message; }
    }

    public static async Task<string> Fetch(string url, CancellationToken ct)
    {
        try
        {
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
            var html = await Http.GetStringAsync(url, ct);
            html = Regex.Replace(html, @"<script[\s\S]*?</script>|<style[\s\S]*?</style>|<[^>]+>", " ");
            html = WebUtility.HtmlDecode(Regex.Replace(html, @"\s+", " ")).Trim();
            return html.Length > 6000 ? html[..6000] + "…" : html;
        }
        catch (Exception e) { Log.Error("fetch", e); return "Getirilemedi: " + e.Message; }
    }
}
