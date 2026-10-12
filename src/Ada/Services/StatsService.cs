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
