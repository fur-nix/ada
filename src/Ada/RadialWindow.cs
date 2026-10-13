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
/// İmlecin etrafında açılan dairesel başlatıcı: Dock'taki uygulamalar halkada, ortada Spotlight.
/// Fareyi bir dilime götür → vurgulanır, tıkla → açılır. Esc / dışarı tıklama / odak kaybı → kapanır.
/// </summary>
public sealed unsafe class RadialWindow : ISurface
{
    const float S = 440, Outer = 158, Inner = 58, IconR = 108, IconSize = 46, MaxItems = 12;
    static bool _registered;

    readonly Island I;
    Gfx G => I.G;
    readonly Ui U;
    public IntPtr Hwnd { get; private set; }
    public bool Animating { get; private set; }
    bool _dirty = true, _open, _shown;
    readonly Stopwatch _sw = Stopwatch.StartNew();
    double _last;
    Spring _scale = new(0.6f, 340, 22);
    Smooth _alpha = new(0, 18);
    float _openTime;
    int _pw;
    List<PinnedApp> _apps = new();

    public RadialWindow(Island island)
    {
        I = island;
        U = new Ui(island.G);
        var inst = Win32.GetModuleHandleW(IntPtr.Zero);
        if (!_registered)
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Island.ProcPtr, hInstance = inst,
                hCursor = Win32.LoadCursorW(IntPtr.Zero, 32512), lpszClassName = Marshal.StringToHGlobalUni("AdaRadial")
            };
            Win32.RegisterClassExW(ref wc);
            _registered = true;
        }
        _pw = (int)MathF.Ceiling(S * I.Scale);
        Hwnd = Win32.CreateWindowExW(Win32.WS_EX_LAYERED | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST | Win32.WS_EX_ACCEPTFILES, "AdaRadial", "Ada Daire",
            Win32.WS_POPUP, 0, 0, _pw, _pw, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
        Win32.DragAcceptFiles(Hwnd, true); // açıkken üzerine bırakılan dosya → daireye eklenir
    }

    public bool IsOpen => _open;

    /// <summary>--snap: ekranın ortasında açık hali (isteğe bağlı bir dilim vurgulu) PNG'ye çizer.</summary>
    public void SnapRender(string path, int hoverIndex)
    {
        _apps = Store.Current.RadialApps.Take((int)MaxItems).ToList();
        _open = true; _shown = true; _scale.Snap(1); _alpha.Value = _alpha.Target = 1; _openTime = U.Time - 5;
        int n = Math.Max(1, _apps.Count);
        float ang = (hoverIndex * 360f / n - 90) * MathF.PI / 180;
        U.Inside = hoverIndex >= 0;
        U.Mouse = new Vector2(S / 2 + MathF.Cos(ang) * IconR, S / 2 + MathF.Sin(ang) * IconR);
        for (int i = 0; i < 60; i++) Frame();
        G.SavePng(path, _pw, _pw);
        _open = false; _shown = false;
    }
    public void Invalidate() => _dirty = true;
    public void FrameIfNeeded() { if (_shown && (_dirty || Animating)) Frame(); }

    public void Toggle() { if (_open) Close(); else Open(); }

    public void Open() { Win32.GetCursorPos(out var p); Open(p, false); }

    /// <summary>hold: sağ tuş hâlâ basılı (imleç takip edilir, bırakınca seçilir/kapanır).</summary>
    public void Open(POINT p, bool hold)
    {
        _hold = hold;
        _apps = Store.Current.RadialApps.Take((int)MaxItems).ToList();
        var mon = I.MonitorRect;
        int x = Math.Clamp(p.X - _pw / 2, mon.Left, mon.Right - _pw);
        int y = Math.Clamp(p.Y - _pw / 2, mon.Top, mon.Bottom - _pw);
        _wx = x; _wy = y;
        Win32.SetWindowPos(Hwnd, Win32.HWND_TOPMOST, x, y, _pw, _pw, Win32.SWP_NOACTIVATE);
        foreach (var a in _apps) I.Apps.Icon(a.Path, IconSize * 1.2f); // ikonları önceden hazırla
        _open = true;
        _scale.Snap(0.62f); _scale.Target = 1;
        _alpha.Value = 0;
        _openTime = U.Time;
        if (!_shown) { Win32.ShowWindow(Hwnd, 5); _shown = true; }
        Win32.SetForegroundWindow(Hwnd);
        _dirty = true; Animating = true;
    }

    bool _hold;
    int _wx, _wy;
    int _lastHover = -1;
    bool _lastCenter;

    /// <summary>Ekran noktası halkanın üstünde mi?</summary>
    public bool Contains(POINT p)
    {
        float cx = _wx + _pw / 2f, cy = _wy + _pw / 2f;
        float dx = p.X - cx, dy = p.Y - cy;
        return dx * dx + dy * dy <= MathF.Pow((Outer + 12) * I.Scale, 2);
    }

    /// <summary>Basılı tutulan sağ tuş bırakıldı.</summary>
    public void Released()
    {
        if (!_open || !_hold) return;
        _hold = false;
        if (Store.Current.RadialReleaseMode == 1) { _dirty = true; return; } // açık kalsın: tıkla seç
        if (_lastCenter) { Close(); Dispatcher.Post(I.ToggleSpotlight); }
        else if (_lastHover >= 0 && _lastHover < _apps.Count) Launch(_apps[_lastHover]);
        else Close();
    }

    void Launch(PinnedApp app)
    {
        Close();
        var c2 = Store.Current.LaunchCounts;
        c2[app.Path] = (c2.TryGetValue(app.Path, out var k) ? k : 0) + 1;
        Dispatcher.Post(() => AppsService.Launch(app));
    }

    public void Close()
    {
        if (!_open) return;
        _hold = false;
        _open = false;
        _scale.Target = 0.8f;
        _dirty = true; Animating = true;
    }

    public IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l)
    {
        const uint WM_SETCURSOR = 0x20;
        switch (m)
        {
            case Win32.WM_MOUSEMOVE:
                U.Mouse = new Vector2(Win32.LoWord(l) / I.Scale, Win32.HiWord(l) / I.Scale);
                U.Inside = true; _dirty = true; return 0;
            case Win32.WM_LBUTTONDOWN: U.Down = true; U.Pressed = true; _dirty = true; return 0;
            case Win32.WM_LBUTTONUP: U.Down = false; U.Released = true; _dirty = true; return 0;
            case Win32.WM_RBUTTONUP: U.RightReleased = true; _dirty = true; return 0;
            case Win32.WM_DROPFILES:
            {
                char* buf = stackalloc char[1024];
                uint n = Win32.DragQueryFileW(w, 0xFFFFFFFF, null, 0);
                for (uint i = 0; i < n; i++)
                {
                    uint len = Win32.DragQueryFileW(w, i, buf, 1024);
                    I.AddToRadial(new string(buf, 0, (int)len), toast: false);
                }
                Win32.DragFinish(w);
                _apps = Store.Current.RadialApps.Take((int)MaxItems).ToList();
                _dirty = true; Animating = true;
                return 0;
            }
            case Win32.WM_KEYDOWN: if ((int)w == 0x1B) Close(); return 0;
            case Win32.WM_ACTIVATE: if (Win32.LoWord(w) == 0) Close(); return 0;
            case WM_SETCURSOR:
                if (U.WantHand && Win32.LoWord(l) == 1) { Win32.SetCursor(Win32.LoadCursorW(IntPtr.Zero, 32649)); return 1; }
                break;
        }
        return Win32.DefWindowProcW(h, m, w, l);
    }

    void Frame()
    {
        if (G.InDraw) { _dirty = true; return; }
        double now = _sw.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - _last, 1 / 240.0, 1 / 24.0);
        if (!Animating) dt = 1 / 60f;
        _last = now;
        U.Dt = dt; U.Time += dt; U.NeedsFrame = false; U.WantHand = false;
        // basılı tutarken pencere mesajı beklemeden imleci doğrudan izle
        if (_hold && Win32.GetCursorPos(out var cp)) { U.Mouse = new Vector2((cp.X - _wx) / I.Scale, (cp.Y - _wy) / I.Scale); U.Inside = true; U.NeedsFrame = true; }
        _scale.Step(dt);
        _alpha.Target = _open ? 1 : 0; _alpha.Speed = _open ? 16 : 24; _alpha.Step(dt);

        G.Begin(_pw, _pw);
        try { Draw(); }
        finally { G.End(); }
        G.PresentInPlace(Hwnd, _pw, _pw);

        Animating = U.NeedsFrame || !_scale.Settled || !_alpha.Settled;
        _dirty = false;
        U.EndFrame();
        if (!_open && _alpha.Value < 0.01f && _shown) { Win32.ShowWindow(Hwnd, 0); _shown = false; Animating = false; }
    }

    void Draw()
    {
        var c = new Vector2(S / 2, S / 2);
        float a = _alpha.Value, sc = _scale.Value;
        G.Alpha = a;
        G.PushTranslate(0, 0, sc, c);

        int n = Math.Max(1, _apps.Count);
        float seg = 360f / n;
        // fare hangi dilimde?
        var d = U.Mouse - c;
        float dist = d.Length() / MathF.Max(0.01f, sc);
        int hover = -1;
        bool centerHover = U.Inside && dist < Inner;
        if (U.Inside && dist >= Inner && dist <= Outer + 40 && _apps.Count > 0)
        {
            float ang = (MathF.Atan2(d.Y, d.X) * 180 / MathF.PI + 90 + 360 + seg / 2) % 360; // 0 = tepe
            hover = (int)(ang / seg) % n;
        }

        // halka: gölge + cam + iç/dış kenar
        G.SoftShadow(new Vector2(c.X, c.Y + 10), Outer + 26, Outer + 22, 0.55f);
        G.FillCircle(c, Outer, Theme.Rgb(0x161922, 0.90f));
        G.StrokeCircle(c, Outer - 0.5f, Theme.Rgb(0xFFFFFF, 0.14f), 1);
        // dilim vurgusu (yayılan halka parçası)
        for (int i = 0; i < _apps.Count; i++)
        {
            float hv = U.Anim(Ui.Id("rad.h", i), i == hover ? 1 : 0, 20);
            if (hv < 0.01f) continue;
            float start = i * seg - seg / 2 + 2;
            G.Arc(c, (Outer + Inner) / 2, start, seg - 4, Theme.Accent.WithA(0.28f * hv), Outer - Inner - 6);
        }
        // ayraçlar
        for (int i = 0; i < _apps.Count && _apps.Count > 1; i++)
        {
            float ang = (i * seg - seg / 2 - 90) * MathF.PI / 180;
            var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            G.Line(c + dir * (Inner + 6), c + dir * (Outer - 8), Theme.Rgb(0xFFFFFF, 0.06f), 1);
        }

        // ikonlar (sırayla belirir)
        for (int i = 0; i < _apps.Count; i++)
        {
            var app = _apps[i];
            float appear = Ease.OutCubic(Ease.Clamp01((U.Time - _openTime - i * 0.025f) / 0.22f));
            if (appear < 1) U.NeedsFrame = true;
            float hv = U.Anim(Ui.Id("rad.s", i), i == hover ? 1 : 0, 18);
            float ang = (i * seg - 90) * MathF.PI / 180;
            float r = IconR * (0.75f + 0.25f * appear) + 6 * hv;
            var p = c + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * r;
            float sz = IconSize * (0.6f + 0.4f * appear) * (1 + 0.18f * hv);
            var rr = new Rect(p.X - sz / 2, p.Y - sz / 2, sz, sz);
            float old = G.Alpha; G.Alpha = old * appear;
            G.SoftShadow(new Vector2(p.X, rr.Bottom - 2), sz * 0.45f, sz * 0.12f, 0.4f);
            var bmp = I.Apps.Icon(app.Path, IconSize * 1.2f);
            if (bmp != null) G.Bitmap(bmp, rr);
            else
            {
                G.VGradientRound(rr, sz * 0.24f, Theme.Rgb(0x8E8E93), Theme.Rgb(0x48484A));
                G.Icon(app.Path.StartsWith("ms-settings") ? Glyph.Settings : Glyph.Apps, p, sz * 0.3f, Theme.Text);
            }
            G.Alpha = old;
        }

        // merkez: Spotlight / üzerinde durulan uygulamanın adı
        float ch = U.Anim(Ui.Id("rad.c"), centerHover ? 1 : 0, 20);
        G.FillCircle(c, Inner - 4, Theme.Mix(Theme.Rgb(0x0E1018), Theme.Accent.WithA(0.9f), ch * 0.85f));
        G.StrokeCircle(c, Inner - 4, Theme.Rgb(0xFFFFFF, 0.12f), 1);
        if (hover >= 0)
            G.Text(_apps[hover].Name, new Rect(c.X - Inner + 6, c.Y - 12, (Inner - 6) * 2, 24), 11.5f, Theme.Text, FontWeight.Bold, TextAlignment.Center);
        else
        {
            var fg = Theme.Mix(Theme.Text, Theme.TileOnText, ch);
            G.Icon("", new Vector2(c.X, c.Y - 7), 15, fg);
            G.Text("Ara", new Rect(c.X - 30, c.Y + 6, 60, 16), 10.5f, fg, FontWeight.SemiBold, TextAlignment.Center);
        }
        if (_apps.Count == 0)
        {
            G.Text("Daire boş", new Rect(c.X - 140, c.Y + Inner + 18, 280, 18), 12, Theme.Text, FontWeight.Bold, TextAlignment.Center);
            G.Text("Buraya uygulama sürükle ya da Ayarlar › Daire menüsü", new Rect(c.X - 150, c.Y + Inner + 38, 300, 16), 10.5f, Theme.Text2, FontWeight.SemiBold, TextAlignment.Center);
        }

        G.PopTransform();
        G.Alpha = 1;

        // tıklama
        if (_open && U.Released)
        {
            if (centerHover) { Close(); Dispatcher.Post(I.ToggleSpotlight); }
            else if (hover >= 0) Launch(_apps[hover]);
            else if (dist > Outer + 40) Close();
        }
        // sağ tık bir öğede → daireden çıkar; boşlukta → kapat
        if (_open && U.RightReleased)
        {
            if (hover >= 0)
            {
                var app = _apps[hover];
                Store.Current.RadialApps.RemoveAll(x => x.Path.Equals(app.Path, StringComparison.OrdinalIgnoreCase));
                Store.Save();
                _apps = Store.Current.RadialApps.Take((int)MaxItems).ToList();
                U.NeedsFrame = true;
            }
            else Close();
        }
        if (hover >= 0 || centerHover) U.WantHand = true;
        _lastHover = hover; _lastCenter = centerHover;
    }
}
