using System.Diagnostics;
using System.Runtime.InteropServices;
using Ada.Core;
using Microsoft.Win32;

namespace Ada.Services;

/// <summary>
/// ASUS ACPI (ATKACPI) — G-Helper'ın kullandığı arayüz: fan/performans profili, pil şarj sınırı, GPU modu,
/// ekran overdrive, fan devri ve CPU sıcaklığı. ASUS System Control Interface sürücüsü yoksa Available=false.
/// </summary>
public static unsafe class AsusAcpi
{
    public const uint CpuFan = 0x00110013, GpuFan = 0x00110014, PerfMode = 0x00120075, BatteryLimit = 0x00120057,
        CpuTemp = 0x00120094, GpuEco = 0x00090020, GpuMux = 0x00090016, Overdrive = 0x00050019;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sec, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(IntPtr h, uint code, byte* inBuf, uint inLen, byte* outBuf, uint outLen, out uint ret, IntPtr ov);

    static IntPtr _h = IntPtr.Zero;
    static bool _tried;
    static readonly object Lock = new();

    public static bool Available
    {
        get
        {
            lock (Lock)
            {
                if (!_tried)
                {
                    _tried = true;
                    _h = CreateFileW(@"\\.\ATKACPI", 0xC0000000, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
                }
                return _h != IntPtr.Zero && _h != new IntPtr(-1);
            }
        }
    }

    static int Call(uint method, uint id, int? value)
    {
        if (!Available) return -1;
        byte* inb = stackalloc byte[16];
        byte* outb = stackalloc byte[16];
        *(uint*)inb = method;
        *(uint*)(inb + 4) = 8;
        *(uint*)(inb + 8) = id;
        *(int*)(inb + 12) = value ?? 0;
        lock (Lock)
            if (!DeviceIoControl(_h, 0x0022240C, inb, 16, outb, 16, out _, IntPtr.Zero)) return -1;
        return *(int*)outb;
    }

    /// <summary>DSTS: değer (varlık biti 0x10000 düşülmüş). Desteklenmiyorsa negatif.</summary>
    public static int Get(uint id) => Call(0x53545344, id, null) - 65536;

    /// <summary>DEVS: ayarla. 1 = başarılı.</summary>
    public static int Set(uint id, int value)
    {
        int r = Call(0x53564544, id, value);
        Log.Info($"ASUS ACPI set 0x{id:X8}={value} → {r}");
        return r;
    }

    /// <summary>Fan devri (rpm) ya da -1.</summary>
    public static int FanRpm(uint id)
    {
        int v = Get(id);
        return v is < 0 or > 200 ? -1 : v * 100;
    }

    public static int CpuTempC()
    {
        int v = Get(CpuTemp);
        return v is <= 0 or > 120 ? -1 : v;
    }
}

/// <summary>Windows güç planları ve (Dengeli plandayken) güç modu kaplaması.</summary>
public static unsafe class PowerPlans
{
    [DllImport("powrprof.dll")] static extern uint PowerEnumerate(IntPtr root, IntPtr sub, IntPtr sub2, uint access, uint index, byte* buf, ref uint size);
    [DllImport("powrprof.dll")] static extern uint PowerReadFriendlyName(IntPtr root, Guid* scheme, IntPtr sub, IntPtr setting, byte* buf, ref uint size);
    [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
    [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, Guid* scheme);
    [DllImport("powrprof.dll")] static extern uint PowerGetEffectiveOverlayScheme(out Guid mode);
    [DllImport("powrprof.dll")] static extern uint PowerSetActiveOverlayScheme(Guid mode);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);

    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid[] Overlays =
    {
        new("961cc777-2547-4f9d-8174-7d86181b8a7a"), // en iyi güç verimliliği
        Guid.Empty,                                   // dengeli
        new("ded574b5-45a0-4f42-8737-46345c09c238"),  // en iyi performans
    };

    public sealed record Plan(Guid Id, string Name);

    public static List<Plan> List()
    {
        var list = new List<Plan>();
        for (uint i = 0; i < 32; i++)
        {
            Guid g; uint size = (uint)sizeof(Guid);
            if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, i, (byte*)&g, ref size) != 0) break;
            uint n = 0;
            PowerReadFriendlyName(IntPtr.Zero, &g, IntPtr.Zero, IntPtr.Zero, null, ref n);
            var buf = stackalloc byte[(int)Math.Max(n, 2)];
            PowerReadFriendlyName(IntPtr.Zero, &g, IntPtr.Zero, IntPtr.Zero, buf, ref n);
            list.Add(new Plan(g, new string((char*)buf)));
        }
        return list;
    }

    public static Guid Active()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var p) != 0) return Guid.Empty;
        var g = *(Guid*)p;
        LocalFree(p);
        return g;
    }

    public static void SetActive(Guid g) => PowerSetActiveScheme(IntPtr.Zero, &g);

    /// <summary>0 verimlilik, 1 dengeli, 2 performans; -1 bilinmiyor.</summary>
    public static int Overlay()
    {
        if (PowerGetEffectiveOverlayScheme(out var g) != 0) return -1;
        int i = Array.IndexOf(Overlays, g);
        return i >= 0 ? i : g == new Guid("3af9b8d9-7c97-431d-ad78-34a8bfea439f") ? 2 : 1;
    }

    public static void SetOverlay(int i) => PowerSetActiveOverlayScheme(Overlays[Math.Clamp(i, 0, 2)]);
}

/// <summary>NVIDIA ekran kartı bilgisi (nvidia-smi, sadece panel açıkken ve arka planda).</summary>
public sealed class NvidiaInfo
{
    public string Name = "", Status = "";
    public int Temp = -1, Util = -1, MemUsed = -1, MemTotal = -1, Clock = -1;
    public double Power = -1;
    public bool Available { get; private set; } = File.Exists(Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"));
    bool _busy;

    public void Refresh()
    {
        if (!Available || _busy) return;
        _busy = true;
        Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
                    "--query-gpu=name,temperature.gpu,utilization.gpu,power.draw,clocks.gr,memory.used,memory.total,pstate --format=csv,noheader,nounits")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
                using var p = Process.Start(psi)!;
                var line = p.StandardOutput.ReadLine() ?? "";
                p.WaitForExit(3000);
                var f = line.Split(',').Select(s => s.Trim()).ToArray();
                if (f.Length < 8) { Dispatcher.Post(() => Status = "Kart uykuda / okunamadı"); return; }
                static int I(string s) => int.TryParse(s, out var v) ? v : -1;
                double.TryParse(f[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pw);
                Dispatcher.Post(() =>
                {
                    Name = f[0].Replace("NVIDIA ", "").Replace("GeForce ", "");
                    Temp = I(f[1]); Util = I(f[2]); Power = pw; Clock = I(f[4]); MemUsed = I(f[5]); MemTotal = I(f[6]);
                    Status = f[7];
                });
            }
            catch (Exception e) { Log.Error("nvidia-smi", e); Dispatcher.Post(() => Available = false); }
            finally { _busy = false; }
        });
    }
}

/// <summary>Ekran yenileme hızı (birincil ekran).</summary>
public static unsafe class DisplayModes
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettingsW(string? dev, int mode, ref DEVMODE dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsExW(string? dev, ref DEVMODE dm, IntPtr hwnd, uint flags, IntPtr param);

    static DEVMODE New() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>(), dmDeviceName = "", dmFormName = "" };

    public static int Current()
    {
        var dm = New();
        return EnumDisplaySettingsW(null, -1, ref dm) ? dm.dmDisplayFrequency : -1; // ENUM_CURRENT_SETTINGS
    }

    /// <summary>Mevcut çözünürlükte desteklenen yenileme hızları.</summary>
    public static List<int> Rates()
    {
        var cur = New();
        if (!EnumDisplaySettingsW(null, -1, ref cur)) return new();
        var set = new SortedSet<int>();
        for (int i = 0; ; i++)
        {
            var dm = New();
            if (!EnumDisplaySettingsW(null, i, ref dm)) break;
            if (dm.dmPelsWidth == cur.dmPelsWidth && dm.dmPelsHeight == cur.dmPelsHeight && dm.dmBitsPerPel == cur.dmBitsPerPel && dm.dmDisplayFrequency > 1)
                set.Add(dm.dmDisplayFrequency);
        }
        return set.ToList();
    }

    /// <summary>0 = başarılı (DISP_CHANGE_SUCCESSFUL).</summary>
    public static int Set(int hz)
    {
        var dm = New();
        if (!EnumDisplaySettingsW(null, -1, ref dm)) return -1;
        dm.dmDisplayFrequency = hz;
        dm.dmFields = 0x400000; // DM_DISPLAYFREQUENCY
        int r = ChangeDisplaySettingsExW(null, ref dm, IntPtr.Zero, 0x1, IntPtr.Zero); // CDS_UPDATEREGISTRY
        Log.Info($"Yenileme hızı {hz} Hz → {r}");
        return r;
    }
}

/// <summary>Windows Grafik ayarları: uygulama başına GPU tercihi (0 Windows karar versin, 1 tasarruf, 2 yüksek performans).</summary>
public static class GpuPreference
{
    const string Key = @"Software\Microsoft\DirectX\UserGpuPreferences";

    public static int Get(string exe)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        var v = k?.GetValue(exe) as string;
        if (v == null) return 0;
        var i = v.IndexOf("GpuPreference=", StringComparison.Ordinal);
        return i >= 0 && int.TryParse(v.AsSpan(i + 14, 1), out var n) ? n : 0;
    }

    public static void Set(string exe, int pref)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key);
        if (pref == 0) k.DeleteValue(exe, false);
        else k.SetValue(exe, $"GpuPreference={pref};");
    }
}
