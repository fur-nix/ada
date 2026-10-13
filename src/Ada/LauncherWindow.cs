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
/// Başlat menüsü tarzı panel (Windows tuşuyla açılan menü gibi — tam ekran değil):
/// Dock'un üstünde ortada, buzlu cam, üstte arama, altında uygulama ızgarası.
/// Yaz → süzülür, ok tuşları + Enter, Esc / dışına tık / odak kaybı → kapanır.
/// </summary>
public sealed unsafe class LauncherWindow : ISurface
{
    const float PanelW = 680, PanelH = 700, M = 26, R = 20;
    static bool _registered;
    readonly Island I;
    Gfx G => I.G;
    readonly Ui U;
    public IntPtr Hwnd { get; private set; }
    public bool Animating { get; private set; }
    bool _dirty = true, _open, _shown;
    readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
    double _last;
    Smooth _vis = new(0, 16);
    Spring _scroll = new(0, 260, 32);
    readonly TextBox _box = new() { Placeholder = "Uygulama ara" };
    int _pw, _ph, _sel, _winX, _winY;
    RECT _mon;
    float _scale = 1;
    ID2D1Bitmap? _backdrop;
    List<AppIndex.Entry> _items = new();

    public LauncherWindow(Island island)
    {
        I = island;
        U = new Ui(island.G);
        var inst = Win32.GetModuleHandleW(IntPtr.Zero);
        if (!_registered)
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Island.ProcPtr, hInstance = inst,
                hCursor = Win32.LoadCursorW(IntPtr.Zero, 32512), lpszClassName = Marshal.StringToHGlobalUni("AdaLauncher")
            };
            Win32.RegisterClassExW(ref wc);
            _registered = true;
        }
        Hwnd = Win32.CreateWindowExW(Win32.WS_EX_LAYERED | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST, "AdaLauncher", "Ada Başlatıcı",
            Win32.WS_POPUP, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
    }

    public bool IsOpen => _open;

    /// <summary>--snap: paneli (isteğe bağlı aramayla) PNG'ye çizer.</summary>
    public void SnapRender(string path, string query)
    {
        _mon = I.MonitorRect; _scale = I.Scale;
        Place();
        I.AppIndex.EnsureLoaded();
        _box.Text = query; _box.Caret = query.Length;
        Rebuild();
        _open = true; _shown = true; _vis.Value = _vis.Target = 1;
        for (int i = 0; i < 90; i++) Frame();
        G.SavePng(path, _pw, _ph);
        _open = false; _shown = false;
    }

    public void Invalidate() => _dirty = true;
    public void FrameIfNeeded() { if (_shown && (_dirty || Animating)) Frame(); }
    public void Toggle() { if (_open) Hide(); else Show(); }

    void Place()
    {
        _pw = (int)MathF.Ceiling((PanelW + M * 2) * _scale);
        _ph = (int)MathF.Ceiling((PanelH + M * 2) * _scale);
        int cx = (_mon.Left + _mon.Right) / 2;
        _winX = cx - _pw / 2;
        // Dock'un üstünde: ekranın altından ~96px pay bırak
        _winY = _mon.Bottom - _ph - (int)(70 * _scale);
        if (_winY < _mon.Top + 8) _winY = _mon.Top + 8;
    }

    public void Show()
    {
        var mon = Win32.MonitorFromWindow(I.Hwnd, 2);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        Win32.GetMonitorInfoW(mon, ref mi);
        _mon = mi.rcMonitor;
        Win32.GetDpiForMonitor(mon, 0, out var dpi, out _);
        _scale = dpi == 0 ? 1 : dpi / 96f;
        G.SetScale(_scale);
        Place();
        Win32.SetWindowPos(Hwnd, Win32.HWND_TOPMOST, _winX, _winY, _pw, _ph, Win32.SWP_NOACTIVATE);

        I.AppIndex.EnsureLoaded();
        _box.Clear(); _sel = 0; _scroll.Snap(0);
        Rebuild();
        LoadBackdrop();
        _open = true; _vis.Value = 0;
        if (!_shown) { Win32.ShowWindow(Hwnd, 5); _shown = true; }
        Win32.SetForegroundWindow(Hwnd);
        _dirty = true; Animating = true;
    }

    public void Hide() { if (!_open) return; _open = false; _dirty = true; Animating = true; }
    public void Close() => Win32.DestroyWindow(Hwnd);

    void LoadBackdrop()
    {
        _backdrop?.Dispose(); _backdrop = null;
        var file = WallpaperService.Current();
        if (File.Exists(file)) _backdrop = G.BlurredWallpaper(file, _mon.Width, _mon.Height, 10);
    }

    void Rebuild()
    {
        var q = _box.Text.Trim();
        if (q.Length == 0)
        {
            var counts = Store.Current.LaunchCounts;
            var freq = I.AppIndex.All.Where(e => counts.ContainsKey(e.Path)).OrderByDescending(e => counts[e.Path]).Take(10).ToList();
            var rest = I.AppIndex.All.Where(e => !freq.Contains(e)).ToList();
            _items = freq.Concat(rest).ToList();
        }
        else _items = AppIndex.Search(I.AppIndex.All, q, 200);
        _sel = Math.Clamp(_sel, 0, Math.Max(0, _items.Count - 1));
    }

    // ---------------- yerleşim (panel içi, DIP) ----------------
    const float Pad = 22, SearchH = 46, GridTop = 84;
    const int Cols = 5;
    float GridW => PanelW - Pad * 2;
    float CellW => GridW / Cols;
    const float CellH = 96, IconSize = 44;
    float ViewH => PanelH - GridTop - Pad;

    // ---------------- mesajlar ----------------
    public IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l)
    {
        const uint WM_SETCURSOR = 0x20;
        switch (m)
        {
            case Win32.WM_MOUSEMOVE: U.Mouse = new Vector2(Win32.LoWord(l) / _scale - M, Win32.HiWord(l) / _scale - M); U.Inside = true; _dirty = true; return 0;
            case Win32.WM_LBUTTONDOWN: U.Down = true; U.Pressed = true; _dirty = true; return 0;
            case Win32.WM_LBUTTONUP: U.Down = false; U.Released = true; _dirty = true; return 0;
            case Win32.WM_MOUSEWHEEL: _scroll.Target -= Win32.HiWord(w) / 120f * CellH; _dirty = true; return 0;
            case WM_SETCURSOR: if (U.WantHand && Win32.LoWord(l) == 1) { Win32.SetCursor(Win32.LoadCursorW(IntPtr.Zero, 32649)); return 1; } break;
            case Win32.WM_ACTIVATE: if (Win32.LoWord(w) == 0) Hide(); return 0;
            case Win32.WM_CHAR:
                if (_box.OnChar((char)w)) { _sel = 0; _scroll.Target = 0; Rebuild(); }
                _dirty = true; return 0;
            case Win32.WM_KEYDOWN:
            case Win32.WM_SYSKEYDOWN:
            {
                int vk = (int)w; bool ctrl = Win32.GetKeyState(0x11) < 0;
                if (vk == 0x1B) { if (_box.Text.Length > 0) { _box.Clear(); Rebuild(); } else Hide(); }
                else if (vk == 0x0D) Launch();
                else if (vk == 0x27) _sel = Math.Min(_items.Count - 1, _sel + 1);
                else if (vk == 0x25) _sel = Math.Max(0, _sel - 1);
                else if (vk == 0x28) _sel = Math.Min(_items.Count - 1, _sel + Cols);
                else if (vk == 0x26) _sel = Math.Max(0, _sel - Cols);
                else { var b = _box.Text; _box.OnKey(vk, ctrl, () => ClipboardService.ReadText(Hwnd)); if (b != _box.Text) { _sel = 0; Rebuild(); } }
                EnsureVisible();
                _dirty = true; return 0;
            }
        }
        return Win32.DefWindowProcW(h, m, w, l);
    }

    void EnsureVisible()
    {
        if (_items.Count == 0) return;
        float y = _sel / Cols * CellH;
        if (y < _scroll.Target) _scroll.Target = y;
        else if (y + CellH > _scroll.Target + ViewH) _scroll.Target = y + CellH - ViewH;
    }

    void Launch()
    {
        if (_sel >= _items.Count) return;
        var e = _items[_sel];
        var c = Store.Current.LaunchCounts;
        c[e.Path] = (c.TryGetValue(e.Path, out var k) ? k : 0) + 1;
        Store.Save();
        Hide();
        Dispatcher.Post(() => AppsService.Launch(new PinnedApp { Name = e.Name, Path = e.Path }));
    }

    // ---------------- çizim ----------------
    void Frame()
    {
        double now = _sw.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - _last, 1 / 240.0, 1 / 24.0);
        if (!Animating) dt = 1 / 60f;
        _last = now;
        U.Dt = dt; U.Time += dt; U.NeedsFrame = false; U.WantHand = false;
        _vis.Target = _open ? 1 : 0; _vis.Speed = _open ? 16 : 22; _vis.Step(dt);
        _scroll.Step(dt);

        G.Begin(_pw, _ph);
        try
        {
            float a = Ease.OutCubic(_vis.Value);
            G.Alpha = a;
            G.PushTranslate(M, M + 14 * (1 - a), 0.98f + 0.02f * a, new Vector2(PanelW / 2, PanelH)); // alttan yukarı kayarak gelir
            Draw();
            G.PopTransform();
        }
        finally { G.End(); }
        G.PresentInPlace(Hwnd, _pw, _ph);

        Animating = U.NeedsFrame || !_vis.Settled || !_scroll.Settled;
        _dirty = false;
        U.EndFrame();
        if (!_open && _vis.Value < 0.01f && _shown)
        {
            Win32.ShowWindow(Hwnd, 0); _shown = false; Animating = false;
            _backdrop?.Dispose(); _backdrop = null;
            if (I.Apps.IconCount > 300) I.Apps.ClearIconCache();
        }
    }

    void Draw()
    {
        var panel = new Rect(0, 0, PanelW, PanelH);
        G.Shadow(panel, R, 22, 0.55f, 10);
        // buzlu cam: duvar kağıdının bulanık bölgesi (ekrana hizalı) + koyu cam
        if (_backdrop != null)
        {
            var bs = _backdrop.Size;
            var mx = Matrix3x2.CreateScale(_mon.Width / _scale / bs.Width, _mon.Height / _scale / bs.Height)
                     * Matrix3x2.CreateTranslation((_mon.Left - _winX) / _scale - M, (_mon.Top - _winY) / _scale - M);
            G.FillRoundBitmap(panel, R, _backdrop, mx);
            G.FillRound(panel, R, Theme.Rgb(0x0E1118, 0.72f));
        }
        else G.FillRound(panel, R, Theme.Rgb(0x121620, 0.96f));
        G.VGradientRound(new Rect(0, 0, PanelW, 90), R, Theme.Rgb(0xFFFFFF, 0.06f), Theme.Rgb(0xFFFFFF, 0));
        G.StrokeRound(new Rect(0.5f, 0.5f, PanelW - 1, PanelH - 1), R, Theme.Rgb(0xFFFFFF, 0.12f), 1);

        // arama
        var sb = new Rect(Pad, 20, PanelW - Pad * 2, SearchH);
        G.FillRound(sb, SearchH / 2, Theme.Rgb(0xFFFFFF, 0.1f));
        G.StrokeRound(sb, SearchH / 2, Theme.Rgb(0xFFFFFF, 0.12f), 1);
        G.Icon("", new Vector2(sb.X + 24, sb.CenterY), 15, Theme.Text2);
        var tr = new Rect(sb.X + 46, sb.Y, sb.Width - 60, sb.Height);
        if (_box.Text.Length == 0) G.Text(_box.Placeholder, tr, 15, Theme.Text3, FontWeight.Normal, font: Theme.TextFont);
        float caretX = G.Measure(_box.Text, 15, FontWeight.Normal, Theme.TextFont);
        G.Text(_box.Text, tr, 15, Theme.Text, FontWeight.Normal, font: Theme.TextFont);
        float blink = ((int)(U.Time / 0.53f)) % 2 == 0 ? 1 : 0.15f;
        G.Line(new Vector2(tr.X + caretX + 2, sb.CenterY - 11), new Vector2(tr.X + caretX + 2, sb.CenterY + 11), Theme.Accent.WithA(blink), 2);
        if (_open) U.NeedsFrame = true;

        // başlık
        G.Text(_box.Text.Length == 0 ? "Tüm uygulamalar" : $"“{_box.Text}” için sonuçlar", new Rect(Pad + 2, GridTop - 22, PanelW - Pad * 2, 18), 11, Theme.Text3, FontWeight.Bold);

        // ızgara
        var area = new Rect(Pad, GridTop, GridW, ViewH);
        float maxScroll = MathF.Max(0, MathF.Ceiling(_items.Count / (float)Cols) * CellH - area.Height);
        if (U.Hit(area) && U.Wheel != 0) _scroll.Target -= U.Wheel * CellH;
        _scroll.Target = Math.Clamp(_scroll.Target, 0, maxScroll);

        if (_items.Count == 0) G.Text("Uygulama bulunamadı", area, 14, Theme.Text3, FontWeight.SemiBold, TextAlignment.Center);

        G.PushClip(area);
        int budget = 5;
        for (int i = 0; i < _items.Count; i++)
        {
            int col = i % Cols, row = i / Cols;
            float cx = area.X + col * CellW + CellW / 2;
            float cy = area.Y + row * CellH - _scroll.Value + CellH / 2;
            if (cy + CellH / 2 < area.Y - 4 || cy - CellH / 2 > area.Bottom + 4) continue;
            var cell = new Rect(cx - CellW / 2, cy - CellH / 2, CellW, CellH);
            int id = Ui.Id("lp", _items[i].Path.GetHashCode());
            bool hit = U.Hit(cell) && U.Hit(area);
            if (hit && U.Pressed) _sel = i;
            bool sel = i == _sel;
            float hv = U.Anim(id, hit || sel ? 1 : 0);
            if (hv > 0.01f) G.FillRound(new Rect(cell.X + 6, cell.Y + 4, cell.Width - 12, cell.Height - 8), 14, Theme.Rgb(0xFFFFFF, 0.1f * hv));
            var ir = new Rect(cx - IconSize / 2, cy - CellH / 2 + 12, IconSize, IconSize);
            var bmp = (I.Apps.HasIcon(_items[i].Path, 64) || budget > 0) ? LoadIcon(_items[i].Path, ref budget) : null;
            if (bmp != null) G.Bitmap(bmp, ir);
            else { G.VGradientRound(ir, 11, Theme.Rgb(0x3A3F52), Theme.Rgb(0x23262F)); G.Text(_items[i].Name.Length > 0 ? _items[i].Name[..1].ToUpper() : "?", ir, 17, Theme.Text, FontWeight.Bold, TextAlignment.Center); }
            G.Text(_items[i].Name, new Rect(cell.X + 3, ir.Bottom + 6, cell.Width - 6, 30), 10.5f, Theme.Text, FontWeight.SemiBold, TextAlignment.Center, wrap: true);
            if (hit) U.WantHand = true;
            if (hit && U.Released && U.ActiveId == 0) { _sel = i; Launch(); G.PopClip(); return; }
        }
        G.PopClip();
        if (budget <= 0) U.NeedsFrame = true;

        if (maxScroll > 0)
        {
            float th = area.Height * area.Height / (area.Height + maxScroll);
            float ty = area.Y + (area.Height - th) * (_scroll.Value / maxScroll);
            G.FillRound(new Rect(area.Right - 3, ty, 3, th), 1.5f, Theme.Rgb(0xFFFFFF, 0.2f));
        }
    }

    ID2D1Bitmap? LoadIcon(string path, ref int budget)
    {
        if (I.Apps.HasIcon(path, 64)) return I.Apps.Icon(path, 64);
        if (budget <= 0) return null;
        budget--;
        return I.Apps.Icon(path, 64);
    }
}
