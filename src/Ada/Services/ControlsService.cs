using System.Diagnostics;
using Ada.Core;
using Ada.Native;
using Microsoft.Win32;
using Windows.Devices.Radios;

namespace Ada.Services;

public sealed class ControlsService
{
    // ---------------- Ses (Core Audio) ----------------
    IAudioEndpointVolume? _vol;
    static readonly Guid Ctx = Guid.Empty;

    public float Volume = 0.5f;
    public bool Muted;

    IAudioEndpointVolume? Vol()
    {
        if (_vol != null) return _vol;
        try
        {
            var clsid = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
            var iid = typeof(IMMDeviceEnumerator).GUID;
            if (Win32.CoCreateInstance(clsid, IntPtr.Zero, 23, iid, out var pEnum) != 0) return null;
            var en = ComUtil.Wrap<IMMDeviceEnumerator>(pEnum);
            if (en == null || en.GetDefaultAudioEndpoint(0, 1, out var pDev) != 0) return null;
            var dev = ComUtil.Wrap<IMMDevice>(pDev);
            var vid = typeof(IAudioEndpointVolume).GUID;
            if (dev == null || dev.Activate(vid, 23, IntPtr.Zero, out var pVol) != 0) return null;
            _vol = ComUtil.Wrap<IAudioEndpointVolume>(pVol);
        }
        catch (Exception e) { Log.Error("Volume", e); }
        return _vol;
    }

    public void ReadVolume()
    {
        _vol = null; // varsayılan cihaz değişmiş olabilir
        var v = Vol();
        if (v == null) return;
        if (v.GetMasterVolumeLevelScalar(out var l) == 0) Volume = l;
        if (v.GetMute(out var m) == 0) Muted = m != 0;
    }

    public void SetVolume(float level)
    {
        Volume = Math.Clamp(level, 0, 1);
        var v = Vol();
        v?.SetMasterVolumeLevelScalar(Volume, Ctx);
        if (Muted && Volume > 0) { Muted = false; v?.SetMute(0, Ctx); }
    }

    public void ToggleMute()
    {
        Muted = !Muted;
        Vol()?.SetMute(Muted ? 1 : 0, Ctx);
    }

    // ---------------- Parlaklık ----------------
    public float Brightness = -1; // -1: desteklenmiyor / bilinmiyor
    bool _ddc;
    float _pendingBrightness = -1;
    bool _wmiBusy;

    public void ReadBrightness(IntPtr monitor)
    {
        if (TryDdc(monitor, null, out var b)) { _ddc = true; Brightness = b; return; }
        _ddc = false;
        // dizüstü ekran: WMI (powershell) – arka planda
        Task.Run(() =>
        {
            var o = RunPs("(Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightness -ErrorAction Stop | Select-Object -First 1).CurrentBrightness");
            if (int.TryParse(o?.Trim(), out var v)) Dispatcher.Post(() => Brightness = v / 100f);
        });
    }

    public void SetBrightness(IntPtr monitor, float value)
    {
        Brightness = Math.Clamp(value, 0, 1);
        if (_ddc) { TryDdc(monitor, Brightness, out _); return; }
        _pendingBrightness = Brightness;
        if (_wmiBusy) return;
        _wmiBusy = true;
        Task.Run(async () =>
        {
            // sürükleme boyunca biriken değerleri birleştir (powershell yavaş)
            while (_pendingBrightness >= 0)
            {
                var v = (int)MathF.Round(_pendingBrightness * 100);
                _pendingBrightness = -1;
                RunPs($"Invoke-CimMethod -Namespace root/WMI -ClassName WmiMonitorBrightnessMethods -MethodName WmiSetBrightness -Arguments @{{Timeout=0;Brightness={v}}} | Out-Null");
                await Task.Delay(50);
            }
            _wmiBusy = false;
        });
    }

    static unsafe bool TryDdc(IntPtr monitor, float? set, out float value)
    {
        value = -1;
        if (!Win32.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var n) || n == 0) return false;
        var arr = stackalloc PHYSICAL_MONITOR[(int)n];
        if (!Win32.GetPhysicalMonitorsFromHMONITOR(monitor, n, arr)) return false;
        try
        {
            if (!Win32.GetMonitorBrightness(arr[0].hPhysicalMonitor, out var min, out var cur, out var max) || max <= min) return false;
            value = (cur - min) / (float)(max - min);
            if (set is float s) Win32.SetMonitorBrightness(arr[0].hPhysicalMonitor, (uint)(min + s * (max - min)));
            return true;
        }
        finally { Win32.DestroyPhysicalMonitors(n, arr); }
    }

    static string? RunPs(string cmd)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{cmd}\"")
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return o;
        }
        catch (Exception e) { Log.Error("ps", e); return null; }
    }

    // ---------------- Radyolar ----------------
    Radio? _wifi, _bt;
    public bool? Wifi, Bluetooth;

    public async void ReadRadios()
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) return;
            var radios = await Radio.GetRadiosAsync();
            Radio? wifi = null, bt = null;
            foreach (var r in radios)
            {
                if (r.Kind == RadioKind.WiFi) wifi ??= r;
                else if (r.Kind == RadioKind.Bluetooth) bt ??= r;
            }
            Dispatcher.Post(() =>
            {
                _wifi = wifi; _bt = bt;
                Wifi = wifi == null ? null : wifi.State == RadioState.On;
                Bluetooth = bt == null ? null : bt.State == RadioState.On;
            });
        }
        catch (Exception e) { Log.Error("Radios", e); }
    }

    public async void ToggleRadio(bool wifi)
    {
        var r = wifi ? _wifi : _bt;
        if (r == null) { OpenSettings(wifi ? "ms-settings:network-wifi" : "ms-settings:bluetooth"); return; }
        bool on = r.State == RadioState.On;
        if (wifi) Wifi = !on; else Bluetooth = !on;
        try
        {
            var res = await r.SetStateAsync(on ? RadioState.Off : RadioState.On);
            if (res != RadioAccessStatus.Allowed) Dispatcher.Post(ReadRadios);
        }
        catch (Exception e) { Log.Error("Radio.Toggle", e); Dispatcher.Post(ReadRadios); }
    }

    // ---------------- Tema ----------------
    const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    public bool DarkMode;

    public void ReadTheme()
    {
        using var k = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        DarkMode = (k?.GetValue("AppsUseLightTheme") as int? ?? 1) == 0;
    }

    public void ToggleDark()
    {
        DarkMode = !DarkMode;
        int light = DarkMode ? 0 : 1;
        Task.Run(() =>
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(PersonalizeKey);
                k.SetValue("AppsUseLightTheme", light, RegistryValueKind.DWord);
                k.SetValue("SystemUsesLightTheme", light, RegistryValueKind.DWord);
                Win32.SendMessageTimeoutW(new IntPtr(0xFFFF), Win32.WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet", 2, 500, out _);
            }
            catch (Exception e) { Log.Error("Theme", e); }
        });
    }

    // ---------------- Güç ----------------
    public static void Lock() => Win32.LockWorkStation();
    public static void Sleep() => Task.Run(() => Win32.SetSuspendState(false, false, false));
    public static void Restart() => Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false });
    public static void Shutdown() => Process.Start(new ProcessStartInfo("shutdown", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false });
    public static void OpenSettings(string uri = "ms-settings:") => Win32.ShellExecuteW(IntPtr.Zero, "open", uri, null, null, Win32.SW_SHOWNORMAL);
}
