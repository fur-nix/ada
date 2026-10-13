using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Ada.Core;
using Ada.Data;
using Ada.Native;
using Ada.Services;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace Ada;

/// <summary>
/// macOS tarzı Dock: sabitlenmiş + açık uygulamalar, büyütme efekti, çalışma noktası, zıplayan açılış,
/// isteğe bağlı otomatik gizleme ve AppBar ile ekran alanı ayırma.
/// </summary>
public sealed unsafe class DockWindow : ISurface
{
    const float Margin = 30, Bottom = 8, Gap = 8, Pad = 10, LabelSpace = 40;
    static bool _registered;
    const uint WM_APPBAR = Win32.WM_APP + 5;

    readonly Island I;
    Gfx G => I.G;
    readonly Ui U;
    public IntPtr Hwnd { get; private set; }
    public bool Animating { get; private set; }
    bool _dirty = true, _tracking, _shown, _appbar, _autoHidden;
    readonly Stopwatch _sw = Stopwatch.StartNew();
    double _last;
    int _winX, _winY, _winW, _winH;
    Spring _hideY = new(0, 260, 30);
    int _awayTicks;
    float _baseBottomPx; // dock'un oturduğu y (piksel, ekran)

    sealed class Item
    {
        public string Name = "", Path = "";
        public string? Args;
        public bool Pinned, Launcher, Separator;
        public readonly List<IntPtr> Windows = new();
        public float BounceStart = -1;
        public string Key => Launcher ? "#launcher" : Separator ? "#sep" + Name : Path.ToLowerInvariant();
    }
    List<Item> _items = new();
    readonly Dictionary<string, string> _lnkTargets = new(StringComparer.OrdinalIgnoreCase);
    static List<IntPtr>? _enumBuf;

    float B => new[] { 40f, 50f, 62f }[Store.Current.DockSize];
    float Mag => Store.Current.DockMagnify ? 1.65f : 1f;

    public DockWindow(Island island)
    {
        I = island;
        U = new Ui(island.G);
        var inst = Win32.GetModuleHandleW(IntPtr.Zero);
        if (!_registered)
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Island.ProcPtr, hInstance = inst,
                hCursor = Win32.LoadCursorW(IntPtr.Zero, 32512), lpszClassName = Marshal.StringToHGlobalUni("AdaDock")
            };
            Win32.RegisterClassExW(ref wc);
            _registered = true;
        }
        Hwnd = Win32.CreateWindowExW(Win32.WS_EX_LAYERED | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST | Win32.WS_EX_NOACTIVATE,
            "AdaDock", "Ada Dock", Win32.WS_POPUP, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
        Refresh();
        if (I.SnapMode) return; // test: ekrana çıkma, görev çubuğuna dokunma
        Frame();
        Win32.ShowWindow(Hwnd, Win32.SW_SHOWNOACTIVATE);
        _shown = true;
        ApplyLayoutSettings();
    }

    /// <summary>--snap: Dock'u (isteğe bağlı fare konumuyla büyütülmüş) PNG'ye çizer.</summary>
    public void SnapRender(string path, float? mouseX)
    {
        _snapMouse = mouseX;
        for (int i = 0; i < 90; i++) Frame();
        G.SavePng(path, _winW, _winH);
        _snapMouse = null;
    }
    float? _snapMouse;

    public void Invalidate() => _dirty = true;
    public void FrameIfNeeded() { if (_dirty || Animating) Frame(); }

    // ---------------- ayarlar / ekran alanı ----------------
    public void ApplyLayoutSettings()
    {
        var s = Store.Current;
        Taskbar.SetHidden(s.HideTaskbar);
        bool reserve = s.DockReserve && !s.DockAutoHide;
        if (reserve) RegisterAppBar(); else RemoveAppBar();
        if (!s.DockAutoHide && _autoHidden) { _autoHidden = false; Show(); }
        _dirty = true; Animating = true;
    }

    float PlateH => B + Pad * 2;

    /// <summary>Dock'un oturacağı alt kenar: görev çubuğu görünürse onun üstü, değilse ekranın altı.</summary>
    int BaseBottom()
    {
        var mon = I.MonitorRect;
        var tray = Win32.FindWindowW("Shell_TrayWnd", null);
        if (tray != IntPtr.Zero && Win32.IsWindowVisible(tray) && !Store.Current.HideTaskbar && Win32.GetWindowRect(tray, out var r)
            && r.Top > (mon.Top + mon.Bottom) / 2 && r.Top < mon.Bottom - 4)
            return r.Top;
        return mon.Bottom;
    }

    void RegisterAppBar()
    {
        var abd = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = Hwnd, uCallbackMessage = WM_APPBAR };
        if (!_appbar) { Win32.SHAppBarMessage(0, ref abd); _appbar = true; } // ABM_NEW
        var mon = I.MonitorRect;
        int bottom = BaseBottom();
        int h = (int)MathF.Ceiling((PlateH + Bottom * 2) * I.Scale);
        abd.uEdge = 3; // ABE_BOTTOM
        abd.rc = new RECT { Left = mon.Left, Right = mon.Right, Bottom = bottom, Top = bottom - h };
        Win32.SHAppBarMessage(2, ref abd); // ABM_QUERYPOS
        abd.rc.Top = abd.rc.Bottom - h;
        Win32.SHAppBarMessage(3, ref abd); // ABM_SETPOS
    }

    public void RemoveAppBar()
    {
        if (!_appbar) return;
        var abd = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = Hwnd };
        Win32.SHAppBarMessage(1, ref abd); // ABM_REMOVE
        _appbar = false;
    }

    public void Shutdown()
    {
        RemoveAppBar();
        Taskbar.SetHidden(false);
        Win32.DestroyWindow(Hwnd);
    }

    // ---------------- öğeler ----------------
    [UnmanagedCallersOnly]
    static int EnumProc(IntPtr h, IntPtr l) { _enumBuf!.Add(h); return 1; }

    /// <summary>Sabitlenmiş + açık pencereleri topla (saniyede bir).</summary>
    void Refresh()
    {
        var s = Store.Current;
        var old = _items.ToDictionary(i => i.Key, i => i);
        var items = new List<Item> { new() { Name = "Uygulamalar", Launcher = true } };
        foreach (var a in s.Apps)
        {
            var it = old.TryGetValue(a.Path.ToLowerInvariant(), out var o) ? o : new Item();
            it.Name = a.Name; it.Path = a.Path; it.Args = a.Args; it.Pinned = true; it.Windows.Clear();
            items.Add(it);
        }

        if (s.DockShowRunning)
        {
            _enumBuf = new List<IntPtr>(256);
            Win32.EnumWindows(&EnumProc, IntPtr.Zero);
            var running = new List<Item>();
            foreach (var h in _enumBuf)
            {
                if (!IsAppWindow(h)) continue;
                Win32.GetWindowThreadProcessId(h, out int pid);
                if (pid == Environment.ProcessId) continue;
                var exe = Win32.ProcessPath(pid);
                if (exe == null) continue;
                var pinned = items.FirstOrDefault(i => i.Pinned && Same(i.Path, exe));
                if (pinned != null) { pinned.Windows.Add(h); continue; }
                var key = exe.ToLowerInvariant();
                var r = running.FirstOrDefault(i => i.Path.Equals(exe, StringComparison.OrdinalIgnoreCase));
                if (r == null)
                {
                    r = old.TryGetValue(key, out var o) && !o.Pinned ? o : new Item();
                    r.Path = exe; r.Pinned = false; r.Windows.Clear();
                    r.Name = PrettyName(exe, h);
                    running.Add(r);
                }
                r.Windows.Add(h);
            }
            _enumBuf = null;
            if (running.Count > 0)
            {
                items.Add(new Item { Separator = true, Name = "run" });
                items.AddRange(running);
            }
        }
        _items = items;
        _dirty = true;
    }

    static bool IsAppWindow(IntPtr h)
    {
        if (!Win32.IsWindowVisible(h) || Win32.GetWindow(h, 4) != IntPtr.Zero) return false; // GW_OWNER
        int ex = Win32.GetWindowLongW(h, Win32.GWL_EXSTYLE);
        if ((ex & Win32.WS_EX_TOOLWINDOW) != 0 && (ex & 0x40000) == 0) return false;
        if (Win32.GetWindowTextLengthW(h) == 0) return false;
        if (Win32.DwmGetWindowAttribute(h, 14, out int cloaked, 4) == 0 && cloaked != 0) return false; // gizli UWP
        var cls = Win32.ClassName(h);
        return cls is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Windows.UI.Core.CoreWindow");
    }

    bool Same(string pinnedPath, string exe)
    {
        if (pinnedPath.Equals(exe, StringComparison.OrdinalIgnoreCase)) return true;
        if (!pinnedPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return false;
        if (!_lnkTargets.TryGetValue(pinnedPath, out var t)) { t = ResolveLnk(pinnedPath) ?? ""; _lnkTargets[pinnedPath] = t; }
        return t.Equals(exe, StringComparison.OrdinalIgnoreCase);
    }

    static string? ResolveLnk(string lnk)
    {
        try
        {
            var clsid = new Guid("00021401-0000-0000-C000-000000000046");
            if (Win32.CoCreateInstance(clsid, IntPtr.Zero, 1, typeof(IShellLinkW).GUID, out var p) != 0) return null;
            var link = ComUtil.Wrap<IShellLinkW>(p);
            if (link == null || ((IPersistFile)(object)link).Load(lnk, 0) != 0) return null;
            var buf = Marshal.AllocHGlobal(1040);
            try { return link.GetPath(buf, 520, IntPtr.Zero, 0) == 0 ? Marshal.PtrToStringUni(buf) : null; }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { return null; }
    }

    static readonly Dictionary<string, string> NameCache = new(StringComparer.OrdinalIgnoreCase);

    static string PrettyName(string exe, IntPtr h)
    {
        if (NameCache.TryGetValue(exe, out var cached)) return cached;
        var n = ReadName(exe, h);
        if (NameCache.Count > 200) NameCache.Clear();
        NameCache[exe] = n;
        return n;
    }

    static string ReadName(string exe, IntPtr h)
    {
        try
        {
            var d = FileVersionInfo.GetVersionInfo(exe).FileDescription;
            if (!string.IsNullOrWhiteSpace(d) && d.Length <= 28) return d.Trim();
        }
        catch { }
        var t = Win32.WindowText(h);
        return t.Length is > 0 and <= 28 ? t : Path.GetFileNameWithoutExtension(exe);
    }

    int _tick;
    public void Tick()
    {
        if (++_tick % 2 == 0 || _dirty) Refresh(); // pencere listesi 2 sn'de bir
        if (Store.Current.HideTaskbar) Taskbar.EnsureHidden();
        if (_appbar && (int)_baseBottomPx != BaseBottom()) RegisterAppBar();
    }

    // ---------------- otomatik gizleme / tam ekran ----------------
    void Show() { if (!_shown) { Win32.ShowWindow(Hwnd, Win32.SW_SHOWNOACTIVATE); _shown = true; } _dirty = true; Animating = true; }

    /// <summary>Adanın 100 ms'lik yakınlık zamanlayıcısından çağrılır.</summary>
    public void Proximity()
    {
        if (!Store.Current.DockAutoHide) return;
        if (!Win32.GetCursorPos(out var p)) return;
        var mon = I.MonitorRect;
        bool atBottom = p.Y >= mon.Bottom - 2 && p.X > _winX && p.X < _winX + _winW;
        if (_autoHidden) { if (atBottom) { _autoHidden = false; _awayTicks = 0; Show(); } return; }
        bool over = p.X >= _winX && p.X <= _winX + _winW && p.Y >= _winY + (_winH - (PlateH + Bottom + 6) * I.Scale);
        if (over || atBottom) _awayTicks = 0;
        else if (++_awayTicks >= 10) { _autoHidden = true; _dirty = true; Animating = true; }
    }

    bool Concealed => _autoHidden || I.FullscreenHidden;

    // ---------------- mesajlar ----------------
    public IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l)
    {
        const uint WM_SETCURSOR = 0x20;
        switch (m)
        {
            case Win32.WM_MOUSEMOVE:
                U.Inside = true;
                if (!_tracking)
                {
                    var t = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = Win32.TME_LEAVE, hwndTrack = h };
                    Win32.TrackMouseEvent(ref t); _tracking = true;
                }
                _dirty = true; return 0;
            case Win32.WM_MOUSELEAVE:
                _tracking = false; if (!U.Down) U.Inside = false; _dirty = true; return 0;
            case Win32.WM_LBUTTONDOWN:
                U.Down = true; U.Pressed = true; _dirty = true; return 0;
            case Win32.WM_LBUTTONUP:
                U.Down = false; U.Released = true; _dirty = true; return 0;
            case Win32.WM_RBUTTONUP:
                U.RightReleased = true; _dirty = true; return 0;
            case Win32.WM_MOUSEACTIVATE:
                return Win32.MA_NOACTIVATE;
            case WM_SETCURSOR:
                if (U.WantHand && Win32.LoWord(l) == 1) { Win32.SetCursor(Win32.LoadCursorW(IntPtr.Zero, 32649)); return 1; }
                break;
            case WM_APPBAR:
                if ((int)w == 1) RegisterAppBar(); // ABN_POSCHANGED
                return 0;
        }
        return Win32.DefWindowProcW(h, m, w, l);
    }

    // ---------------- çizim ----------------
    void Frame()
    {
        double now = _sw.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - _last, 1 / 240.0, 1 / 24.0);
        if (!Animating) dt = 1 / 60f;
        _last = now;
        U.Dt = dt; U.Time += dt; U.NeedsFrame = false; U.WantHand = false;
        float s = I.Scale;
        if (_snapMouse is float smx) { U.Inside = true; U.Mouse = new Vector2(_winW / s / 2 + smx, _winH / s - 30); }
        else if (I.SnapMode) U.Inside = false;
        else if (Win32.GetCursorPos(out var cp)) U.Mouse = new Vector2((cp.X - _winX) / s, (cp.Y - _winY) / s);

        float b = B, mag = Mag;
        float H = b * mag + Pad * 2 + Bottom + LabelSpace;
        _hideY.Target = Concealed ? PlateH + Bottom + 6 : 0;
        _hideY.Step(dt);

        // ---- yerleşim (büyütme) ----
        float plateTop = H - Bottom - PlateH;
        bool hovering = U.Inside && U.Mouse.Y >= plateTop - b * (mag - 1) - 4 && U.Mouse.Y <= H;
        int n = _items.Count;
        Span<float> size = stackalloc float[n];
        // önce taban yerleşimi: büyütme hedefi tabandaki ikon merkezine göre hesaplanır (macOS gibi)
        float baseTotal = Pad * 2;
        for (int i = 0; i < n; i++) baseTotal += (_items[i].Separator ? 1 : b) + (i > 0 ? Gap : 0);
        float total = Pad * 2;
        float bx = -baseTotal / 2 + Pad;
        for (int i = 0; i < n; i++)
        {
            var it = _items[i];
            float w0 = it.Separator ? 1 : b;
            float center = bx + w0 / 2;
            float target = w0;
            if (!it.Separator && hovering && mag > 1)
            {
                float d = (U.Mouse.X - _winW / s / 2) - center;
                target = b * (1 + (mag - 1) * MathF.Exp(-(d * d) / (b * b * 2.6f)));
            }
            size[i] = it.Separator ? 1 : U.Anim(HashCode.Combine("dock.sz", it.Key), target, 22);
            total += size[i] + (i > 0 ? Gap : 0);
            bx += w0 + Gap;
        }

        float W = MathF.Max(total, baseTotal) + Margin * 2 + 120;
        int pw = (int)MathF.Ceiling(W * s), ph = (int)MathF.Ceiling(H * s);
        var mon = I.MonitorRect;
        int bottom = BaseBottom();
        _baseBottomPx = bottom;
        _winX = (mon.Left + mon.Right - pw) / 2;
        _winY = bottom - ph + (int)MathF.Round(_hideY.Value * s);
        _winW = pw; _winH = ph;

        G.Begin(pw, ph);
        float cx = W / 2;
        var plate = new Rect(cx - total / 2, plateTop, total, PlateH);
        G.Shadow(plate, 20, 14, 0.45f, 6);
        G.FillRound(plate, 20, Theme.Rgb(0x1B1E27, 0.88f));
        G.VGradientRound(plate, 20, Theme.Rgb(0xFFFFFF, 0.10f), Theme.Rgb(0xFFFFFF, 0.02f));
        G.StrokeRound(new Rect(plate.X + 0.5f, plate.Y + 0.5f, plate.Width - 1, plate.Height - 1), 20, Theme.Rgb(0xFFFFFF, 0.14f), 1);

        float x = plate.X + Pad;
        string? label = null; float labelX = 0, labelY = 0;
        var fg = Win32.GetForegroundWindow();
        for (int i = 0; i < n; i++)
        {
            var it = _items[i];
            float sz = size[i];
            if (it.Separator)
            {
                G.Line(new Vector2(x, plate.Y + 12), new Vector2(x, plate.Bottom - 12), Theme.Rgb(0xFFFFFF, 0.18f), 1);
                x += sz + Gap;
                continue;
            }
            float bounce = 0;
            if (it.BounceStart >= 0)
            {
                float t = U.Time - it.BounceStart;
                if (t > 1.3f) it.BounceStart = -1;
                else { bounce = MathF.Abs(MathF.Sin(t * MathF.PI * 2.3f)) * b * 0.45f * (1 - t / 1.3f); U.NeedsFrame = true; }
            }
            var r = new Rect(x, plate.Bottom - Pad - sz - bounce, sz, sz);
            int id = HashCode.Combine("dock", it.Key);
            bool hit = U.Hit(new Rect(x - Gap / 2, plate.Y - (sz - b), sz + Gap, plate.Bottom - plate.Y + (sz - b)));
            if (hit) { U.WantHand = true; label = it.Name; labelX = r.CenterX; labelY = r.Y - 12; }
            float press = U.Anim(id + 1, U.ActiveId == id && U.Down && hit ? 1 : 0, 26);
            if (hit && U.Pressed) U.ActiveId = id;
            var rr = new Rect(r.CenterX - r.Width * (1 - 0.1f * press) / 2, r.CenterY - r.Height * (1 - 0.1f * press) / 2, r.Width * (1 - 0.1f * press), r.Height * (1 - 0.1f * press));

            if (it.Launcher) DrawLauncher(rr);
            else
            {
                var bmp = I.Apps.Icon(it.Path);
                if (bmp != null) G.Bitmap(bmp, rr);
                else if (it.Path.StartsWith("ms-settings", StringComparison.OrdinalIgnoreCase))
                {
                    G.VGradientRound(rr, rr.Width * 0.22f, Theme.Rgb(0x8E8E93), Theme.Rgb(0x48484A));
                    G.Icon(Glyph.Settings, new Vector2(rr.CenterX, rr.CenterY), rr.Width * 0.3f, Theme.Text);
                }
                else
                {
                    G.VGradientRound(rr, rr.Width * 0.22f, Theme.Rgb(0x3A3F52), Theme.Rgb(0x23262F));
                    G.Text(it.Name.Length > 0 ? it.Name[..1].ToUpper() : "?", rr, rr.Width * 0.42f, Theme.Text, FontWeight.Bold, TextAlignment.Center);
                }
            }
            // çalışma noktası (öndeki uygulamanınki daha parlak)
            if (it.Windows.Count > 0)
            {
                bool front = it.Windows.Contains(fg);
                G.FillCircle(new Vector2(r.CenterX, plate.Bottom - 4), 2.2f, front ? Theme.Text : Theme.Rgb(0xFFFFFF, 0.55f));
            }

            if (hit && U.Released && U.ActiveId == id) Activate(it);
            if (hit && U.RightReleased) { var item = it; Dispatcher.Post(() => Menu(item)); }
            x += sz + Gap;
        }

        if (label != null)
        {
            float lw = G.Measure(label, 12, FontWeight.SemiBold) + 22;
            var lr = new Rect(labelX - lw / 2, labelY - 26, lw, 24);
            G.FillRound(lr, 8, Theme.Rgb(0x1B1E27, 0.95f));
            G.StrokeRound(lr, 8, Theme.Rgb(0xFFFFFF, 0.14f), 1);
            G.Text(label, lr, 12, Theme.Text, FontWeight.SemiBold, TextAlignment.Center);
        }
        G.End();
        G.Present(Hwnd, _winX, _winY, pw, ph);

        Animating = U.NeedsFrame || !_hideY.Settled;
        _dirty = false;
        U.EndFrame();
        U.GcAnims();
        if (Concealed && _hideY.Settled && _shown && I.FullscreenHidden) { Win32.ShowWindow(Hwnd, 0); _shown = false; }
        if (!I.FullscreenHidden && !_shown && !I.SnapMode) Show();
    }

    void DrawLauncher(Rect r)
    {
        G.VGradientRound(r, r.Width * 0.24f, Theme.Rgb(0x5B6CFF), Theme.Rgb(0x8E54E9));
        float c = r.Width / 7f;
        for (int yy = 0; yy < 3; yy++)
        for (int xx = 0; xx < 3; xx++)
            G.FillRound(new Rect(r.X + c * (1 + xx * 2) - c * 0.5f + c * 0.5f, r.Y + c * (1 + yy * 2) - c * 0.5f + c * 0.5f, c, c), c * 0.3f, Theme.Rgb(0xFFFFFF, 0.92f));
    }

    void Activate(Item it)
    {
        if (it.Launcher) { Dispatcher.Post(() => I.ToggleSpotlight()); return; }
        if (it.Windows.Count > 0)
        {
            var w = it.Windows[0];
            if (w == Win32.GetForegroundWindow() && !Win32.IsIconic(w)) { Win32.ShowWindow(w, 6); return; } // küçült
            if (Win32.IsIconic(w)) Win32.ShowWindow(w, 9); // geri yükle
            Win32.SetForegroundWindow(w);
            return;
        }
        it.BounceStart = U.Time;
        Animating = true;
        var app = new PinnedApp { Name = it.Name, Path = it.Path, Args = it.Args };
        Dispatcher.Post(() => AppsService.Launch(app));
    }

    void Menu(Item it)
    {
        if (it.Launcher || it.Separator) return;
        var menu = Win32.CreatePopupMenu();
        Win32.AppendMenuW(menu, Win32.MF_STRING, 1, it.Pinned ? "Dock'tan kaldır" : "Dock'ta tut");
        if (it.Windows.Count > 0) Win32.AppendMenuW(menu, Win32.MF_STRING, 2, it.Windows.Count > 1 ? $"Tüm pencereleri kapat ({it.Windows.Count})" : "Kapat");
        Win32.AppendMenuW(menu, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(menu, Win32.MF_STRING, 3, "Dock ayarları…");
        Win32.GetCursorPos(out var p);
        Win32.SetForegroundWindow(Hwnd);
        int cmd = Win32.TrackPopupMenu(menu, Win32.TPM_RETURNCMD | Win32.TPM_RIGHTBUTTON, p.X, p.Y, 0, Hwnd, IntPtr.Zero);
        Win32.DestroyMenu(menu);
        var apps = Store.Current.Apps;
        switch (cmd)
        {
            case 1:
                if (it.Pinned) apps.RemoveAll(a => a.Path.Equals(it.Path, StringComparison.OrdinalIgnoreCase));
                else apps.Add(new PinnedApp { Name = it.Name, Path = it.Path });
                Store.Save(); Refresh(); break;
            case 2:
                foreach (var w in it.Windows) Win32.PostMessageW(w, 0x10, 0, 0); // WM_CLOSE
                break;
            case 3: I.OpenSettings(SettingsWindow.DockPage); break;
        }
    }
}

/// <summary>Windows görev çubuğunu gizleme/geri getirme (Ada kapanınca ya da ayar kapatılınca geri gelir).</summary>
public static class Taskbar
{
    static bool _hidden;
    static IntPtr Tray => Win32.FindWindowW("Shell_TrayWnd", null);

    public static void SetHidden(bool hide)
    {
        var tray = Tray;
        if (tray == IntPtr.Zero || hide == _hidden && (!hide || !Win32.IsWindowVisible(tray))) { _hidden = hide; return; }
        var abd = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = tray };
        if (hide)
        {
            // otomatik gizle: çalışma alanı tam ekrana genişler; sonra pencereyi tamamen sakla
            abd.lParam = 1; Win32.SHAppBarMessage(10, ref abd); // ABM_SETSTATE, ABS_AUTOHIDE
            Win32.ShowWindow(tray, 0);
            var second = Win32.FindWindowW("Shell_SecondaryTrayWnd", null);
            if (second != IntPtr.Zero) Win32.ShowWindow(second, 0);
        }
        else
        {
            abd.lParam = 2; Win32.SHAppBarMessage(10, ref abd); // ABS_ALWAYSONTOP (normal)
            Win32.ShowWindow(tray, Win32.SW_SHOWNOACTIVATE);
            var second = Win32.FindWindowW("Shell_SecondaryTrayWnd", null);
            if (second != IntPtr.Zero) Win32.ShowWindow(second, Win32.SW_SHOWNOACTIVATE);
        }
        _hidden = hide;
    }

    /// <summary>Explorer bazen görev çubuğunu geri açar (Win tuşu vb.) → tekrar sakla.</summary>
    public static void EnsureHidden()
    {
        var tray = Tray;
        if (tray != IntPtr.Zero && Win32.IsWindowVisible(tray)) Win32.ShowWindow(tray, 0);
    }
}
