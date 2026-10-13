using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Ada.Core;
using Ada.Native;
using Ada.Services;
using Ada.Services.Ai;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

using Ada.Data;
namespace Ada;

/// <summary>Bağımsız yapay zeka penceresi — üst ada kapalıyken bile kısayolla açılır (Ctrl+Shift+Space).</summary>
public sealed unsafe class AiWindow : ISurface
{
    const float W = 560, H = 620, M = 26, R = 22;
    static bool _registered;

    readonly Island I;
    Gfx G => I.G;
    readonly Ui U;
    readonly AiView _view = new();
    public IntPtr Hwnd { get; private set; }
    public bool Animating { get; private set; }
    bool _dirty = true, _open, _shown, _focused = true;
    readonly Stopwatch _sw = Stopwatch.StartNew();
    double _last;
    Smooth _vis = new(0, 16);
    int _pw, _ph;

    public AiWindow(Island island)
    {
        I = island;
        U = new Ui(island.G);
        _view.Box.Placeholder = "Ada'ya yaz…";
        var inst = Win32.GetModuleHandleW(IntPtr.Zero);
        if (!_registered)
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Island.ProcPtr, hInstance = inst,
                hCursor = Win32.LoadCursorW(IntPtr.Zero, 32512), hIcon = I.AppIconLarge, hIconSm = I.AppIconSmall,
                lpszClassName = Marshal.StringToHGlobalUni("AdaAi")
            };
            Win32.RegisterClassExW(ref wc);
            _registered = true;
        }
        float s = I.Scale;
        _pw = (int)MathF.Ceiling((W + M * 2) * s); _ph = (int)MathF.Ceiling((H + M * 2) * s);
        var mon = I.MonitorRect;
        int x = (mon.Left + mon.Right - _pw) / 2, y = mon.Top + (int)((mon.Bottom - mon.Top) * 0.12f);
        const int WS_EX_APPWINDOW = 0x40000;
        Hwnd = Win32.CreateWindowExW(Win32.WS_EX_LAYERED | WS_EX_APPWINDOW, "AdaAi", "Ada — Yapay Zeka",
            Win32.WS_POPUP | 0x80000 /*WS_SYSMENU*/ | 0x20000 /*WS_MINIMIZEBOX*/, x, y, _pw, _ph, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
        I.Ai.Changed += () => _dirty = true;
    }

    public bool IsOpen => _open;
    public void Invalidate() => _dirty = true;
    public void FrameIfNeeded() { if (_shown && (_dirty || Animating) && !Win32.IsIconic(Hwnd)) Frame(); }

    public void Toggle() { if (_open) Hide(); else Show(); }

    public void Show()
    {
        _open = true; _focused = true;
        _view.Box.Caret = _view.Box.Text.Length;
        if (!_shown) { Win32.ShowWindow(Hwnd, 5); _shown = true; }
        Win32.SetForegroundWindow(Hwnd);
        _dirty = true; Animating = true;
    }

    public void Hide() { if (!_open) return; _open = false; _dirty = true; Animating = true; }
    public void Close() => Win32.DestroyWindow(Hwnd);

    void Submit()
    {
        if (string.IsNullOrWhiteSpace(_view.Box.Text)) return;
        if (!I.Ai.Configured) { I.OpenSettings(SettingsWindow.AiPage); return; }
        I.Ai.Send(_view.Box.Text); _view.Box.Clear(); _view.Stick = true;
    }

    public IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l)
    {
        const uint WM_NCHITTEST = 0x84, WM_CLOSE = 0x10, WM_SETCURSOR = 0x20;
        switch (m)
        {
            case WM_NCHITTEST:
            {
                var p = new POINT(Win32.LoWord(l), Win32.HiWord(l)); Win32.ScreenToClient(h, ref p);
                float x = p.X / I.Scale - M, y = p.Y / I.Scale - M;
                // başlık sürüklenir ama sağdaki butonlar (yeni/ayarlar/kapat) tıklanabilsin
                return (y is >= 0 and < 46 && x < W - 120) ? 2 : 1;
            }
            case Win32.WM_MOUSEMOVE:
                U.Mouse = new Vector2(Win32.LoWord(l) / I.Scale - M, Win32.HiWord(l) / I.Scale - M); U.Inside = true;
                var t = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = Win32.TME_LEAVE, hwndTrack = h };
                Win32.TrackMouseEvent(ref t); _dirty = true; return 0;
            case Win32.WM_MOUSELEAVE: if (!U.Down) U.Inside = false; _dirty = true; return 0;
            case Win32.WM_LBUTTONDOWN: U.Down = true; U.Pressed = true; Win32.SetCapture(h); _dirty = true; return 0;
            case Win32.WM_LBUTTONUP: U.Down = false; U.Released = true; Win32.ReleaseCapture(); _dirty = true; return 0;
            case Win32.WM_MOUSEWHEEL: U.Wheel += Win32.HiWord(w) / 120f; _dirty = true; return 0;
            case WM_SETCURSOR: if (U.WantHand && Win32.LoWord(l) == 1) { Win32.SetCursor(Win32.LoadCursorW(IntPtr.Zero, 32649)); return 1; } break;
            case Win32.WM_CHAR: if (_view.Box.OnChar((char)w)) _dirty = true; return 0;
            case Win32.WM_KEYDOWN:
            case Win32.WM_SYSKEYDOWN:
            {
                int vk = (int)w; bool ctrl = Win32.GetKeyState(0x11) < 0;
                if (vk == 0x1B) Hide();
                else if (_view.Box.OnKey(vk, ctrl, () => ClipboardService.ReadText(Hwnd))) Submit();
                _dirty = true; return 0;
            }
            case Win32.WM_ACTIVATE: if (Win32.LoWord(w) == 0) { /* odak kaybı: açık kalsın (iş sürebilir) */ } return 0;
            case WM_CLOSE: Hide(); return 0;
            case Win32.WM_DESTROY: I.AiWindowClosed(); return 0;
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
        _vis.Target = _open ? 1 : 0; _vis.Speed = _open ? 16 : 22; _vis.Step(dt);

        G.Begin(_pw, _ph);
        try
        {
            float a = Ease.OutCubic(_vis.Value);
            G.Alpha = a;
            G.PushTranslate(M, M - 12 * (1 - a), 0.97f + 0.03f * a, new Vector2(W / 2, 0));
            Draw();
            G.PopTransform();
        }
        finally { G.End(); }
        G.PresentInPlace(Hwnd, _pw, _ph);

        Animating = U.NeedsFrame || !_vis.Settled;
        _dirty = false;
        U.EndFrame();
        if (!_open && _vis.Value < 0.01f && _shown) { Win32.ShowWindow(Hwnd, 0); _shown = false; Animating = false; }
    }

    void Draw()
    {
        var win = new Rect(0, 0, W, H);
        G.Shadow(win, R, 20, 0.55f, 8);
        G.FillRound(win, R, Theme.Rgb(0x111420));
        G.VGradientRound(new Rect(0, 0, W, 80), R, Theme.Rgb(0xFFFFFF, 0.06f), Theme.Rgb(0xFFFFFF, 0));
        G.StrokeRound(new Rect(0.5f, 0.5f, W - 1, H - 1), R, Theme.Rgb(0xFFFFFF, 0.1f), 1);

        // başlık
        var pill = new Rect(22, 16, 30, 30);
        G.FillCircle(new Vector2(pill.X + 15, pill.Y + 15), 14, Theme.Accent.WithA(0.9f));
        G.Icon("", new Vector2(pill.X + 15, pill.Y + 15), 13, Theme.Rgb(0x0B140D));
        G.Text("Ada", new Rect(62, 14, 160, 22), 16, Theme.Text, FontWeight.Bold);
        G.Text(Providers.Get(Store.Current.AiProvider).Name, new Rect(62, 34, 220, 16), 10.5f, Theme.Text3, FontWeight.SemiBold);
        if (U.IconButton(Ui.Id("ai.set"), new Vector2(W - 60, 30), 13, "", 12, Theme.Text2)) I.OpenSettings(SettingsWindow.AiPage);
        if (U.IconButton(Ui.Id("ai.x"), new Vector2(W - 28, 30), 13, "", 11, Theme.Text2)) Dispatcher.Post(Hide);
        if (I.Ai.Bubbles.Count > 0 && U.IconButton(Ui.Id("ai.new"), new Vector2(W - 92, 30), 13, "", 12, Theme.Text2)) I.Ai.Reset();

        var area = new Rect(22, 60, W - 44, H - 76);
        if (_view.Draw(U, G, I.Ai, area, _focused, Submit, () => I.OpenSettings(SettingsWindow.AiPage)))
            _focused = true;
        if (_open) U.NeedsFrame = true; // imleç yanıp söner
    }
}
