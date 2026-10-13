using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Ada.Core;
using Ada.Data;
using Ada.Native;
using Ada.Services;
using Vortice.Direct2D1;
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
    const float Margin = 30, Bottom = 8, Pad = 10, LabelSpace = 44;
    float Gap => Store.Current.DockSpacing;
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

    float B => Store.Current.DockIconSize;
    float Mag => Store.Current.DockMagnify ? Store.Current.DockMagnification : 1f;

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
    public void SnapRender(string path, float? mouseX, int frames = 90)
    {
        _snapMouse = mouseX;
        for (int i = 0; i < frames; i++) Frame();
        G.SavePng(path, _winW, _winH);
        File.WriteAllText(path + ".txt", $"hover={_hoverItem?.Name} preview={_previewItem?.Name} menu={_menuItem?.Name} items={string.Join(",", _items.Select(x => x.Name + ":" + x.Windows.Count))} mouse={U.Mouse} inside={U.Inside} win={_winW}x{_winH} time={U.Time}");
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
        ReloadBackdrop();
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
        // iki ikon kademesini önceden hazırla (normal + büyütülmüş) → üzerine gelince takılma olmaz
        foreach (var it in items)
            if (!it.Launcher && !it.Separator) { I.Apps.Icon(it.Path, B); I.Apps.Icon(it.Path, B * Mag); }
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
        if (G.InDraw) { _dirty = true; return; }
        double now = _sw.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - _last, 1 / 240.0, 1 / 24.0);
        if (!Animating) dt = 1 / 60f;
        _last = now;
        U.Dt = dt; U.Time += dt; U.NeedsFrame = false; U.WantHand = false;
        float s = I.Scale;
        if (_snapMouse is float smx) { U.Inside = true; U.Mouse = new Vector2(_winW / s / 2 + smx, _winH / s - 30); }
        else if (I.SnapMode) U.Inside = false;
        else if (Win32.GetCursorPos(out var cp)) U.Mouse = new Vector2((cp.X - _winX) / s, (cp.Y - _winY) / s);

        var st = Store.Current;
        float b = B, mag = Mag, gap = Gap;
        if (_backdropDirty) LoadBackdrop();

        // üstte açılır panel (önizleme / menü) varsa pencere yukarı doğru uzar
        bool pop = _previewItem != null || _menuItem != null;
        float H = b * mag + Pad * 2 + Bottom + LabelSpace + (pop ? PopSpace : 0);
        if (_snapMouse is float smy) U.Mouse = new Vector2(_winW / s / 2 + smy, H - 30); // test faresi bu karenin yüksekliğine göre
        _hideY.Target = Concealed ? PlateH + Bottom + 6 : 0;
        _hideY.Step(dt);

        // ---- yerleşim (macOS büyütmesi: hedef, tabandaki ikon merkezine göre) ----
        float plateTop = H - Bottom - PlateH;
        bool overPop = pop && _popRect.Contains(U.Mouse) && U.Inside;
        bool hovering = U.Inside && !overPop && U.Mouse.Y >= plateTop - b * (mag - 1) - 4 && U.Mouse.Y <= H;
        int n = _items.Count;
        Span<float> size = stackalloc float[n];
        float baseTotal = Pad * 2;
        for (int i = 0; i < n; i++) baseTotal += (_items[i].Separator ? 1 : b) + (i > 0 ? gap : 0);
        float total = Pad * 2;
        float bx = -baseTotal / 2 + Pad;
        for (int i = 0; i < n; i++)
        {
            var it = _items[i];
            float w0 = it.Separator ? 1 : b;
            float center = bx + w0 / 2;
            float target = w0;
            if (!it.Separator && hovering && mag > 1 && _menuItem == null)
            {
                float d = (U.Mouse.X - _winW / s / 2) - center;
                target = b * (1 + (mag - 1) * MathF.Exp(-(d * d) / (b * b * 2.6f)));
            }
            size[i] = it.Separator ? 1 : U.Anim(HashCode.Combine("dock.sz", it.Key), target, 22);
            total += size[i] + (i > 0 ? gap : 0);
            bx += w0 + gap;
        }

        float W = MathF.Max(MathF.Max(total, baseTotal) + Margin * 2 + 120, pop ? PopMinW : 0);
        int pw = (int)MathF.Ceiling(W * s), ph = (int)MathF.Ceiling(H * s);
        var mon = I.MonitorRect;
        int bottom = BaseBottom();
        _baseBottomPx = bottom;
        _winX = (mon.Left + mon.Right - pw) / 2;
        _winY = bottom - ph + (int)MathF.Round(_hideY.Value * s);
        _winW = pw; _winH = ph;

        G.Begin(pw, ph);
        try {
        float cx = W / 2;
        var plate = new Rect(cx - total / 2, plateTop, total, PlateH);
        float rad = MathF.Min(st.DockRadius, PlateH / 2);
        G.Shadow(plate, rad, 16, 0.42f, 7);
        Glass(plate, rad, s);

        float x = plate.X + Pad;
        string? label = null; float labelX = 0, labelY = 0;
        Item? hoverIcon = null; float hoverX = 0, hoverTop = 0;
        var fg = Win32.GetForegroundWindow();
        for (int i = 0; i < n; i++)
        {
            var it = _items[i];
            float sz = size[i];
            if (it.Separator)
            {
                G.Line(new Vector2(x, plate.Y + 12), new Vector2(x, plate.Bottom - 12), Theme.Rgb(0xFFFFFF, 0.20f), 1);
                x += sz + gap;
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
            bool hit = !overPop && U.Hit(new Rect(x - gap / 2, plate.Y - (sz - b), sz + gap, plate.Bottom - plate.Y + (sz - b)));
            if (hit) { U.WantHand = true; label = it.Name; labelX = r.CenterX; labelY = r.Y - 12; hoverIcon = it; hoverX = r.CenterX; hoverTop = r.Y; }
            float press = U.Anim(id + 1, U.ActiveId == id && U.Down && hit ? 1 : 0, 26);
            if (hit && U.Pressed) U.ActiveId = id;
            float k = 1 - 0.1f * press;
            var rr = new Rect(r.CenterX - r.Width * k / 2, r.CenterY - r.Height * k / 2, r.Width * k, r.Height * k);

            // ikon altı yumuşak gölge (zıplarken uzaklaşıp hafifler)
            G.SoftShadow(new Vector2(r.CenterX, plate.Bottom - Pad - sz * 0.02f), sz * 0.44f, sz * 0.11f, 0.38f * (1 - MathF.Min(1, bounce / b)));
            if (it.Launcher) DrawLauncher(rr);
            else
            {
                var bmp = I.Apps.Icon(it.Path, sz);
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
            if (st.DockIndicators && it.Windows.Count > 0)
            {
                bool front = it.Windows.Contains(fg);
                G.FillCircle(new Vector2(r.CenterX, plate.Bottom - 4), 2.2f, front ? Theme.Text : Theme.Rgb(0xFFFFFF, 0.5f));
            }

            if (hit && U.Released && U.ActiveId == id) { _menuItem = null; _previewItem = null; Activate(it); }
            if (hit && U.RightReleased) { _menuItem = it; _menuX = r.CenterX; _previewItem = null; Animating = true; }
            x += sz + gap;
        }

        // ---- DockDoor tarzı önizleme: üzerinde durulunca açılır, panele geçince açık kalır ----
        if (hoverIcon != null && _menuItem == null)
        {
            if (hoverIcon != _hoverItem) { _hoverItem = hoverIcon; _hoverSince = U.Time; }
            bool wants = st.DockPreviews && hoverIcon.Windows.Count > 0;
            if (wants && (_previewItem != null || U.Time - _hoverSince > 0.45f)) { _previewItem = hoverIcon; _previewX = hoverX; }
            else if (!wants && _previewItem != null && _previewItem != hoverIcon) _previewItem = null;
            if (wants && _previewItem == null) U.NeedsFrame = true; // bekleme süresini say
            _leaveAt = -1;
        }
        else if (_previewItem != null && !overPop)
        {
            _hoverItem = null;
            if (_leaveAt < 0) _leaveAt = U.Time;
            if (U.Time - _leaveAt > 0.35f) _previewItem = null;
            U.NeedsFrame = true;
        }
        else { _hoverItem = null; _leaveAt = -1; }

        float popBottom = plateTop - b * (mag - 1) - 14;
        _popRect = default;
        if (_menuItem != null) DrawMenu(_menuItem, popBottom, W);
        else if (_previewItem != null) DrawPreview(_previewItem, popBottom, W);
        else if (label != null && st.DockLabels) Label(label, labelX, labelY);

        // menü dışına tıklayınca kapan
        if (_menuItem != null && U.Pressed && !overPop && hoverIcon != _menuItem) _menuItem = null;

        }
        finally { G.End(); }
        G.Present(Hwnd, _winX, _winY, pw, ph);

        Animating = U.NeedsFrame || !_hideY.Settled;
        _dirty = false;
        U.EndFrame();
        U.GcAnims();
        if (Concealed && _hideY.Settled && _shown && I.FullscreenHidden) { Win32.ShowWindow(Hwnd, 0); _shown = false; }
        if (!I.FullscreenHidden && !_shown && !I.SnapMode) Show();
    }

    // ================= görünüm =================
    const float PopSpace = 210, PopMinW = 980;
    ID2D1Bitmap? _backdrop;
    bool _backdropDirty = true;
    Rect _popRect;
    Item? _previewItem, _hoverItem, _menuItem;
    float _previewX, _menuX, _hoverSince, _leaveAt = -1;
    readonly Dictionary<IntPtr, (ID2D1Bitmap? bmp, float t)> _thumbs = new();

    public void ReloadBackdrop() { _backdropDirty = true; _dirty = true; }

    void LoadBackdrop()
    {
        _backdropDirty = false;
        _backdrop?.Dispose(); _backdrop = null;
        if (Store.Current.DockStyle != 0) return;
        var file = WallpaperService.Current();
        var mon = I.MonitorRect;
        if (File.Exists(file)) _backdrop = G.BlurredWallpaper(file, mon.Width, mon.Height);
    }

    /// <summary>Plaka/panel zemini: buzlu cam (duvar kağıdının bulanık kopyası hizalı), koyu ya da şeffaf.</summary>
    void Glass(Rect r, float rad, float s)
    {
        var st = Store.Current;
        float op = st.DockOpacity;
        if (st.DockStyle == 0 && _backdrop != null)
        {
            var mon = I.MonitorRect;
            var bs = _backdrop.Size;
            var m = Matrix3x2.CreateScale(mon.Width / s / bs.Width, mon.Height / s / bs.Height)
                    * Matrix3x2.CreateTranslation((mon.Left - _winX) / s, (mon.Top - _winY) / s);
            G.FillRoundBitmap(r, rad, _backdrop, m);
            G.FillRound(r, rad, Theme.Rgb(0x14161D, 0.18f + 0.55f * op));
        }
        else if (st.DockStyle == 2) G.FillRound(r, rad, Theme.Rgb(0xFFFFFF, 0.04f + 0.10f * op));
        else G.FillRound(r, rad, Theme.Rgb(0x16181F, 0.45f + 0.55f * op));
        G.VGradientRound(r, rad, Theme.Rgb(0xFFFFFF, 0.13f), Theme.Rgb(0xFFFFFF, 0.02f));
        G.StrokeRound(new Rect(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), rad, Theme.Rgb(0xFFFFFF, 0.17f), 1);
    }

    void Label(string text, float x, float iconTop)
    {
        float lw = G.Measure(text, 12, FontWeight.SemiBold) + 24;
        var lr = new Rect(x - lw / 2, iconTop - 30, lw, 26);
        G.SoftShadow(new Vector2(lr.CenterX, lr.Bottom), lw * 0.5f, 8, 0.3f);
        Glass(lr, 9, I.Scale);
        G.Text(text, lr, 12, Theme.Text, FontWeight.SemiBold, TextAlignment.Center);
    }

    /// <summary>DockDoor: uygulamanın pencereleri canlı görüntüleriyle; tıkla → o pencereye geç, × → kapat.</summary>
    void DrawPreview(Item it, float bottom, float W)
    {
        const float cardW = 216, thumbH = 122, pad = 10, titleH = 30;
        int n = Math.Min(it.Windows.Count, 4);
        if (n == 0) { _previewItem = null; return; }
        float pwid = n * cardW + (n + 1) * pad, phgt = thumbH + titleH + pad * 2 + 4;
        float px = Math.Clamp(_previewX - pwid / 2, 8, W - pwid - 8);
        var panel = new Rect(px, bottom - phgt, pwid, phgt);
        _popRect = new Rect(panel.X - 6, panel.Y - 6, panel.Width + 12, panel.Height + 40);
        G.Shadow(panel, 16, 14, 0.45f, 6);
        Glass(panel, 16, I.Scale);
        int budget = 1; // kare başına en fazla bir pencere yakala (takılma olmasın)
        IntPtr? close = null, focus = null;
        for (int i = 0; i < n; i++)
        {
            var h = it.Windows[i];
            var card = new Rect(panel.X + pad + i * (cardW + pad), panel.Y + pad, cardW, thumbH + titleH);
            int id = HashCode.Combine("dock.pv", h);
            bool hit = U.Hit(card);
            float hv = U.Anim(id, hit ? 1 : 0);
            if (hv > 0.01f) G.FillRound(new Rect(card.X - 4, card.Y - 4, card.Width + 8, card.Height + 8), 12, Theme.Rgb(0xFFFFFF, 0.08f * hv));
            var tr = new Rect(card.X, card.Y, cardW, thumbH);
            if (!_thumbs.TryGetValue(h, out var th) || U.Time - th.t > 3f)
            {
                if (budget-- > 0)
                {
                    th.bmp?.Dispose();
                    th = (G.CaptureWindow(h, (int)(cardW * I.Scale)), U.Time);
                    _thumbs[h] = th;
                }
                else U.NeedsFrame = true;
            }
            if (th.bmp != null) G.BitmapCover(th.bmp, tr, 9);
            else
            {
                G.FillRound(tr, 9, Theme.Rgb(0x000000, 0.35f));
                var ib = I.Apps.Icon(it.Path, 48);
                if (ib != null) G.Bitmap(ib, new Rect(tr.CenterX - 22, tr.CenterY - 30, 44, 44));
                G.Text(Win32.IsIconic(h) ? "Simge durumunda" : "Önizleme yok", new Rect(tr.X, tr.CenterY + 18, tr.Width, 16), 10.5f, Theme.Text3, FontWeight.SemiBold, TextAlignment.Center);
            }
            G.StrokeRound(tr, 9, Theme.Mix(Theme.Rgb(0xFFFFFF, 0.12f), Theme.Accent, hv), 1 + hv);
            var title = Win32.WindowText(h);
            G.Text(title.Length > 0 ? title : it.Name, new Rect(card.X + 2, tr.Bottom + 6, cardW - 4, 18), 11.5f, Theme.Text, FontWeight.SemiBold);
            // kapat
            if (hv > 0.05f)
            {
                var cc = new Vector2(tr.Right - 14, tr.Y + 14);
                G.Alpha *= hv;
                if (U.IconButton(HashCode.Combine("dock.pvx", h), cc, 10, "", 8, Theme.Text, true, Theme.Rgb(0x000000, 0.6f))) close = h;
                G.Alpha /= hv;
            }
            if (hit && U.Released && close == null && U.ActiveId == 0) focus = h;
            if (hit) U.WantHand = true;
        }
        if (close is IntPtr ch) { Win32.PostMessageW(ch, 0x10, 0, 0); it.Windows.Remove(ch); _thumbs.Remove(ch); }
        if (focus is IntPtr fh)
        {
            if (Win32.IsIconic(fh)) Win32.ShowWindow(fh, 9);
            Win32.SetForegroundWindow(fh);
            _previewItem = null;
        }
        if (_thumbs.Count > 24) { foreach (var v in _thumbs.Values) v.bmp?.Dispose(); _thumbs.Clear(); }
    }

    /// <summary>Kendi sağ tık menümüz (Windows menüsü yerine, Dock'un camıyla).</summary>
    void DrawMenu(Item it, float bottom, float W)
    {
        var entries = new List<(string text, string glyph, Action run, bool danger)>();
        if (!it.Launcher)
        {
            entries.Add((it.Pinned ? "Dock'tan kaldır" : "Dock'ta tut", it.Pinned ? "" : "", () => TogglePin(it), false));
            entries.Add(("Yeni pencere", "", () =>
            {
                it.BounceStart = U.Time; Animating = true;
                var app = new PinnedApp { Name = it.Name, Path = it.Path, Args = it.Args };
                Dispatcher.Post(() => AppsService.Launch(app));
            }, false));
            if (it.Windows.Count > 0)
            {
                entries.Add(("Tümünü göster", "", () => { foreach (var w in it.Windows) if (Win32.IsIconic(w)) Win32.ShowWindow(w, 9); if (it.Windows.Count > 0) Win32.SetForegroundWindow(it.Windows[0]); }, false));
                entries.Add((it.Windows.Count > 1 ? $"Tümünü kapat ({it.Windows.Count})" : "Kapat", "",
                    () => { foreach (var w in it.Windows) Win32.PostMessageW(w, 0x10, 0, 0); }, true));
            }
        }
        else entries.Add(("Spotlight'ı aç", "", () => Dispatcher.Post(() => I.ToggleSpotlight()), false));
        entries.Add(("Dock ayarları…", Glyph.Settings, () => I.OpenSettings(SettingsWindow.DockPage), false));

        const float rowH = 32, mw = 220;
        float mh = entries.Count * rowH + 12;
        float mx = Math.Clamp(_menuX - mw / 2, 8, W - mw - 8);
        var panel = new Rect(mx, bottom - mh, mw, mh);
        _popRect = new Rect(panel.X - 6, panel.Y - 6, panel.Width + 12, panel.Height + 12);
        G.Shadow(panel, 14, 14, 0.45f, 6);
        Glass(panel, 14, I.Scale);
        G.Text(it.Name, new Rect(panel.X, panel.Y - 22, mw, 18), 11, Theme.Text2, FontWeight.SemiBold, TextAlignment.Center);
        for (int i = 0; i < entries.Count; i++)
        {
            var (text, glyph, run, danger) = entries[i];
            var r = new Rect(panel.X + 6, panel.Y + 6 + i * rowH, mw - 12, rowH);
            int id = HashCode.Combine("dock.menu", i, text);
            bool hit = U.Hit(r);
            float hv = U.Anim(id, hit ? 1 : 0, 22);
            if (hv > 0.01f) G.FillRound(r, 8, (danger ? Theme.Red : Theme.Accent).WithA(0.85f * hv));
            var fg = Theme.Mix(danger ? Theme.Red : Theme.Text, hv > 0.5f ? Theme.Rgb(0x0B0D12) : Theme.Text, hv);
            G.Icon(glyph, new Vector2(r.X + 16, r.CenterY), 11, fg);
            G.Text(text, new Rect(r.X + 34, r.Y, r.Width - 40, r.Height), 12.5f, fg, FontWeight.SemiBold);
            if (hit) U.WantHand = true;
            if (U.Click(id + 1, r)) { _menuItem = null; Dispatcher.Post(run); }
        }
    }

    void TogglePin(Item it)
    {
        var apps = Store.Current.Apps;
        if (it.Pinned) apps.RemoveAll(a => a.Path.Equals(it.Path, StringComparison.OrdinalIgnoreCase));
        else apps.Add(new PinnedApp { Name = it.Name, Path = it.Path });
        Store.Save(); Refresh();
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
