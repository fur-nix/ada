using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Ada.Core;
using Ada.Native;

namespace Ada.Services;

/// <summary>Sistem istatistikleri. Yalnızca Sistem sekmesi açıkken saniyede bir örneklenir.</summary>
public sealed class StatsService : IDisposable
{
    public const int History = 60;
    public float Cpu, Ram, Gpu = -1;
    public ulong RamUsed, RamTotal;
    public double DiskUsed, DiskTotal;
    public double NetDown, NetUp; // bayt/sn
    public int Battery = -1; public bool Charging;
    public readonly float[] CpuHist = new float[History];
    public readonly float[] NetHist = new float[History];

    long _idle, _kernel, _user;
    long _rx, _tx; DateTime _netStamp;
    IntPtr _pdhQuery, _pdhGpu;
    bool _gpuFailed;

    public void Sample()
    {
        SampleCpu(); SampleRam(); SampleDisk(); SampleNet(); SampleBattery(); SampleGpu();
        Shift(CpuHist, Cpu);
        Shift(NetHist, (float)(NetDown + NetUp));
    }

    static void Shift(float[] a, float v) { Array.Copy(a, 1, a, 0, a.Length - 1); a[^1] = v; }

    void SampleCpu()
    {
        if (!Win32.GetSystemTimes(out var idle, out var kernel, out var user)) return;
        if (_kernel != 0)
        {
            long di = idle - _idle, dk = kernel - _kernel, du = user - _user;
            long total = dk + du;
            if (total > 0) Cpu = Math.Clamp((float)(total - di) / total, 0, 1);
        }
        _idle = idle; _kernel = kernel; _user = user;
    }

    void SampleRam()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!Win32.GlobalMemoryStatusEx(ref m)) return;
        RamTotal = m.ullTotalPhys; RamUsed = m.ullTotalPhys - m.ullAvailPhys;
        Ram = (float)RamUsed / RamTotal;
    }

    void SampleDisk()
    {
        try
        {
            var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            DiskTotal = d.TotalSize; DiskUsed = d.TotalSize - d.AvailableFreeSpace;
        }
        catch { }
    }

    void SampleNet()
    {
        long rx = 0, tx = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var s = ni.GetIPStatistics();
                rx += s.BytesReceived; tx += s.BytesSent;
            }
        }
        catch { return; }
        var now = DateTime.UtcNow;
        if (_netStamp != default)
        {
            var sec = (now - _netStamp).TotalSeconds;
            if (sec > 0.2)
            {
                NetDown = Math.Max(0, (rx - _rx) / sec);
                NetUp = Math.Max(0, (tx - _tx) / sec);
            }
        }
        _rx = rx; _tx = tx; _netStamp = now;
    }

    /// <summary>Kapalı hapta CPU·RAM yuvası için hafif örnekleme.</summary>
    public void SampleLight() { SampleCpu(); SampleRam(); }

    // ---------- RAM'e göre en çok kullanan uygulamalar (arka planda) ----------
    public sealed record TopApp(string Name, long Bytes, string? Path);
    public IReadOnlyList<TopApp> TopApps { get; private set; } = Array.Empty<TopApp>();
    bool _topBusy;

    public void RefreshTopApps()
    {
        if (_topBusy) return;
        _topBusy = true;
        Task.Run(() =>
        {
            try
            {
                var groups = new Dictionary<string, (long bytes, string? path)>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id <= 4 || p.ProcessName is "Idle" or "System" or "Memory Compression" or "Registry" or "svchost" or "dwm" or "csrss") continue;
                        var ws = p.WorkingSet64;
                        groups.TryGetValue(p.ProcessName, out var g);
                        groups[p.ProcessName] = (g.bytes + ws, g.path ?? ImagePath(p.Id));
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
                var top = groups.OrderByDescending(kv => kv.Value.bytes).Take(4)
                    .Select(kv => new TopApp(Pretty(kv.Key, kv.Value.path), kv.Value.bytes, kv.Value.path)).ToList();
                Dispatcher.Post(() => TopApps = top);
            }
            catch (Exception e) { Log.Error("TopApps", e); }
            finally { _topBusy = false; }
        });
    }

    static string Pretty(string name, string? path)
    {
        try
        {
            if (path != null)
            {
                var d = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(d) && d.Length <= 28) return d.Trim();
            }
        }
        catch { }
        return name;
    }

    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern unsafe bool QueryFullProcessImageNameW(IntPtr h, int flags, char* buf, ref int size);

    static unsafe string? ImagePath(int pid)
    {
        var h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return null;
        try
        {
            char* buf = stackalloc char[520]; int n = 520;
            return QueryFullProcessImageNameW(h, 0, buf, ref n) ? new string(buf, 0, n) : null;
        }
        finally { CloseHandle(h); }
    }

    // ---------- internet hız testi (kullanıcı başlatır) ----------
    public double SpeedDown = -1, SpeedUp = -1;
    public bool SpeedTesting;
    public string SpeedStatus = "";

    public void RunSpeedTest()
    {
        if (SpeedTesting) return;
        SpeedTesting = true; SpeedDown = SpeedUp = -1; SpeedStatus = "İndirme test ediliyor…";
        Task.Run(async () =>
        {
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using (var resp = await http.GetAsync("https://speed.cloudflare.com/__down?bytes=25000000", System.Net.Http.HttpCompletionOption.ResponseHeadersRead))
                await using (var s = await resp.Content.ReadAsStreamAsync())
                {
                    var buf = new byte[64 * 1024]; long total = 0; int n;
                    while ((n = await s.ReadAsync(buf)) > 0)
                    {
                        total += n;
                        double mbps = total * 8 / 1e6 / Math.Max(0.05, sw.Elapsed.TotalSeconds);
                        Dispatcher.Post(() => SpeedDown = mbps);
                    }
                }
                Dispatcher.Post(() => SpeedStatus = "Yükleme test ediliyor…");
                var payload = new byte[8_000_000];
                sw.Restart();
                using (await http.PostAsync("https://speed.cloudflare.com/__up", new System.Net.Http.ByteArrayContent(payload))) { }
                double up = payload.Length * 8 / 1e6 / sw.Elapsed.TotalSeconds;
                Dispatcher.Post(() => { SpeedUp = up; SpeedStatus = "Tamamlandı"; });
            }
            catch (Exception e) { Log.Error("SpeedTest", e); Dispatcher.Post(() => SpeedStatus = "Bağlantı hatası"); }
            finally { Dispatcher.Post(() => SpeedTesting = false); }
        });
    }

    long _battStamp;
    /// <summary>Kapalı haptaki pil göstergesi için (en fazla 5 sn'de bir).</summary>
    public void SampleBatteryOnly()
    {
        long now = Environment.TickCount64;
        if (now - _battStamp < 5000 && _battStamp != 0) return;
        _battStamp = now;
        SampleBattery();
    }

    void SampleBattery()
    {
        if (!Win32.GetSystemPowerStatus(out var s)) return;
        if ((s.BatteryFlag & 128) != 0 || s.BatteryLifePercent == 255) { Battery = -1; return; }
        Battery = s.BatteryLifePercent;
        Charging = s.ACLineStatus == 1;
    }

    unsafe void SampleGpu()
    {
        if (_gpuFailed) return;
        try
        {
            if (_pdhQuery == IntPtr.Zero)
            {
                if (Win32.PdhOpenQueryW(null, IntPtr.Zero, out _pdhQuery) != 0 ||
                    Win32.PdhAddEnglishCounterW(_pdhQuery, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _pdhGpu) != 0)
                { _gpuFailed = true; return; }
                Win32.PdhCollectQueryData(_pdhQuery);
                return;
            }
            if (Win32.PdhCollectQueryData(_pdhQuery) != 0) return;
            const uint PDH_FMT_DOUBLE = 0x200, PDH_MORE_DATA = 0x800007D2;
            uint size = 0;
            int r = Win32.PdhGetFormattedCounterArrayW(_pdhGpu, PDH_FMT_DOUBLE, ref size, out uint count, IntPtr.Zero);
            if ((uint)r != PDH_MORE_DATA || size == 0) { Gpu = 0; return; }
            var buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (Win32.PdhGetFormattedCounterArrayW(_pdhGpu, PDH_FMT_DOUBLE, ref size, out count, buf) != 0) return;
                double sum = 0;
                // PDH_FMT_COUNTERVALUE_ITEM_W: { wchar* name; { uint status; double value; } } = 24 bayt (x64)
                for (int i = 0; i < count; i++)
                {
                    byte* item = (byte*)buf + i * 24;
                    uint status = *(uint*)(item + 8);
                    if (status == 0) sum += *(double*)(item + 16);
                }
                Gpu = (float)Math.Clamp(sum / 100.0, 0, 1);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch (Exception e) { Log.Error("GPU", e); _gpuFailed = true; Gpu = -1; }
    }

    /// <summary>Sekme kapanınca PDH sorgusunu bırak (bellek).</summary>
    public void Pause()
    {
        if (_pdhQuery != IntPtr.Zero) { Win32.PdhCloseQuery(_pdhQuery); _pdhQuery = IntPtr.Zero; }
    }

    public static string Bytes(double b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
        return i == 0 ? $"{b:0} {u[i]}" : b < 10 ? $"{b:0.0} {u[i]}" : $"{b:0} {u[i]}";
    }

    public static string Uptime()
    {
        var t = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return t.TotalDays >= 1 ? $"{(int)t.TotalDays} g {t.Hours} sa" : $"{t.Hours} sa {t.Minutes} dk";
    }

    public void Dispose() => Pause();
}
