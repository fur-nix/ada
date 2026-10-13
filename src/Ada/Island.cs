using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Ada.Core;
using Ada.Data;
using Ada.Native;
using Ada.Panels;
using Ada.Services;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace Ada;

public enum Mode { Collapsed, Hover, Live, Expanded }

public sealed unsafe class Island
{
    public static Island Instance = null!;

    // ---- geometri (DIP) ----
    public const float Top = 6, Margin = 28;
    public const float CW = 200, CH = 34, PlayW = 262;
    public const float LW = 380, LH = 60;
    public const float EW = 780, EH = 352, ER = 34;

    public IntPtr Hwnd;
    public IntPtr Monitor;
    public readonly Gfx G;
    public readonly Ui U;
    public readonly MediaService Media = new();
    public readonly AppIndex AppIndex = new();
    public readonly FileIndex Files = new();
    public readonly StatsService Stats = new();
    public readonly ControlsService Controls = new();
    public readonly AppsService Apps;
    public ClipboardService Clip = null!;
    public readonly Panel[] Panels;

    public Mode Mode { get; private set; } = Mode.Collapsed;
    public int Tab { get; private set; }
    public Color4 MediaColor = Theme.Accent;
    public ID2D1Bitmap? MediaArt;

    Spring _w = new(CW), _h = new(CH), _r = new(CH / 2);
    Spring _hideY = new(0, 260, 30);
    Smooth _collA = new(1, 16), _liveA = new(0, 12);
    Smooth _notch = new(0, 9); // 0 = yüzen ada, 1 = üst kenara yapışık çentik
    float TopGap => Top * (1 - _notch.Value);

    public bool NotchStyle
    {
        get => Store.Current.Notch;
        set { Store.Current.Notch = value; Store.Save(); _dirty = true; _animating = true; }
    }
    float _tabT = 1; int _prevTab = -1; int _tabDir = 1;
    Spring _indX = new(0, 460, 36), _indW = new(80, 460, 36);
    bool _indInit;

    // canlı bildirim
    record LiveInfo(string Title, string Sub, string Glyph, Color4 Color, bool IsMedia, int TabOnClick, float Seconds);
    LiveInfo? _live, _pendingLive;

    // ekran
    float _scale = 1;
    RECT _mon;
    int _winX, _winY, _winW, _winH;
    bool _hidden, _shown;
    bool _autoHidden; int _awayTicks;
    /// <summary>Tam ekran yüzünden ya da "fare gelince göster" modunda çekilmiş.</summary>
    bool Concealed => _hidden || _autoHidden;

    // döngü
    readonly Stopwatch _sw = Stopwatch.StartNew();
    double _last;
    bool _animating, _wasAnimating, _dirty = true;
    bool _tracking;
    bool _collapsePending;
    IntPtr _prevForeground;
    bool _keyboardMode;
    int _minute = -1;
    int _idleTicks;
    bool _trimmed;
    int _mediaArtVersion = -1;
    bool _startupDone;

    const int T_TICK = 1, T_COLLAPSE = 2, T_EQ = 3, T_BLINK = 4, T_LIVE = 5, T_HOVER = 6, T_PROX = 8;
    const int HOTKEY_ID = 7, SEARCH_HOTKEY_ID = 9, RADIAL_HOTKEY_ID = 10, AI_HOTKEY_ID = 11;
    const uint WM_QUIT = 0x12, WM_SETCURSOR = 0x20;
    /// <summary>Başka bir Ada süreci (Başlat menüsü → "Ada Ayarları") bu mesajla ayarları açtırır.</summary>
    public const uint WM_OPEN_SETTINGS = Win32.WM_APP + 2;

    public SettingsWindow? Settings;
    public SpotlightWindow? Spotlight;
    public DockWindow? Dock;
    public RadialWindow? Radial;
    public AiWindow? AiWin;
    public readonly Ada.Services.Ai.AiService Ai = new();
    public bool IslandOff => !Store.Current.IslandEnabled;

    bool SurfacesAnimating() => Settings?.Animating == true || Spotlight?.Animating == true || Dock?.Animating == true || Radial?.Animating == true || AiWin?.Animating == true;
    void FrameSurfaces() { Settings?.FrameIfNeeded(); Spotlight?.FrameIfNeeded(); Dock?.FrameIfNeeded(); Radial?.FrameIfNeeded(); AiWin?.FrameIfNeeded(); }
    void InvalidateSurfaces() { Settings?.Invalidate(); Spotlight?.Invalidate(); Dock?.Invalidate(); Radial?.Invalidate(); AiWin?.Invalidate(); }
    ISurface? SurfaceFor(IntPtr h) =>
        Settings != null && h == Settings.Hwnd ? Settings :
        Spotlight != null && h == Spotlight.Hwnd ? Spotlight :
        Dock != null && h == Dock.Hwnd ? Dock :
        Radial != null && h == Radial.Hwnd ? Radial :
        AiWin != null && h == AiWin.Hwnd ? AiWin : null;
    public IntPtr AppIconLarge, AppIconSmall;

    public Island()
    {
        Instance = this;
        G = new Gfx();
        U = new Ui(G);
        Apps = new AppsService(G);
        Panels = new Panel[]
        {
            new ControlsPanel(this), new TodayPanel(this), new SystemPanel(this), new ClipboardPanel(this),
            new AppsPanel(this), new WallpaperPanel(this), new PowerPanel(this), new AiPanel(this)
        };
        Ai.Changed += () => { _dirty = true; Settings?.Invalidate(); };
    }

    // ================= pencere =================

    public void Create()
    {
        var cls = Marshal.StringToHGlobalUni("AdaIsland");
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&WndProcStatic,
            hInstance = Win32.GetModuleHandleW(IntPtr.Zero),
            hCursor = Win32.LoadCursorW(IntPtr.Zero, 32512),
            lpszClassName = cls
        };
        if (Environment.ProcessPath is string exe)
            Win32.ExtractIconExW(exe, 0, out AppIconLarge, out AppIconSmall, 1);
        Win32.RegisterClassExW(ref wc);
        Hwnd = Win32.CreateWindowExW(
            Win32.WS_EX_LAYERED | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_ACCEPTFILES,
            "AdaIsland", "Ada", Win32.WS_POPUP, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        Dispatcher.Init(Hwnd);
        UpdateMonitor();

        Clip = new ClipboardService(Hwnd);
        Win32.DragAcceptFiles(Hwnd, true);
        Win32.SetTimer(Hwnd, T_TICK, 1000, IntPtr.Zero);

        var st = Store.Load();
        AppsService.EnsureDefaults(st);
        Store.Save();
        Tab = Math.Clamp(st.LastTab, 0, Panels.Length - 1);
        _notch.Value = _notch.Target = st.Notch ? 1 : 0;
        ApplySettings();

        Media.Changed += OnMediaChanged;
        Media.TrackChanged += () =>
        {
            if (!_startupDone || Mode == Mode.Expanded || !Store.Current.NotifyTrack) return;
            ShowLive(new LiveInfo(Media.Title, Media.Artist, Glyph.Music, MediaColor, true, 0, 3.2f));
        };
        Media.Start();
        Dispatcher.Post(() => _startupDone = true);

        Frame();
        if (SnapMode) return;
        if (Store.Current.IslandEnabled) { Win32.ShowWindow(Hwnd, Win32.SW_SHOWNOACTIVATE); _shown = true; }
        Shell.EnsureStartMenu();
        // ASUS pil şarj sınırı yeniden başlatınca sıfırlanır → kullanıcının seçimini yeniden uygula
        if (Store.Current.ChargeLimit is 60 or 80 && AsusAcpi.Available) AsusAcpi.Set(AsusAcpi.BatteryLimit, Store.Current.ChargeLimit);
        Shell.EnsureShellMenu(Store.Current.ShellPinMenu);
    }

    // ================= ayarlar =================

    static readonly (uint mods, uint vk)[] Hotkeys =
    {
        (Win32.MOD_CONTROL | Win32.MOD_ALT, 0x20), (Win32.MOD_CONTROL | 0x4, 0x20), (Win32.MOD_ALT | 0x4, 0x20)
    };
    public static readonly string[] HotkeyNames = { "Ctrl+Alt+Space", "Ctrl+Shift+Space", "Alt+Shift+Space", "Kapalı" };
    public static readonly uint[] AccentPalette = { 0x8E9BFF, 0x7DB4FF, 0x5EEAD4, 0x4ADE80, 0xFBBF5B, 0xFF8A65, 0xF59AC9, 0xF5F5F7 };
    public bool HotkeyOk { get; private set; } = true;
    public bool SearchHotkeyOk { get; private set; } = true;
    public bool RadialHotkeyOk { get; private set; } = true;
    public bool AiHotkeyOk { get; private set; } = true;

    /// <summary>Üst ada açık/kapalı: kapalıysa hap gizlenir ama pencere yaşar (kısayollar, yapay zeka, dock çalışmaya devam).</summary>
    void UpdateIslandVisible()
    {
        if (SnapMode) return;
        bool off = !Store.Current.IslandEnabled;
        if (off)
        {
            if (Mode == Mode.Expanded) Collapse();
            Mode = Mode.Collapsed;
            if (_shown) { Win32.ShowWindow(Hwnd, Win32.SW_HIDE); _shown = false; }
            Win32.KillTimer(Hwnd, T_EQ);
        }
        else if (!_shown && !_hidden)
        {
            Win32.ShowWindow(Hwnd, Win32.SW_SHOWNOACTIVATE); _shown = true;
            _dirty = true; _animating = true;
        }
    }

    /// <summary>Ayarlar değişince çağrılır: tema, kısayol, yeniden çizim, kayıt.</summary>
    public void ApplySettings(bool save = false)
    {
        var s = Store.Current;
        s.Normalize();
        Theme.Accent = uint.TryParse(s.Accent, System.Globalization.NumberStyles.HexNumber, null, out var hex) ? Theme.Rgb(hex) : Theme.Rgb(0x8E9BFF);
        if (!SnapMode)
        {
            Win32.UnregisterHotKey(Hwnd, HOTKEY_ID);
            HotkeyOk = true;
            if (s.Hotkey < Hotkeys.Length)
                HotkeyOk = Win32.RegisterHotKey(Hwnd, HOTKEY_ID, Hotkeys[s.Hotkey].mods | Win32.MOD_NOREPEAT, Hotkeys[s.Hotkey].vk);
        }
        if (!s.VisibleTabs().Contains(Tab)) Tab = s.VisibleTabs()[0];
        if (!SnapMode)
        {
            if (s.Visibility == 0) Reveal();
            if (s.Visibility == 0 && !s.DockAutoHide && s.AppOverlapMode == 0) Win32.KillTimer(Hwnd, T_PROX);
            else Win32.SetTimer(Hwnd, T_PROX, 100, IntPtr.Zero);

            // Spotlight kısayolu
            Win32.UnregisterHotKey(Hwnd, SEARCH_HOTKEY_ID);
            SearchHotkeyOk = true;
            if (s.SearchHotkey < 2)
                SearchHotkeyOk = Win32.RegisterHotKey(Hwnd, SEARCH_HOTKEY_ID, (s.SearchHotkey == 0 ? Win32.MOD_CONTROL : Win32.MOD_ALT) | Win32.MOD_NOREPEAT, 0x20);

            // İmleç menüsü (daire) — bağımsız ana özellik
            bool radialOn = s.RadialEnabled;
            MouseHook.CanOpen = () => radialOn && Store.Current.RadialRightHold && !_hidden && Radial?.IsOpen != true && !ForegroundFullscreen();
            MouseHook.RadialOpenAndOutside = p => Radial?.IsOpen == true && !Radial.Contains(p);
            if (radialOn && s.RadialRightHold) MouseHook.Install(Hwnd); else MouseHook.Uninstall();
            Win32.UnregisterHotKey(Hwnd, RADIAL_HOTKEY_ID);
            RadialHotkeyOk = true;
            if (radialOn && s.RadialHotkey < 2)
                RadialHotkeyOk = Win32.RegisterHotKey(Hwnd, RADIAL_HOTKEY_ID, (s.RadialHotkey == 0 ? Win32.MOD_ALT : Win32.MOD_ALT | Win32.MOD_CONTROL) | Win32.MOD_NOREPEAT, 0x51);
            if (!radialOn && Radial != null) { Radial.Close(); }

            // Yapay zeka penceresi kısayolu (Ctrl+Shift+Space / Ctrl+Alt+A) — bağımsız ana özellik
            Win32.UnregisterHotKey(Hwnd, AI_HOTKEY_ID);
            AiHotkeyOk = true;
            if (s.AiEnabled && s.AiHotkey < 2)
                AiHotkeyOk = Win32.RegisterHotKey(Hwnd, AI_HOTKEY_ID,
                    (s.AiHotkey == 0 ? Win32.MOD_CONTROL | 0x4u : Win32.MOD_CONTROL | Win32.MOD_ALT) | Win32.MOD_NOREPEAT,
                    (uint)(s.AiHotkey == 0 ? 0x20 : 0x41)); // Space : A
            if (!s.AiEnabled && AiWin != null) { AiWin.Close(); AiWin = null; }

            Shell.EnsureShellMenu(s.ShellPinMenu);

            // Üst ada — bağımsız ana özellik
            UpdateIslandVisible();

            // Dock
            if (s.DockEnabled) { Dock ??= new DockWindow(this); Dock.ApplyLayoutSettings(); }
            else if (Dock != null) { Dock.Shutdown(); Dock = null; Taskbar.SetHidden(false); }
            if (!s.DockEnabled) Taskbar.SetHidden(false);
        }
        if (!s.HideOnFullscreen && _hidden) { _hidden = false; if (!_shown && !SnapMode) { Win32.ShowWindow(Hwnd, Win32.SW_SHOWNOACTIVATE); _shown = true; } }
        _indInit = false;
        _dirty = true; _animating = true;
        if (save) Store.Save();
    }

    public void OpenSettings() => OpenSettings(-1);

    public void OpenSettings(int page)
    {
        if (SnapMode) return;
        Collapse();
        Settings ??= new SettingsWindow(this);
        if (page >= 0) Settings.GoTo(page);
        Settings.Show();
        _animating = true;
    }

    public bool FullscreenHidden => _hidden;

    /// <summary>Ön plandaki pencere tam ekran mı (oyun vb.) — sağ tık kancası o sırada karışmaz.</summary>
    bool ForegroundFullscreen()
    {
        var fg = Win32.GetForegroundWindow();
        if (fg == IntPtr.Zero || SurfaceFor(fg) != null || fg == Hwnd || fg == Win32.GetShellWindow()) return false;
        var cls = Win32.ClassName(fg);
        if (cls is "WorkerW" or "Progman") return false;
        if (!Win32.GetWindowRect(fg, out var r)) return false;
        bool covers = r.Left <= _mon.Left && r.Top <= _mon.Top && r.Right >= _mon.Right && r.Bottom >= _mon.Bottom;
        return covers && (Win32.GetWindowLongW(fg, -16) & 0xC00000) != 0xC00000;
    }

    /// <summary>Dairesel menüye ekle (Gezgin sağ tık, sürükle-bırak, Dock menüsü, ayarlar).</summary>
    public void AddToRadial(string path, bool toast = true, string? name = null)
    {
        path = path.Trim().Trim('"');
        if (path.Length == 0 || !(File.Exists(path) || Directory.Exists(path) || path.StartsWith("ms-", StringComparison.OrdinalIgnoreCase))) return;
        var list = Store.Current.RadialApps;
        if (list.Any(a => a.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
        if (list.Count >= 12) { if (toast) Toast("Daire dolu", "En fazla 12 öğe", Glyph.Apps, Theme.Orange, 0, 2f); return; }
        var app = path.StartsWith("ms-", StringComparison.OrdinalIgnoreCase) ? new PinnedApp { Name = name ?? "Ayarlar", Path = path } : AppsService.FromPath(path);
        if (name != null) app.Name = name;
        list.Add(app);
        Store.Save();
        Settings?.Invalidate();
        if (toast) Toast("Daireye eklendi", app.Name, Glyph.Add, Theme.Accent, 0, 2.2f);
    }

    /// <summary>Dock'a kalıcı sabitle (sürükle-bırak, Gezgin sağ tık menüsü, --pin).</summary>
    public void PinToDock(string path)
    {
        path = path.Trim().Trim('"');
        if (path.Length == 0 || !(File.Exists(path) || Directory.Exists(path))) return;
        var apps = Store.Current.Apps;
        if (!apps.Any(a => a.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            apps.Add(AppsService.FromPath(path));
            Store.Save();
        }
        if (!Store.Current.DockEnabled) { Store.Current.DockEnabled = true; ApplySettings(save: true); }
        Dock?.Pinned(path);
        Toast("Dock'a sabitlendi", Path.GetFileNameWithoutExtension(path), Glyph.Add, Theme.Accent, 4, 2.2f);
    }

    public void ToggleRadial()
    {
        if (SnapMode) return;
        Collapse();
        Radial ??= new RadialWindow(this);
        Radial.Toggle();
    }

    public void ToggleAi()
    {
        if (SnapMode || !Store.Current.AiEnabled) return;
        if (Store.Current.IslandEnabled && Mode != Mode.Expanded && !_hidden)
        {
            // ada açıksa yapay zeka sekmesini aç
            var vis = Store.Current.VisibleTabs();
            if (vis.Contains(7)) { Expand(7); BeginKeyboard(); return; }
        }
        Collapse();
        AiWin ??= new AiWindow(this);
        AiWin.Toggle();
    }

    public void AiWindowClosed() => AiWin = null;

    public void ToggleSpotlight()
    {
        if (SnapMode) return;
        Collapse();
        Spotlight ??= new SpotlightWindow(this);
        Spotlight.Toggle();
    }

    /// <summary>Çıkışta: Dock ekran alanını bırak, görev çubuğunu geri getir.</summary>
    public void Shutdown()
    {
        MouseHook.Uninstall();
        Dock?.Shutdown(); Dock = null;
        Taskbar.SetHidden(false);
    }

    public void SettingsClosed()
    {
        Settings = null;
        _trimmed = false; _idleTicks = 0; // bellek kırpma tekrar çalışsın
    }

    // ================= --snap: ekran dışı görsel test =================
    public bool SnapMode;

    public void Snapshot(string dir)
    {
        Directory.CreateDirectory(dir);
        void Pump(double seconds)
        {
            var until = _sw.Elapsed.TotalSeconds + seconds;
            while (_sw.Elapsed.TotalSeconds < until)
            {
                while (Win32.PeekMessageW(out var m, IntPtr.Zero, 0, 0, Win32.PM_REMOVE)) { Win32.TranslateMessage(ref m); Win32.DispatchMessageW(ref m); }
                Thread.Sleep(15);
            }
        }
        void Shot(string name, int frames = 110)
        {
            for (int i = 0; i < frames; i++) Frame();
            G.SavePng(Path.Combine(dir, name + ".png"), _winW, _winH);
        }

        Clip.History.Add("https://github.com/ada-island/ada — premium dynamic island", DateTime.Now.AddMinutes(-42));
        Clip.History.Add("Toplantı notları: tasarım onaylandı, v1 cuma günü çıkıyor", DateTime.Now.AddMinutes(-7));
        Clip.History.Add("dotnet publish -c Release -p:Aot=true", DateTime.Now.AddSeconds(-20));
        Pump(2.5);
        Stats.Sample(); Pump(1.0); Stats.Sample();
        for (int i = 0; i < 40; i++) Stats.CpuHist[StatsService.History - 40 + i] = 0.15f + 0.25f * MathF.Abs(MathF.Sin(i * 0.35f)) + (i % 7) * 0.02f;

        Mode = Mode.Collapsed; Shot("1-kapali");
        ShowLive(new LiveInfo("Kopyalandı", "Toplantı notları: tasarım onaylandı, v1 cuma günü çıkıyor", Glyph.Copy, Theme.Mint, false, 3, 60));
        Shot("2-canli");
        Win32.KillTimer(Hwnd, T_LIVE); Mode = Mode.Collapsed; _live = null;
        for (int t = 0; t < Panels.Length; t++)
        {
            Expand(t);
            if (t is 0 or 2 or 4) Pump(1.5);
            Shot($"3-sekme{t}-{TabName(t)}");
        }
        // morf ortası (animasyon karesi)
        Collapse(); for (int i = 0; i < 100; i++) Frame();
        Expand(0); Shot("4-acilis-ortasi", 9);
        // çentik görünümü
        Store.Current.Notch = true;
        Collapse(); Shot("5-centik-kapali");
        Expand(2); Shot("5-centik-acik");
        Collapse();
        Store.Current.Notch = false; Shot("5-yuzen-kapali");
        Store.Current.Notch = true;
        // ayarlar penceresi sayfaları
        var sw = new SettingsWindow(this);
        Settings = sw;
        for (int p = 0; p < SettingsWindow.PageCount; p++) sw.SnapPage(p, Path.Combine(dir, $"6-ayarlar{p}.png"));
        Settings = null;

        // Dock + Spotlight
        Pump(2.0); // uygulama/dosya dizini taransın
        var dock = new DockWindow(this);
        dock.SnapRender(Path.Combine(dir, "7-dock.png"), null);
        dock.SnapRender(Path.Combine(dir, "7-dock-buyutme.png"), -60);
        dock.SnapRender(Path.Combine(dir, "7-dock-onizleme.png"), 298, 400);
        // dairesel menü (örnek öğeler: Dock'takiler)
        foreach (var a in Store.Current.Apps.Take(8)) AddToRadial(a.Path, toast: false, name: a.Name);
        var rad = new RadialWindow(this);
        rad.SnapRender(Path.Combine(dir, "9-daire.png"), 2);
        // güç alt sekmeleri
        for (int s = 0; s < 4; s++) { Expand(6); ((PowerPanel)Panels[6]).SetSub(s); Pump(s == 1 ? 3.0 : 0.6); Shot($"10-guc{s}"); }
        // Wi-Fi listesi
        Expand(0); ((ControlsPanel)Panels[0]).SnapOpenList(1); Pump(4.0); Shot("11-wifi", 140);
        Collapse();
        var sp = new SpotlightWindow(this);
        Pump(1.5);
        sp.SnapRender("vis", Path.Combine(dir, "8-spotlight-uygulama.png"));
        sp.SnapRender("12*(3+4)", Path.Combine(dir, "8-spotlight-hesap.png"));
        Store.Current.Notch = true;
    }

    void UpdateMonitor()
    {
        Monitor = Win32.MonitorFromPoint(new POINT(0, 0), 1);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        Win32.GetMonitorInfoW(Monitor, ref mi);
        _mon = mi.rcMonitor;
        Win32.GetDpiForMonitor(Monitor, 0, out var dpi, out _);
        _scale = dpi == 0 ? 1 : dpi / 96f;
        G.SetScale(_scale);
        Apps.ClearIconCache();
        _dirty = true;
    }

    public static IntPtr ProcPtr => (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&WndProcStatic;
    public float Scale => _scale;
    public RECT MonitorRect => _mon;

    [UnmanagedCallersOnly]
    static IntPtr WndProcStatic(IntPtr h, uint m, IntPtr w, IntPtr l)
    {
        try
        {
            // Çizim sürerken (COM çağrısı mesaj pompaladığında) durum değiştiren mesajları ertele → iç içe çizim/çökme yok
            if (Instance.G.InDraw && Deferrable(m))
            {
                if (m != Win32.WM_TIMER) Instance._deferred.Add((h, m, w, l));
                return m == Win32.WM_DROPFILES ? 0 : 0;
            }
            return Instance.Dispatch(h, m, w, l);
        }
        catch (Exception e) { Log.Error("WndProc " + m, e); return Win32.DefWindowProcW(h, m, w, l); }
    }

    IntPtr Dispatch(IntPtr h, uint m, IntPtr w, IntPtr l)
    {
        var s = SurfaceFor(h);
        if (s != null) return s.WndProc(h, m, w, l);
        return WndProc(h, m, w, l);
    }

    readonly List<(IntPtr h, uint m, IntPtr w, IntPtr l)> _deferred = new();

    static bool Deferrable(uint m) => m is Win32.WM_TIMER or Win32.WM_CHAR or Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN
        or Win32.WM_HOTKEY or Win32.WM_DISPLAYCHANGE or Win32.WM_DPICHANGED or Win32.WM_DROPFILES or Win32.WM_CLIPBOARDUPDATE
        or Win32.WM_SETTINGCHANGE or 0x10 /* WM_CLOSE */ || (m >= Win32.WM_APP && m < 0xC000);

    /// <summary>Çizim bitince ertelenen mesajları sırayla işle.</summary>
    void ReplayDeferred()
    {
        if (_deferred.Count == 0 || G.InDraw) return;
        var list = _deferred.ToArray();
        _deferred.Clear();
        foreach (var (h, m, w, l) in list)
        {
            try { Dispatch(h, m, w, l); } catch (Exception e) { Log.Error("deferred " + m, e); }
        }
    }

    IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l)
    {
        switch (m)
        {
            case Win32.WM_MOUSEMOVE:
                U.Inside = true;
                if (!_tracking)
                {
                    var t = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = Win32.TME_LEAVE, hwndTrack = h };
                    Win32.TrackMouseEvent(ref t);
                    _tracking = true;
                }
                _dirty = true;
                return 0;
            case Win32.WM_MOUSELEAVE:
                _tracking = false;
                if (!U.Down) U.Inside = false;
                _dirty = true;
                return 0;
            case Win32.WM_LBUTTONDOWN:
                U.Down = true; U.Pressed = true; U.Inside = true;
                Win32.SetCapture(h);
                _dirty = true;
                return 0;
            case Win32.WM_LBUTTONUP:
                U.Down = false; U.Released = true;
                Win32.ReleaseCapture();
                _dirty = true;
                return 0;
            case Win32.WM_RBUTTONUP:
                U.RightReleased = true; _dirty = true;
                return 0;
            case Win32.WM_MOUSEWHEEL:
                U.Wheel += Win32.HiWord(w) / 120f;
                _dirty = true;
                return 0;
            case WM_SETCURSOR:
                if (U.WantHand) { Win32.SetCursor(Win32.LoadCursorW(IntPtr.Zero, 32649)); return 1; }
                break;
            case Win32.WM_MOUSEACTIVATE:
                return _keyboardMode ? 1 : Win32.MA_NOACTIVATE;
            case Win32.WM_ACTIVATE:
                if (Win32.LoWord(w) == 0) // WA_INACTIVE
                {
                    _prevForeground = IntPtr.Zero;
                    EndKeyboard(false);
                    if (Mode == Mode.Expanded && !OverIsland()) Collapse();
                }
                return 0;
            case Win32.WM_KEYDOWN:
            case Win32.WM_SYSKEYDOWN:
                OnKey((int)w);
                _dirty = true;
                return 0;
            case Win32.WM_CHAR:
                if (Mode == Mode.Expanded && Panels[Tab].OnChar((char)w)) _dirty = true;
                return 0;
            case Win32.WM_HOTKEY:
                if ((int)w == SEARCH_HOTKEY_ID) { ToggleSpotlight(); return 0; }
                if ((int)w == RADIAL_HOTKEY_ID) { ToggleRadial(); return 0; }
                if ((int)w == AI_HOTKEY_ID) { ToggleAi(); return 0; }
                if (Mode == Mode.Expanded) Collapse();
                else { Expand(Tab); BeginKeyboard(); }
                return 0;
            case Win32.WM_TIMER:
                OnTimer((int)w);
                return 0;
            case Win32.WM_CLIPBOARDUPDATE:
                if (Clip.OnUpdate(out var text))
                {
                    if (Mode != Mode.Expanded && Store.Current.NotifyClipboard)
                        ShowLive(new LiveInfo("Kopyalandı", new ClipItem(text!, DateTime.Now).Preview, Glyph.Copy, Theme.Accent, false, 3, 1.8f));
                    _dirty = true;
                }
                return 0;
            case Win32.WM_DROPFILES:
                OnDrop(w);
                return 0;
            case Win32.WM_DISPLAYCHANGE:
            case Win32.WM_DPICHANGED:
                UpdateMonitor();
                return 0;
            case Win32.WM_SETTINGCHANGE:
                if ((int)w == 0x14) Dock?.ReloadBackdrop(); // SPI_SETDESKWALLPAPER: buzlu camı yenile
                _dirty = true;
                break;
            case Dispatcher.WM_RUN:
                Dispatcher.Drain();
                _dirty = true;
                InvalidateSurfaces();
                return 0;
            case 0x4A: // WM_COPYDATA: başka Ada süreci "--pin <yol>" gönderdi
            {
                var cds = (COPYDATASTRUCT*)l;
                if (cds->dwData is 1 or 2 && cds->cbData > 0)
                {
                    var path = new string((char*)cds->lpData, 0, cds->cbData / 2).TrimEnd('\0');
                    if (cds->dwData == 1) Dispatcher.Post(() => PinToDock(path));
                    else Dispatcher.Post(() => AddToRadial(path));
                }
                return 1;
            }
            case MouseHook.WM_HOOK:
                switch ((int)w)
                {
                    case 1: MouseHook.Replay(true, false); break; // sürükleme başladı: basışı geri ver
                    case 2: MouseHook.Replay(true, true); break;  // kısa sağ tık: aynen geri gönder
                    case 3: Radial?.Released(); break;
                    case 4: Radial?.Close(); break;
                }
                return 0;
            case WM_OPEN_SETTINGS:
                OpenSettings();
                return 0;
            case Win32.WM_DESTROY:
                Win32.PostQuitMessage(0);
                return 0;
        }
        return Win32.DefWindowProcW(h, m, w, l);
    }

    // ================= döngü =================

    public void Run()
    {
        MSG msg;
        while (true)
        {
            if (_animating || SurfacesAnimating())
            {
                while (Win32.PeekMessageW(out msg, IntPtr.Zero, 0, 0, Win32.PM_REMOVE))
                {
                    if (msg.message == WM_QUIT) return;
                    Win32.TranslateMessage(ref msg);
                    Win32.DispatchMessageW(ref msg);
                }
                if (!_shown && Concealed) _animating = false; // gizliyken ada için asla dönme
                double t0 = _sw.Elapsed.TotalMilliseconds;
                if (_animating || _dirty) Frame();
                FrameSurfaces();
                ReplayDeferred();
                if (!_animating && !SurfacesAnimating()) continue;
                // vsync; DwmFlush başarısız olur ya da anında dönerse döngüyü ~120 fps'te sınırla
                if (Win32.DwmFlush() != 0 || _sw.Elapsed.TotalMilliseconds - t0 < 4) Thread.Sleep(8);
            }
            else
            {
                if (Win32.GetMessageW(out msg, IntPtr.Zero, 0, 0) <= 0) return;
                Win32.TranslateMessage(ref msg);
                Win32.DispatchMessageW(ref msg);
                while (Win32.PeekMessageW(out msg, IntPtr.Zero, 0, 0, Win32.PM_REMOVE))
                {
                    if (msg.message == WM_QUIT) return;
                    Win32.TranslateMessage(ref msg);
                    Win32.DispatchMessageW(ref msg);
                }
                if (_dirty) Frame();
                FrameSurfaces();
                ReplayDeferred();
            }
        }
    }

    Rect IslandRect()
    {
        return new Rect(Margin, TopGap, _w.Value, _h.Value);
    }

    bool OverIsland()
    {
        if (!U.Inside) return false;
        var r = IslandRect();
        // ince çizgi halindeyken yakalaması kolay olsun: dikeyde birkaç piksel pay
        if (r.Height < 10) r = new Rect(r.X - 10, 0, r.Width + 20, r.Bottom + 6);
        return r.Contains(U.Mouse);
    }

    /// <summary>Ön plandaki uygulama ekranın üstünü (adanın olduğu yeri) kaplıyor mu?</summary>
    bool _appCovering;

    /// <summary>Görünürlük: ayar + "uygulama üstteyken fare gelince" kuralı.</summary>
    int EffectiveVisibility => _appCovering && Store.Current.AppOverlapMode == 2 && Store.Current.Visibility == 0 ? 1 : Store.Current.Visibility;

    void CheckCovering()
    {
        bool cov = false;
        var fg = Win32.GetForegroundWindow();
        if (fg != IntPtr.Zero && fg != Hwnd && SurfaceFor(fg) == null && fg != Win32.GetShellWindow() && Win32.GetWindowRect(fg, out var r))
        {
            var cls = Win32.ClassName(fg);
            int cx = (_mon.Left + _mon.Right) / 2;
            cov = cls is not ("WorkerW" or "Progman" or "Shell_TrayWnd")
                  && (Win32.IsZoomed(fg) || r.Top <= _mon.Top + 4)
                  && r.Left < cx && r.Right > cx && r.Bottom > _mon.Top + 40;
        }
        if (cov == _appCovering) return;
        _appCovering = cov;
        _awayTicks = 0;
        _dirty = true; _animating = true;
    }

    void Frame()
    {
        if (G.InDraw) { _dirty = true; return; }
        _dbgFrames++;
        double now = _sw.Elapsed.TotalSeconds;
        float dt = (float)(now - _last);
        _last = now;
        if (!_wasAnimating || SnapMode) dt = 1 / 60f;
        dt = Math.Clamp(dt, 1 / 240f, 1 / 24f);
        U.Dt = dt; U.Time += dt;
        U.NeedsFrame = false; U.WantHand = false;

        // fare konumu: pencerenin son konumuna göre (pencere her karede hareket ediyor)
        if (!SnapMode && Win32.GetCursorPos(out var cp))
            U.Mouse = new Vector2((cp.X - _winX) / _scale, (cp.Y - _winY) / _scale);

        if (!SnapMode) UpdateMode();
        UpdateGeometry(dt);

        float W = _w.Value, H = _h.Value;
        // pencere boyutu: ada + gölge payı
        int pw = (int)MathF.Ceiling((W + Margin * 2) * _scale);
        int ph = (int)MathF.Ceiling((TopGap + H + Margin) * _scale);
        int cx = (_mon.Left + _mon.Right) / 2;
        _winX = cx - (int)MathF.Round((W / 2 + Margin) * _scale);
        _winY = _mon.Top + (int)MathF.Round(_hideY.Value * _scale);
        _winW = pw; _winH = ph;

        G.Begin(pw, ph);
        try { Draw(W, H); }
        finally { G.End(); }
        G.Present(Hwnd, _winX, _winY, pw, ph);

        bool anim = !_w.Settled || !_h.Settled || !_r.Settled || !_hideY.Settled || !_collA.Settled || !_liveA.Settled || !_notch.Settled
                    || _tabT < 1 || !_indX.Settled || !_indW.Settled || U.NeedsFrame;
        _animating = anim;
        _wasAnimating = anim;
        _dirty = false;
        U.EndFrame();
        U.GcAnims();

        if (Concealed && _hideY.Settled && _shown) { Win32.ShowWindow(Hwnd, Win32.SW_HIDE); _shown = false; }
    }

    void UpdateMode()
    {
        bool over = OverIsland();
        switch (Mode)
        {
            case Mode.Collapsed when over:
                Mode = Mode.Hover;
                if (Store.Current.OpenOnHover) Win32.SetTimer(Hwnd, T_HOVER, 220, IntPtr.Zero);
                break;
            case Mode.Hover when !over: Mode = Mode.Collapsed; break;
            case Mode.Expanded:
                if (over || U.Down || _keyboardMode) CancelCollapse();
                else if (U.Inside || !_tracking) ScheduleCollapse();
                if (U.Pressed && !over) Collapse();
                break;
        }

        if (Mode is Mode.Collapsed or Mode.Hover or Mode.Live)
        {
            if (over && U.Released && U.ActiveId == 0) Expand(Mode == Mode.Live && _live != null ? _live.TabOnClick : Tab);
            else if (over && U.Wheel < 0) Expand(Tab);
            else if (over && U.RightReleased) ShowMenu();
        }
        if (over) U.WantHand |= Mode != Mode.Expanded;
    }

    void UpdateGeometry(float dt)
    {
        float k = SizeK, rnd = Store.Current.Roundness;
        float cw = IdleWidth(k), ch = CH * k;
        // tarayıcı vb. ekranın üstünü kaplarken: sekmelerin önüne geçmeyen ince çizgi
        bool mini = _appCovering && Store.Current.AppOverlapMode == 1 && Mode == Mode.Collapsed;
        if (mini) { cw = 72; ch = 5; }
        (float w, float h, float r) target = Mode switch
        {
            Mode.Hover => (cw + 16 * k, ch + 4 * k, (ch + 4 * k) / 2 * rnd),
            Mode.Live => (LW, LH, LH / 2 * rnd),
            Mode.Expanded => (EW, EH, ER * (0.5f + 0.5f * rnd)),
            _ => (cw, ch, ch / 2 * rnd),
        };
        bool growing = target.h > _h.Target + 0.5f || target.w > _w.Target + 0.5f;
        float sp = new[] { 0.6f, 1f, 1.55f }[Store.Current.AnimSpeed];
        float damp = (growing ? 24f : 31f) * MathF.Sqrt(sp), stiff = (growing ? 300f : 340f) * sp;
        _w.Damping = _h.Damping = _r.Damping = damp;
        _w.Stiffness = _h.Stiffness = _r.Stiffness = stiff;
        _w.Target = target.w; _h.Target = target.h; _r.Target = target.r;
        _w.Step(dt); _h.Step(dt); _r.Step(dt);
        _hideY.Target = Concealed ? -(CH * 1.2f + Top + 16) : 0;
        _hideY.Step(dt);

        _notch.Target = Store.Current.Notch ? 1 : 0; _notch.Step(dt);
        _collA.Target = (Mode is Mode.Collapsed or Mode.Hover) && !mini ? 1 : 0; _collA.Step(dt);
        _liveA.Target = Mode == Mode.Live ? 1 : 0; _liveA.Step(dt);

        if (_tabT < 1) _tabT = MathF.Min(1, _tabT + dt / 0.34f);
    }

    // ================= çizim =================

    void Draw(float W, float H)
    {
        var isl = IslandRect();
        float R = Math.Min(_r.Value, H / 2);
        float e = Ease.Clamp01((H - CH) / (EH - CH));

        if (SnapMode) // test görüntüsü için duvar kağıdı benzeri arka plan
            G.VGradientRound(new Rect(0, 0, W + Margin * 2, TopGap + H + Margin), 0, Theme.Rgb(0x6B7FA8), Theme.Rgb(0xC9B79C));

        // çentik modunda gövde yukarı uzar: üst köşeler ekranın dışında kalır → kenara yapışık görünür
        float n = _notch.Value;
        var body = new Rect(isl.X, isl.Y - R * n, isl.Width, isl.Height + R * n);

        // gölge + gövde
        if (Store.Current.Shadow) G.Shadow(body, R, 7 + 11 * e, (0.30f + 0.12f * e) * (1 - 0.35f * n), 3 + 5 * e);
        G.FillRound(body, R, Theme.Island);
        if (n > 0.01f)
        {
            float ear = MathF.Min(9 + 7 * e, H * 1.4f) * n; // ince çizgide kulaklar da küçülür
            G.NotchEar(new Vector2(isl.X + 0.5f, 0), ear, true, Theme.Island);
            G.NotchEar(new Vector2(isl.Right - 0.5f, 0), ear, false, Theme.Island);
        }
        // medya ışıması (açık + medya sekmesi)
        float expA = Mode == Mode.Expanded ? Ease.SmoothStep(0.62f, 0.98f, e) : Ease.SmoothStep(0.82f, 1f, e);
        if (expA > 0.01f && Media.HasSession && (Tab == 0 || _prevTab == 0 && _tabT < 1))
        {
            float tabW = Tab == 0 ? (_prevTab == 0 || _tabT >= 1 ? 1 : Ease.OutCubic(_tabT)) : 1 - Ease.OutCubic(_tabT);
            G.Alpha = expA * tabW;
            var c = new Vector2(isl.X + 120, isl.Y + 200);
            G.Glow(c, 420, 300, MediaColor.WithA(0.30f), body, R);
            G.Glow(new Vector2(isl.Right - 60, isl.Y + 20), 260, 160, MediaColor.WithA(0.10f), body, R);
            G.Alpha = 1;
        }
        // üst parlama + kenar (çentikte yok: donanım çentiği gibi saf siyah)
        G.Alpha = 1 - n;
        if (G.Alpha > 0.01f)
        {
            G.VGradientRound(new Rect(isl.X, isl.Y, isl.Width, MathF.Min(isl.Height, 70)), R, Theme.Rgb(0xFFFFFF, 0.075f), Theme.Rgb(0xFFFFFF, 0));
            G.StrokeRound(new Rect(isl.X + 0.5f, isl.Y + 0.5f, isl.Width - 1, isl.Height - 1), R, Theme.Rgb(0xFFFFFF, 0.07f + 0.03f * e), 1f);
        }
        G.Alpha = 1;

        G.PushClip(isl);
        float ca = _collA.Value * (1 - Ease.SmoothStep(0, 0.15f, e));
        if (ca > 0.01f) DrawCollapsed(isl, ca);
        if (_liveA.Value > 0.01f && _live != null) DrawLive(isl, _liveA.Value * (1 - Ease.SmoothStep(0.12f, 0.3f, e)));
        if (expA > 0.01f) DrawExpanded(isl, expA);
        G.PopClip();
        G.Alpha = 1;
    }

    void DrawCollapsed(Rect isl, float a)
    {
        G.Alpha = a;
        DrawIdle(isl, SizeK);
        G.Alpha = 1;
    }

    public static readonly float[] SizeFactors = { 0.88f, 1f, 1.14f };
    float SizeK => SizeFactors[Store.Current.Size];
    bool PlayingInIdle => Media.HasSession && Media.Playing && Store.Current.MediaInIdle;

    string? SlotText(Slot s)
    {
        var now = DateTime.Now;
        switch (s)
        {
            case Slot.Clock: return now.ToString("HH:mm");
            case Slot.ClockSeconds: return now.ToString("HH:mm:ss");
            case Slot.Date: return now.ToString("d MMM ddd", Tr);
            case Slot.Battery:
                Stats.SampleBatteryOnly();
                return Stats.Battery >= 0 ? $"%{Stats.Battery}" : now.ToString("d MMM", Tr);
            case Slot.CpuRam: return $"{Stats.Cpu * 100:0}% · {Stats.Ram * 100:0}%";
            default: return null;
        }
    }

    /// <summary>Boştaki yuvaların ihtiyaç duyduğu genişlik (metinler çakışmasın).</summary>
    float IdleWidth(float k)
    {
        var s = Store.Current;
        bool playing = PlayingInIdle;
        float side(Slot slot, bool media) => media ? 36 * k : slot switch
        {
            Slot.None => 8, Slot.Dot => 22 * k,
            _ => G.Measure(SlotText(slot) ?? "", 11.5f * k, FontWeight.SemiBold) + 22 * k
        };
        float center = s.IdleCenter is Slot.None or Slot.Dot ? 20 * k : G.Measure(SlotText(s.IdleCenter) ?? "", 13.5f * k, FontWeight.SemiBold) + 20 * k;
        float l = side(s.IdleLeft, playing), r = side(s.IdleRight, playing);
        return MathF.Max((playing ? PlayW : CW) * k, center + 2 * MathF.Max(l, r) + 8);
    }

    /// <summary>Kapalı hapın içeriği (ayarlar önizlemesi de bunu kullanır).</summary>
    public void DrawIdle(Rect isl, float k)
    {
        var s = Store.Current;
        float cy = isl.CenterY;
        bool playing = PlayingInIdle;

        void Side(Slot slot, bool left)
        {
            if (slot == Slot.Dot)
            {
                G.FillCircle(new Vector2(left ? isl.X + 18 * k : isl.Right - 18 * k, cy), 3.2f * k, Theme.Accent.WithA(0.9f));
                return;
            }
            var txt = SlotText(slot);
            if (txt == null) return;
            float fs = 11.5f * k;
            var r = left ? new Rect(isl.X + 13 * k, isl.Y, isl.Width / 2, isl.Height) : new Rect(isl.CenterX, isl.Y, isl.Width / 2 - 13 * k, isl.Height);
            G.Text(txt, r, fs, Theme.Text2, FontWeight.SemiBold, left ? TextAlignment.Leading : TextAlignment.Trailing);
            if (slot == Slot.Battery && Stats.Battery >= 0 && Stats.Charging)
            {
                float tw = G.Measure(txt, fs, FontWeight.SemiBold);
                float ix = left ? isl.X + 13 * k + tw + 9 * k : isl.Right - 13 * k - tw - 9 * k;
                G.Icon(Glyph.Charging, new Vector2(ix, cy), 10 * k, Theme.Green);
            }
        }

        // orta
        if (s.IdleCenter == Slot.Dot) G.FillCircle(new Vector2(isl.CenterX, cy), 3.4f * k, Theme.Accent);
        else if (SlotText(s.IdleCenter) is string c)
            G.Text(c, new Rect(isl.CenterX - 80, isl.Y, 160, isl.Height), 13.5f * k, Theme.Text, FontWeight.SemiBold, TextAlignment.Center);

        if (playing)
        {
            float a = 22 * k;
            var ar = new Rect(isl.X + 7 * k, cy - a / 2, a, a);
            if (MediaArt != null) G.BitmapRound(MediaArt, ar, 7 * k);
            else { G.FillRound(ar, 7 * k, MediaColor.WithA(0.35f)); G.Icon(Glyph.Music, new Vector2(ar.CenterX, ar.CenterY), 11 * k, Theme.Text); }
            DrawBars(new Vector2(isl.Right - 30 * k, cy), 14 * k, MediaColor, true);
        }
        else
        {
            Side(s.IdleLeft, true);
            Side(s.IdleRight, false);
        }
    }

    public static readonly System.Globalization.CultureInfo Tr = new("tr-TR");

    /// <summary>Ekolayzer çubukları (çalarken).</summary>
    public void DrawBars(Vector2 c, float h, Color4 col, bool animate)
    {
        float t = U.Time;
        float[] ph = { 0f, 1.7f, 0.9f, 2.6f };
        for (int i = 0; i < 4; i++)
        {
            float v = animate && Media.Playing
                ? 0.3f + 0.7f * MathF.Abs(MathF.Sin(t * (5.1f + i * 1.3f) + ph[i])) * (0.6f + 0.4f * MathF.Abs(MathF.Sin(t * 1.7f + i)))
                : 0.25f;
            float bh = MathF.Max(3, h * v);
            float x = c.X - 9 + i * 6;
            G.FillRound(new Rect(x - 1.5f, c.Y - bh / 2, 3, bh), 1.5f, Theme.Mix(col, Theme.Text, 0.15f));
        }
    }

    void DrawLive(Rect isl, float a)
    {
        var lv = _live!;
        G.Alpha = a;
        float cy = isl.CenterY;
        var ir = new Rect(isl.X + 12, cy - 19, 38, 38);
        if (lv.IsMedia && MediaArt != null) G.BitmapRound(MediaArt, ir, 11);
        else
        {
            G.FillRound(ir, 19, lv.Color.WithA(0.22f));
            G.Icon(lv.Glyph, new Vector2(ir.CenterX, ir.CenterY), 15, lv.Color);
        }
        float tx = ir.Right + 12, tw = isl.Right - tx - (lv.IsMedia ? 52 : 20);
        G.Text(lv.Title, new Rect(tx, cy - 19, tw, 20), 14, Theme.Text, FontWeight.SemiBold);
        G.Text(lv.Sub, new Rect(tx, cy + 1, tw, 18), 12, Theme.Text2, FontWeight.Normal, font: Theme.TextFont);
        if (lv.IsMedia) DrawBars(new Vector2(isl.Right - 30, cy), 18, MediaColor, true);
        G.Alpha = 1;
    }

    void DrawExpanded(Rect isl, float a)
    {
        // içerik sabit yerleşimle çizilir (morf sırasında yeniden akmaz), hafif büyüme + aşağı kayma ile gelir
        float cx = isl.CenterX;
        var origin = new Vector2(cx, isl.Y);
        // içerik adanın o anki genişliğiyle birlikte ölçeklenir (kenarlardan taşmaz)
        float s = MathF.Min(1f, MathF.Max(0.9f, isl.Width / EW)) * (0.97f + 0.03f * a);
        G.PushTranslate(0, -8 * (1 - a), s, origin);
        bool interactive = Mode == Mode.Expanded && a > 0.85f;
        bool savedInside = U.Inside;
        if (!interactive) U.Inside = false;

        G.Alpha = a;
        DrawTabs(cx, isl.Y + 10, a);
        if (U.IconButton(Ui.Id("open.settings"), new Vector2(cx + EW / 2 - 28, isl.Y + 25), 13, Glyph.Settings, 12, Theme.Text3))
            Dispatcher.Post(OpenSettings);

        var area = new Rect(cx - EW / 2 + 26, isl.Y + 54, EW - 52, EH - 54 - 20);
        if (_tabT < 1 && _prevTab >= 0)
        {
            float t = Ease.OutCubic(_tabT);
            U.Inside = false;
            G.Alpha = a * (1 - Ease.Clamp01(_tabT * 2.2f));
            G.PushTranslate(-_tabDir * 26 * t, 0);
            Panels[_prevTab].Draw(area);
            G.PopTransform();
            U.Inside = interactive && savedInside;
            G.Alpha = a * Ease.OutCubic(Ease.Clamp01((_tabT - 0.12f) / 0.88f));
            G.PushTranslate(_tabDir * 26 * (1 - t), 0);
            Panels[Tab].Draw(area);
            G.PopTransform();
            U.NeedsFrame = true;
        }
        else
        {
            G.Alpha = a;
            Panels[Tab].Draw(area);
        }
        G.Alpha = 1;
        U.Inside = savedInside;
        G.PopTransform();
    }

    public string TabName(int i) => Panels[i].Name;

    /// <summary>Videodaki gibi sade metin sekmeleri: aktif olan vurgu renginde, altında kayan küçük çizgi.</summary>
    void DrawTabs(float cx, float y, float a)
    {
        const float h = 30, pad = 14, fs = 12.5f;
        var vis = Store.Current.VisibleTabs();
        Span<float> widths = stackalloc float[vis.Count];
        float total = 0;
        for (int k = 0; k < vis.Count; k++)
        {
            widths[k] = G.Measure(Panels[vis[k]].Name, fs, FontWeight.SemiBold) + pad * 2;
            total += widths[k];
        }
        float x = cx - total / 2;
        float activeX = 0, activeW = 0, xx = x;
        for (int k = 0; k < vis.Count; k++)
        {
            if (vis[k] == Tab) { activeX = xx; activeW = widths[k]; }
            xx += widths[k];
        }
        if (!_indInit) { _indX.Snap(activeX - cx); _indW.Snap(activeW); _indInit = true; }
        _indX.Target = activeX - cx; _indW.Target = activeW;
        _indX.Step(U.Dt); _indW.Step(U.Dt);

        // kayan alt çizgi
        float lw = MathF.Max(12, _indW.Value - pad * 2 - 8);
        G.FillRound(new Rect(cx + _indX.Value + (_indW.Value - lw) / 2, y + h - 3, lw, 3), 1.5f, Theme.Accent);

        for (int k = 0; k < vis.Count; k++)
        {
            int i = vis[k];
            var r = new Rect(x, y, widths[k], h);
            int id = Ui.Id("tab", i);
            if (U.Click(id, r)) SwitchTab(i);
            float hv = U.Anim(id, U.Hit(r) ? 1 : 0);
            float act = U.Anim(id + 77, i == Tab ? 1 : 0, 14);
            var col = Theme.Mix(Theme.Mix(Theme.Text3, Theme.Text, 0.15f + 0.6f * hv), Theme.Accent, act);
            G.Text(Panels[i].Name, r, fs, col, i == Tab ? FontWeight.Bold : FontWeight.SemiBold, TextAlignment.Center);
            x += widths[k];
        }
    }

    // ================= durum geçişleri =================

    /// <summary>"Fare gelince göster" modunda çekilmiş adayı geri getirir.</summary>
    void Reveal()
    {
        _awayTicks = 0;
        if (!Store.Current.IslandEnabled) return;
        if (!_autoHidden) return;
        _autoHidden = false;
        if (!_shown && !SnapMode) { Win32.ShowWindow(Hwnd, Win32.SW_SHOWNOACTIVATE); _shown = true; }
        UpdateEqTimer();
        _dirty = true; _animating = true;
    }

    void Conceal()
    {
        if (_autoHidden) return;
        _autoHidden = true;
        Mode = Mode.Collapsed;
        UpdateEqTimer();
        _dirty = true; _animating = true;
    }

    /// <summary>100 ms'de bir (yalnızca görünürlük modu 1/2 iken): imleç üst kenara geldi mi, ada boşta mı.</summary>
    void ProximityTick()
    {
        Dock?.Proximity();
        if (Store.Current.AppOverlapMode != 0) CheckCovering();
        var s = Store.Current;
        int vis = EffectiveVisibility;
        if (vis == 0 || _hidden || SnapMode) { if (_autoHidden && vis == 0) Reveal(); return; }
        if (!Win32.GetCursorPos(out var p)) return;
        int cx = (_mon.Left + _mon.Right) / 2;
        bool nearTop = p.Y <= _mon.Top + 2 && Math.Abs(p.X - cx) < 280 * _scale && p.X >= _mon.Left && p.X < _mon.Right;
        if (_autoHidden)
        {
            if (vis == 1 && nearTop) Reveal();
            return;
        }
        bool overWin = p.X >= _winX && p.X <= _winX + _winW && p.Y >= _mon.Top && p.Y <= _winY + _winH + 8;
        if (Mode == Mode.Collapsed && !overWin && !nearTop && !_keyboardMode)
        {
            if (++_awayTicks >= 12) Conceal(); // ~1.2 sn boşta → çekil
        }
        else _awayTicks = 0;
    }

    public void Expand(int tab)
    {
        if (_hidden) return;
        Reveal();
        _live = null;
        Win32.KillTimer(Hwnd, T_LIVE);
        var vis = Store.Current.VisibleTabs();
        if (!vis.Contains(tab)) tab = vis[0];
        if (tab != Tab) { Panels[Tab].OnHide(); Tab = tab; }
        _tabT = 1; _prevTab = -1; _indInit = false; // gösterge eski sekmeden kaymasın
        Mode = Mode.Expanded;
        _trimmed = false;
        Panels[Tab].OnShow();
        Win32.KillTimer(Hwnd, T_EQ);
        _dirty = true;
    }

    public void Collapse()
    {
        if (Mode != Mode.Expanded) return;
        CancelCollapse();
        Panels[Tab].OnHide();
        EndKeyboard(true);
        Mode = Mode.Collapsed;
        Store.Current.LastTab = Tab;
        Store.Save();
        _idleTicks = 0;
        UpdateEqTimer();
        _dirty = true;
        if (_pendingLive != null) { var p = _pendingLive; _pendingLive = null; ShowLive(p); }
    }

    public void SwitchTab(int i)
    {
        if (i == Tab) return;
        Panels[Tab].OnHide();
        EndKeyboard(false);
        var vis = Store.Current.VisibleTabs();
        _prevTab = Tab; _tabDir = vis.IndexOf(i) > vis.IndexOf(Tab) ? 1 : -1;
        Tab = i; _tabT = 0;
        Panels[Tab].OnShow();
        _dirty = true;
    }

    void ScheduleCollapse()
    {
        if (_collapsePending) return;
        _collapsePending = true;
        Win32.SetTimer(Hwnd, T_COLLAPSE, (uint)Store.Current.CollapseDelayMs, IntPtr.Zero);
    }

    void CancelCollapse()
    {
        if (!_collapsePending) return;
        _collapsePending = false;
        Win32.KillTimer(Hwnd, T_COLLAPSE);
    }

    void ShowLive(LiveInfo info)
    {
        if (_hidden || !Store.Current.IslandEnabled) return;
        if (Mode == Mode.Expanded) { if (!info.IsMedia && info.TabOnClick == 1) _pendingLive = info; return; }
        Reveal(); // bildirimler gizli moddayken de görünür
        _live = info;
        Mode = Mode.Live;
        Win32.SetTimer(Hwnd, T_LIVE, (uint)(info.Seconds * 1000), IntPtr.Zero);
        UpdateEqTimer();
        _dirty = true;
    }

    /// <summary>Metin kutusu için klavye odağı al.</summary>
    public void BeginKeyboard()
    {
        if (_keyboardMode) return;
        _keyboardMode = true;
        _prevForeground = Win32.GetForegroundWindow();
        int ex = Win32.GetWindowLongW(Hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLongW(Hwnd, Win32.GWL_EXSTYLE, ex & ~Win32.WS_EX_NOACTIVATE);
        Win32.SetForegroundWindow(Hwnd);
        Win32.SetTimer(Hwnd, T_BLINK, 530, IntPtr.Zero);
    }

    public void EndKeyboard(bool restoreFocus)
    {
        if (!_keyboardMode) return;
        _keyboardMode = false;
        U.FocusId = 0;
        int ex = Win32.GetWindowLongW(Hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLongW(Hwnd, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_NOACTIVATE);
        Win32.KillTimer(Hwnd, T_BLINK);
        if (restoreFocus && _prevForeground != IntPtr.Zero && Win32.GetForegroundWindow() == Hwnd)
            Win32.SetForegroundWindow(_prevForeground);
        _prevForeground = IntPtr.Zero;
    }

    public bool KeyboardMode => _keyboardMode;

    void OnKey(int vk)
    {
        bool ctrl = Win32.GetKeyState(0x11) < 0;
        if (Mode == Mode.Expanded && Panels[Tab].OnKey(vk, ctrl)) return;
        if (vk == 0x1B) { Collapse(); return; } // Esc
        if (Mode != Mode.Expanded) return;
        var vis = Store.Current.VisibleTabs();
        int pos = Math.Max(0, vis.IndexOf(Tab)), n = vis.Count;
        bool shift = Win32.GetKeyState(0x10) < 0;
        if (vk == 0x09) SwitchTab(vis[(pos + (shift ? n - 1 : 1)) % n]);
        else if (vk == 0x27) SwitchTab(vis[(pos + 1) % n]);
        else if (vk == 0x25) SwitchTab(vis[(pos + n - 1) % n]);
        else if (vk >= 0x31 && vk < 0x31 + n) SwitchTab(vis[vk - 0x31]);
    }

    void OnTimer(int id)
    {
        if (id == 8801)
        {
            Win32.KillTimer(Hwnd, 8801);
            if (MouseHook.BeginHold()) { Collapse(); Radial ??= new RadialWindow(this); Radial.Open(MouseHook.DownPoint, true); }
            return;
        }
        switch (id)
        {
            case T_TICK: Tick(); break;
            case T_COLLAPSE:
                Win32.KillTimer(Hwnd, T_COLLAPSE);
                _collapsePending = false;
                if (!OverIsland() && !U.Down && !_keyboardMode) Collapse();
                break;
            case T_EQ:
            case T_BLINK:
                _dirty = true; break;
            case T_PROX:
                ProximityTick();
                break;
            case T_HOVER:
                Win32.KillTimer(Hwnd, T_HOVER);
                if (Mode == Mode.Hover && OverIsland()) Expand(Tab);
                break;
            case T_LIVE:
                Win32.KillTimer(Hwnd, T_LIVE);
                if (Mode == Mode.Live) { Mode = OverIsland() ? Mode.Hover : Mode.Collapsed; _dirty = true; }
                UpdateEqTimer();
                break;
        }
    }

    // ADA_DEBUG=1 → saniyede bir kare sayısı / durum log'a yazılır (performans teşhisi)
    static readonly bool Debug = Environment.GetEnvironmentVariable("ADA_DEBUG") == "1";
    int _dbgFrames;
    void Tick()
    {
        var now = DateTime.Now;
        if (Debug) Log.Info($"dbg fps={_dbgFrames} anim={_animating} mode={Mode} hidden={_hidden} playing={Media.Playing}");
        _dbgFrames = 0;
        if (now.Minute != _minute) { _minute = now.Minute; _dirty = true; }
        if (Mode == Mode.Expanded) Panels[Tab].Tick();
        ((TodayPanel)Panels[1]).BackgroundTick();
        CheckCovering();
        Settings?.Invalidate();
        Spotlight?.Tick(); Dock?.Tick();
        // boştaki yuvalar: saniyeli saat her saniye, CPU·RAM iki saniyede bir
        var st = Store.Current;
        bool Uses(Slot s) => st.IdleLeft == s || st.IdleCenter == s || st.IdleRight == s;
        if (Mode != Mode.Expanded && Uses(Slot.ClockSeconds)) _dirty = true;
        if (Mode != Mode.Expanded && Uses(Slot.CpuRam) && now.Second % 2 == 0) { Stats.SampleLight(); _dirty = true; }
        CheckReminders(now);
        if (now.Second % 2 == 0 && !SnapMode) CheckFullscreen();

        // boşta bellek kırpma: kapalı ve hareketsiz 4 sn sonra bir kez
        if (Mode is Mode.Collapsed && !_animating)
        {
            if (++_idleTicks % 60 == 0) _trimmed = false; // boşta her dakika yeniden kırp (Dock sürekli çalışıyor)
            if (_idleTicks % 60 == 4 && !_trimmed && Settings == null && Spotlight?.IsOpen != true)
            {
                Stats.Pause();
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                Win32.SetProcessWorkingSetSize(Win32.GetCurrentProcess(), -1, -1);
                _trimmed = true;
            }
        }
        else _idleTicks = 0;
    }

    void CheckReminders(DateTime now)
    {
        bool changed = false;
        foreach (var t in Store.Current.Todos)
        {
            if (t.Done || t.Notified || t.Due is not DateTime d || d > now) continue;
            t.Notified = true; changed = true;
            if (!Store.Current.NotifyReminder) continue;
            if (Store.Current.ReminderSound) Beep();
            ShowLive(new LiveInfo(t.Text, "Hatırlatıcı · " + d.ToString("HH:mm"), Glyph.Bell, Theme.Orange, false, 1, 9f));
        }
        if (changed) Store.Save();
    }

    [DllImport("user32.dll")] static extern bool MessageBeep(uint type);
    public void Beep() => MessageBeep(0x40);

    void CheckFullscreen()
    {
        if (!Store.Current.IslandEnabled) return;
        bool fs = false;
        if (Win32.SHQueryUserNotificationState(out var st) == 0 && st is 3 or 4) fs = true; // D3D tam ekran / sunum
        var fg = Win32.GetForegroundWindow();
        if (!fs && fg != IntPtr.Zero && fg != Hwnd && fg != Win32.GetDesktopWindow() && fg != Win32.GetShellWindow())
        {
            var cls = Win32.ClassName(fg);
            if (cls is not ("WorkerW" or "Progman" or "Shell_TrayWnd") && Win32.GetWindowRect(fg, out var r))
            {
                int style = Win32.GetWindowLongW(fg, -16);
                bool noCaption = (style & 0xC00000) != 0xC00000;
                fs = noCaption && r.Left <= _mon.Left && r.Top <= _mon.Top && r.Right >= _mon.Right && r.Bottom >= _mon.Bottom;
            }
        }
        if (fs == _hidden) return;
        if (fs && Mode == Mode.Expanded) return;
        _hidden = fs;
        if (fs) { Mode = Mode.Collapsed; _live = null; }
        else if (!_shown) { Win32.ShowWindow(Hwnd, Win32.SW_SHOWNOACTIVATE); _shown = true; }
        UpdateEqTimer();
        _dirty = true;
    }

    void UpdateEqTimer()
    {
        bool need = Media.HasSession && Media.Playing && Mode != Mode.Expanded && !Concealed;
        if (need) Win32.SetTimer(Hwnd, T_EQ, 70, IntPtr.Zero);
        else Win32.KillTimer(Hwnd, T_EQ);
    }

    void OnMediaChanged()
    {
        if (Media.ArtVersion != _mediaArtVersion)
        {
            _mediaArtVersion = Media.ArtVersion;
            var old = MediaArt;
            ((ControlsPanel)Panels[0]).Media.PushOldArt(old);
            MediaArt = null;
            if (Media.Art != null)
            {
                MediaArt = G.LoadImage(Media.Art, (int)(200 * _scale), out var dom);
                MediaColor = MediaArt != null ? dom : Theme.Accent;
            }
            else MediaColor = Theme.Accent;
        }
        UpdateEqTimer();
        _dirty = true;
    }

    void OnDrop(IntPtr hDrop)
    {
        char* buf = stackalloc char[1024];
        uint n = Win32.DragQueryFileW(hDrop, 0xFFFFFFFF, null, 0);
        int added = 0;
        for (uint i = 0; i < n; i++)
        {
            uint len = Win32.DragQueryFileW(hDrop, i, buf, 1024);
            var path = new string(buf, 0, (int)len);
            if (Store.Current.Apps.Any(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
            Store.Current.Apps.Add(AppsService.FromPath(path));
            added++;
        }
        Win32.DragFinish(hDrop);
        if (added > 0)
        {
            Store.Save();
            if (Mode == Mode.Expanded) SwitchTab(4);
            else ShowLive(new LiveInfo($"{added} uygulama eklendi", "Uygulamalar sekmesinde", Glyph.Apps, Theme.Accent, false, 4, 2f));
        }
    }

    void ShowMenu()
    {
        var menu = Win32.CreatePopupMenu();
        Win32.AppendMenuW(menu, Win32.MF_STRING, 6, "Ada Ayarları…");
        Win32.AppendMenuW(menu, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(menu, Win32.MF_STRING | (!NotchStyle ? Win32.MF_CHECKED : 0), 4, "Görünüm: Ada (yüzen)");
        Win32.AppendMenuW(menu, Win32.MF_STRING | (NotchStyle ? Win32.MF_CHECKED : 0), 5, "Görünüm: Çentik (üste yapışık)");
        Win32.AppendMenuW(menu, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(menu, Win32.MF_STRING | (AppsService.StartupEnabled ? Win32.MF_CHECKED : 0), 1, "Windows ile başlat");
        Win32.AppendMenuW(menu, Win32.MF_STRING, 2, "Veri klasörünü aç");
        Win32.AppendMenuW(menu, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(menu, Win32.MF_STRING, 3, "Ada'dan çık");
        Win32.GetCursorPos(out var p);
        Win32.SetForegroundWindow(Hwnd);
        int cmd = Win32.TrackPopupMenu(menu, Win32.TPM_RETURNCMD | Win32.TPM_RIGHTBUTTON, p.X, p.Y, 0, Hwnd, IntPtr.Zero);
        Win32.DestroyMenu(menu);
        switch (cmd)
        {
            case 1: AppsService.StartupEnabled = !AppsService.StartupEnabled; break;
            case 2: Directory.CreateDirectory(Store.Dir); Win32.ShellExecuteW(IntPtr.Zero, "open", Store.Dir, null, null, Win32.SW_SHOWNORMAL); break;
            case 3: Store.Save(); Win32.PostQuitMessage(0); break;
            case 4: NotchStyle = false; break;
            case 5: NotchStyle = true; break;
            case 6: OpenSettings(); break;
        }
    }

    // panellerin kullandığı yardımcılar
    public void Invalidate() => _dirty = true;
    public void Toast(string title, string sub, string glyph, Color4 color, int tab = 0, float seconds = 1.8f)
        => ShowLive(new LiveInfo(title, sub, glyph, color, false, tab, seconds));
}
