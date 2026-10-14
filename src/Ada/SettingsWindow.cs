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

using Ada.Services.Ai;
namespace Ada;

/// <summary>
/// "Ada Ayarları" penceresi: adayla aynı çizim katmanını (Gfx) paylaşır, ayrı bir uygulama kadar RAM yemez.
/// Başlat menüsündeki "Ada Ayarları" kısayolu, adadaki dişli ve sağ tık menüsü bunu açar.
/// </summary>
public sealed unsafe class SettingsWindow : ISurface
{
    const float W = 880, H = 600, M = 26, SideW = 214, R = 22;
    static bool _registered;

    readonly Island I;
    Gfx G => I.G;
    readonly Ui U;
    public IntPtr Hwnd { get; private set; }
    bool _dirty = true;
    public bool Animating { get; private set; }
    readonly Stopwatch _sw = Stopwatch.StartNew();
    double _last;
    int _page, _prevPage = -1;
    float _pageT = 1;
    Spring _nav = new(0, 420, 34);
    bool _confirmReset;
    DateTime _confirmUntil;

    static readonly string[] Pages = { "Özellikler", "Görünüm", "Boşta", "Davranış", "Dock", "Arama", "Daire", "Yapay Zeka", "Sekmeler", "Genel", "Hakkında" };
    public const int FeaturesPage = 0, DockPage = 4, AiPage = 7;
    static readonly string[] PageGlyphs = { "", Glyph.Theme, Glyph.Clock, Glyph.Sliders, "", "", "", "", Glyph.Apps, Glyph.Settings, "" };
    static readonly string[] PageSubs =
    {
        "Dört ana özelliği aç / kapat (hepsi birbirinden bağımsız)",
        "Adanın şekli, boyutu ve rengi",
        "Kapalıyken solda, ortada ve sağda ne görünsün",
        "Açılma, kapanma ve bildirimler",
        "MacBook tarzı alt çubuk ve Windows görev çubuğu",
        "Spotlight: uygulama, dosya, hesap makinesi, web",
        "İmlecin etrafında açılan dairesel başlatıcı",
        "Gemini, Groq, Ollama… ücretsiz de çalışır",
        "Hangi sekmeler görünsün, hangi sırayla",
        "Kısayol tuşu, başlangıç ve veriler",
        "Sürüm, kaynak kullanımı ve yol haritası"
    };
    static readonly string[] SlotNames = { "Boş", "Saat", "Saniyeli", "Tarih", "Pil", "CPU·RAM", "Nokta" };

    public SettingsWindow(Island island)
    {
        I = island;
        U = new Ui(island.G);
        var inst = Win32.GetModuleHandleW(IntPtr.Zero);
        if (!_registered)
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Island.ProcPtr, hInstance = inst,
                hCursor = Win32.LoadCursorW(IntPtr.Zero, 32512), hIcon = I.AppIconLarge, hIconSm = I.AppIconSmall,
                lpszClassName = Marshal.StringToHGlobalUni("AdaSettings")
            };
            Win32.RegisterClassExW(ref wc);
            _registered = true;
        }
        float s = I.Scale;
        int pw = (int)((W + M * 2) * s), ph = (int)((H + M * 2) * s);
        var mon = I.MonitorRect;
        int x = (mon.Left + mon.Right - pw) / 2, y = (mon.Top + mon.Bottom - ph) / 2;
        const int WS_SYSMENU = 0x80000, WS_MINIMIZEBOX = 0x20000, WS_EX_APPWINDOW = 0x40000;
        Hwnd = Win32.CreateWindowExW(Win32.WS_EX_LAYERED | WS_EX_APPWINDOW, "AdaSettings", "Ada Ayarları",
            Win32.WS_POPUP | WS_SYSMENU | WS_MINIMIZEBOX, x, y, pw, ph, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
    }

    public void Show()
    {
        Frame();
        Win32.ShowWindow(Hwnd, Win32.IsIconic(Hwnd) ? 9 : 5); // SW_RESTORE : SW_SHOW
        Win32.SetForegroundWindow(Hwnd);
    }

    public void Close() => Win32.DestroyWindow(Hwnd);

    /// <summary>--snap testi: bir sayfayı ekran dışında çizip PNG'ye kaydeder.</summary>
    public void SnapPage(int page, string path)
    {
        _page = page; _prevPage = -1; _pageT = 1;
        for (int i = 0; i < 40; i++) Frame();
        float s = I.Scale;
        G.SavePng(path, (int)MathF.Ceiling((W + M * 2) * s), (int)MathF.Ceiling((H + M * 2) * s));
    }

    public static int PageCount => Pages.Length;

    public void Invalidate() => _dirty = true;

    public void FrameIfNeeded()
    {
        if ((_dirty || Animating) && !Win32.IsIconic(Hwnd)) Frame();
    }

    // ---------------- pencere mesajları ----------------
    public IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l)
    {
        const uint WM_NCHITTEST = 0x84, WM_CLOSE = 0x10, WM_SETCURSOR = 0x20;
        switch (m)
        {
            case WM_NCHITTEST:
            {
                var p = new POINT(Win32.LoWord(l), Win32.HiWord(l));
                Win32.ScreenToClient(h, ref p);
                float x = p.X / I.Scale - M, y = p.Y / I.Scale - M;
                // başlık bölgesi sürüklenebilir (butonlar hariç)
                if (y >= 0 && y < 48 && x >= 0 && x < W - 96) return 2; // HTCAPTION
                return 1; // HTCLIENT
            }
            case Win32.WM_MOUSEMOVE:
                U.Mouse = new Vector2(Win32.LoWord(l) / I.Scale - M, Win32.HiWord(l) / I.Scale - M);
                U.Inside = true;
                var t = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = Win32.TME_LEAVE, hwndTrack = h };
                Win32.TrackMouseEvent(ref t);
                _dirty = true; return 0;
            case Win32.WM_MOUSELEAVE:
                if (!U.Down) U.Inside = false;
                _dirty = true; return 0;
            case Win32.WM_LBUTTONDOWN:
                U.Down = true; U.Pressed = true; Win32.SetCapture(h); _dirty = true; return 0;
            case Win32.WM_LBUTTONUP:
                U.Down = false; U.Released = true; Win32.ReleaseCapture(); _dirty = true; return 0;
            case Win32.WM_MOUSEWHEEL:
                U.Wheel += Win32.HiWord(w) / 120f; _dirty = true; return 0;
            case WM_SETCURSOR:
                if (U.WantHand && Win32.LoWord(l) == 1) { Win32.SetCursor(Win32.LoadCursorW(IntPtr.Zero, 32649)); return 1; }
                break;
            case Win32.WM_CHAR:
                if (_focusField != null && _focusField.OnChar((char)w)) _dirty = true;
                return 0;
            case Win32.WM_KEYDOWN:
                if (_focusField != null)
                {
                    int vk = (int)w;
                    if (vk == 0x1B) { _focusField = null; _focusFieldId = 0; }
                    else { bool ctrl = Win32.GetKeyState(0x11) < 0; _focusField.OnKey(vk, ctrl, () => ClipboardService.ReadText(Hwnd)); }
                    _dirty = true;
                    return 0;
                }
                if ((int)w == 0x1B) Close();
                else if ((int)w == 0x28 || (int)w == 0x26) Go(Math.Clamp(_page + ((int)w == 0x28 ? 1 : -1), 0, Pages.Length - 1));
                return 0;
            case WM_CLOSE:
                Close(); return 0;
            case Win32.WM_DESTROY:
                I.SettingsClosed();
                return 0;
        }
        return Win32.DefWindowProcW(h, m, w, l);
    }

    public void GoTo(int page) { _page = Math.Clamp(page, 0, Pages.Length - 1); _prevPage = -1; _pageT = 1; _dirty = true; }

    void Go(int page)
    {
        if (page == _page) return;
        _prevPage = _page; _page = page; _pageT = 0; _dirty = true;
        _scroll.Snap(0);
    }

    // ---------------- çizim ----------------
    void Frame()
    {
        double now = _sw.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - _last, 1 / 240.0, 1 / 24.0);
        if (!Animating) dt = 1 / 60f;
        _last = now;
        U.Dt = dt; U.Time += dt; U.NeedsFrame = false; U.WantHand = false;
        if (_pageT < 1) _pageT = MathF.Min(1, _pageT + dt / 0.3f);

        float s = I.Scale;
        int pw = (int)MathF.Ceiling((W + M * 2) * s), ph = (int)MathF.Ceiling((H + M * 2) * s);
        if (G.InDraw) { _dirty = true; return; }
        G.Begin(pw, ph);
        try { G.PushTranslate(M, M); Draw(); G.PopTransform(); }
        finally { G.End(); }
        G.PresentInPlace(Hwnd, pw, ph);

        Animating = U.NeedsFrame || _pageT < 1 || !_nav.Settled;
        _dirty = false;
        U.EndFrame();
        U.GcAnims();
    }

    void Draw()
    {
        var win = new Rect(0, 0, W, H);
        G.Shadow(win, R, 18, 0.55f, 8);
        G.FillRound(win, R, Theme.Rgb(0x111420));
        // kenar çubuğu
        G.FillRound(new Rect(0, 0, SideW + R, H), R, Theme.Rgb(0x0B0D15));
        G.FillRect(new Rect(SideW, 0, R, H), Theme.Rgb(0x111420));
        G.StrokeRound(new Rect(0.5f, 0.5f, W - 1, H - 1), R, Theme.Rgb(0xFFFFFF, 0.08f), 1);

        // logo
        var pill = new Rect(22, 26, 46, 16);
        G.FillRound(pill, 8, Theme.Rgb(0x000000));
        G.StrokeRound(pill, 8, Theme.Rgb(0xFFFFFF, 0.18f), 1);
        G.FillCircle(new Vector2(pill.X + 9, pill.CenterY), 2.6f, Theme.Accent);
        G.Text("Ada", new Rect(78, 18, 120, 30), 18, Theme.Text, FontWeight.Bold);
        G.Text("AYARLAR", new Rect(22, 50, 160, 16), 9.5f, Theme.Text3, FontWeight.Bold);

        // gezinme
        float ny = 78, nh = 40;
        _nav.Target = _page; _nav.Step(U.Dt);
        G.FillRound(new Rect(12, ny + _nav.Value * (nh + 4), SideW - 24, nh), 12, Theme.Rgb(0xFFFFFF, 0.08f));
        for (int i = 0; i < Pages.Length; i++)
        {
            var r = new Rect(12, ny + i * (nh + 4), SideW - 24, nh);
            int id = Ui.Id("set.nav", i);
            if (U.Click(id, r)) Go(i);
            float hv = U.Anim(id, U.Hit(r) && i != _page ? 1 : 0);
            if (hv > 0.01f) G.FillRound(r, 12, Theme.Rgb(0xFFFFFF, 0.04f * hv));
            bool act = i == _page;
            G.Icon(PageGlyphs[i], new Vector2(r.X + 22, r.CenterY), 13, act ? Theme.Accent : Theme.Text2);
            G.Text(Pages[i], new Rect(r.X + 42, r.Y, r.Width - 50, r.Height), 13, act ? Theme.Text : Theme.Text2, act ? FontWeight.SemiBold : FontWeight.Normal);
        }
        G.Text("Esc: kapat · ↑↓: sayfa", new Rect(22, H - 34, SideW - 30, 16), 10, Theme.Text3, FontWeight.Normal, font: Theme.TextFont);

        // pencere butonları
        if (U.IconButton(Ui.Id("set.min"), new Vector2(W - 66, 26), 13, "", 10, Theme.Text2)) Win32.ShowWindow(Hwnd, 6);
        if (U.IconButton(Ui.Id("set.close"), new Vector2(W - 32, 26), 13, "", 10, Theme.Text2)) { Dispatcher.Post(Close); }

        // içerik (sayfa geçişi: kayma + solma)
        var area = new Rect(SideW + 36, 24, W - SideW - 72, H - 48);
        if (_pageT < 1 && _prevPage >= 0)
        {
            float t = Ease.OutCubic(_pageT);
            bool inside = U.Inside;
            U.Inside = false;
            G.Alpha = 1 - Ease.Clamp01(_pageT * 2.4f);
            G.PushTranslate(0, -14 * t); DrawPage(_prevPage, area); G.PopTransform();
            U.Inside = inside;
            G.Alpha = Ease.OutCubic(Ease.Clamp01((_pageT - 0.15f) / 0.85f));
            G.PushTranslate(0, 14 * (1 - t)); DrawPage(_page, area); G.PopTransform();
            G.Alpha = 1;
        }
        else DrawPage(_page, area);
    }

    void DrawPage(int p, Rect a)
    {
        G.Text(Pages[p], new Rect(a.X, a.Y, a.Width, 34), 24, Theme.Text, FontWeight.Bold);
        G.Text(PageSubs[p], new Rect(a.X, a.Y + 34, a.Width, 18), 12.5f, Theme.Text2, FontWeight.Normal, font: Theme.TextFont);
        var body = new Rect(a.X, a.Y + 68, a.Width, a.Height - 68);
        // kaydırılabilir içerik (uzun sayfalar)
        bool cur = p == _page;
        float sc = cur ? _scroll.Value : 0;
        var savedMouse = U.Mouse; bool savedInside = U.Inside;
        U.Inside = savedInside && body.Contains(savedMouse);
        U.Mouse = new Vector2(savedMouse.X, savedMouse.Y + sc);
        G.PushClip(new Rect(body.X - 12, body.Y, body.Width + 24, body.Height));
        G.PushTranslate(0, -sc);
        _lastY = body.Y;
        switch (p)
        {
            case 0: PageFeatures(body); break;
            case 1: PageLook(body); break;
            case 2: PageIdle(body); break;
            case 3: PageBehavior(body); break;
            case 4: PageDock(body); break;
            case 5: PageSearch(body); break;
            case 6: PageRadial(body); break;
            case 7: PageAi(body); break;
            case 8: PageTabs(body); break;
            case 9: PageGeneral(body); break;
            case 10: PageAbout(body); break;
        }
        G.PopTransform();
        G.PopClip();
        U.Mouse = savedMouse; U.Inside = savedInside;
        if (!cur) return;
        float max = MathF.Max(0, _lastY - body.Bottom + 16);
        if (body.Contains(savedMouse) && U.Wheel != 0) _scroll.Target -= U.Wheel * 90;
        _scroll.Target = Math.Clamp(_scroll.Target, 0, max);
        _scroll.Step(U.Dt);
        if (!_scroll.Settled) U.NeedsFrame = true;
        if (max > 0)
        {
            // ince kaydırma çubuğu + alt kenarda solma
            float th = body.Height * body.Height / (body.Height + max);
            float ty = body.Y + (body.Height - th) * (_scroll.Value / max);
            G.FillRound(new Rect(body.Right + 10, ty, 3, th), 1.5f, Theme.Rgb(0xFFFFFF, 0.18f));
            if (_scroll.Value < max - 2) G.VGradientRound(new Rect(body.X - 12, body.Bottom - 30, body.Width + 24, 30), 0, Theme.Rgb(0x111420, 0), Theme.Rgb(0x111420));
        }
    }

    Spring _scroll = new(0, 300, 34);
    float _lastY;

    // ---------- ortak satır ----------
    Rect Row(ref float y, Rect body, string title, string? desc, float h = 56)
    {
        var r = new Rect(body.X, y, body.Width, h);
        G.Text(title, new Rect(r.X, r.Y + (desc == null ? 0 : 8), r.Width - 320, desc == null ? h : 20), 13.5f, Theme.Text, FontWeight.SemiBold);
        if (desc != null) G.Text(desc, new Rect(r.X, r.Y + 28, r.Width - 320, 18), 11.5f, Theme.Text3, FontWeight.Normal, font: Theme.TextFont);
        G.Line(new Vector2(r.X, r.Bottom), new Vector2(r.Right, r.Bottom), Theme.Rgb(0xFFFFFF, 0.05f), 1);
        y += h;
        _lastY = MathF.Max(_lastY, y);
        return new Rect(r.Right - 300, r.Y + (h - 32) / 2, 300, 32);
    }

    void Changed() { I.ApplySettings(save: true); _dirty = true; }

    /// <summary>Canlı önizleme: ekranın üst kenarı + o anki ayarlarla kapalı ada.</summary>
    void Preview(Rect r)
    {
        var st = Store.Current;
        G.VGradientRound(r, 16, Theme.Rgb(0x55698F), Theme.Rgb(0xB9A78C));
        G.StrokeRound(r, 16, Theme.Rgb(0xFFFFFF, 0.08f), 1);
        G.PushClip(r);
        float k = Island.SizeFactors[st.Size];
        bool playing = I.Media.HasSession && I.Media.Playing && st.MediaInIdle;
        float w = (playing ? Island.PlayW : Island.CW) * k + 20, h = Island.CH * k;
        float top = st.Notch ? r.Y : r.Y + Island.Top;
        var isl = new Rect(r.CenterX - w / 2, top, w, h);
        float rad = h / 2 * st.Roundness;
        var body = st.Notch ? new Rect(isl.X, isl.Y - rad, isl.Width, isl.Height + rad) : isl;
        G.Shadow(body, rad, 8, st.Notch ? 0.2f : 0.35f, 3);
        G.FillRound(body, rad, Theme.Island);
        if (st.Notch)
        {
            G.NotchEar(new Vector2(isl.X + 0.5f, r.Y), 9, true, Theme.Island);
            G.NotchEar(new Vector2(isl.Right - 0.5f, r.Y), 9, false, Theme.Island);
        }
        G.PushClip(isl);
        I.DrawIdle(isl, k);
        G.PopClip();
        G.PopClip();
        G.Text("Canlı önizleme", new Rect(r.X + 14, r.Bottom - 26, 200, 18), 10.5f, Theme.Rgb(0xFFFFFF, 0.75f), FontWeight.SemiBold);
        if (playing) U.NeedsFrame = true;
    }

    // ---------- sayfalar ----------
    void PageLook(Rect b)
    {
        var st = Store.Current;
        Preview(new Rect(b.X, b.Y, b.Width, 104));
        float y = b.Y + 116;
        var c = Row(ref y, b, "Stil", "Çentik: MacBook gibi üst kenara yapışık · Ada: yüzen hap");
        int v = U.Segmented(Ui.Id("s.style"), c, new[] { "Çentik (MacBook)", "Yüzen ada" }, st.Notch ? 0 : 1);
        if (v != (st.Notch ? 0 : 1)) { st.Notch = v == 0; Changed(); }

        c = Row(ref y, b, "Boyut", "Kapalı halin büyüklüğü");
        v = U.Segmented(Ui.Id("s.size"), c, new[] { "Küçük", "Orta", "Büyük" }, st.Size);
        if (v != st.Size) { st.Size = v; Changed(); }

        c = Row(ref y, b, "Köşe yuvarlaklığı", $"%{st.Roundness * 100:0}");
        float rnd = (st.Roundness - 0.4f) / 0.6f;
        if (U.SliderPlain(Ui.Id("s.round"), new Rect(c.X + 10, c.Y, c.Width - 20, c.Height), ref rnd, Theme.Accent))
        { st.Roundness = 0.4f + 0.6f * rnd; Changed(); }

        c = Row(ref y, b, "Vurgu rengi", "Sekmeler, halkalar ve düğmeler");
        var pal = Island.AccentPalette;
        for (int i = 0; i < pal.Length; i++)
        {
            var hex = pal[i].ToString("X6");
            if (U.Swatch(Ui.Id("s.acc", i), new Vector2(c.X + 18 + i * 37, c.CenterY), Theme.Rgb(pal[i]), string.Equals(st.Accent, hex, StringComparison.OrdinalIgnoreCase)))
            { st.Accent = hex; Changed(); }
        }

        c = Row(ref y, b, "Gölge", "Adanın altında yumuşak gölge");
        if (U.Toggle(Ui.Id("s.shadow"), c, st.Shadow)) { st.Shadow = !st.Shadow; Changed(); }
    }

    void PageIdle(Rect b)
    {
        var st = Store.Current;
        Preview(new Rect(b.X, b.Y, b.Width, 104));
        float y = b.Y + 120;
        void SlotRow(string title, Slot cur, Action<Slot> set, string key)
        {
            G.Text(title, new Rect(b.X, y, 200, 20), 13, Theme.Text, FontWeight.SemiBold);
            int v = U.Chips(Ui.Id(key), new Rect(b.X, y + 24, b.Width, 30), SlotNames, (int)cur, Theme.Accent);
            if (v != (int)cur) { set((Slot)v); Changed(); }
            y += 66;
        }
        SlotRow("Sol", st.IdleLeft, s => st.IdleLeft = s, "s.left");
        SlotRow("Orta", st.IdleCenter, s => st.IdleCenter = s, "s.center");
        SlotRow("Sağ", st.IdleRight, s => st.IdleRight = s, "s.right");
        var c = Row(ref y, b, "Müzik çalarken kapak ve ekolayzer", "Sol/sağ yuvaların yerine geçer");
        if (U.Toggle(Ui.Id("s.media"), c, st.MediaInIdle)) { st.MediaInIdle = !st.MediaInIdle; Changed(); }
    }

    void PageBehavior(Rect b)
    {
        var st = Store.Current;
        float y = b.Y;
        string[] visDesc =
        {
            "Ada her zaman ekranın üstünde durur",
            "Gizli durur; fareyi ekranın üst ortasına götürünce belirir",
            "Gizli durur; yalnızca kısayol tuşu ve bildirimlerle görünür"
        };
        var c = Row(ref y, b, "Görünürlük", visDesc[st.Visibility], 58);
        var vr = new Rect(c.X - 90, c.Y, c.Width + 90, c.Height);
        int v = U.Segmented(Ui.Id("s.vis"), vr, new[] { "Her zaman", "Fare gelince", "Sadece kısayol" }, st.Visibility);
        if (v != st.Visibility) { st.Visibility = v; Changed(); }

        string[] ovDesc =
        {
            "Tarayıcı vb. tam açıkken de ada normal boyutta kalır",
            "Sekmelerin önüne geçmeyen ince bir çizgiye dönüşür; üzerine gelince açılır",
            "Tamamen çekilir; fareyi üst kenara götürünce belirir"
        };
        c = Row(ref y, b, "Uygulama penceresi üstteyken", ovDesc[st.AppOverlapMode], 58);
        var ovr = new Rect(c.X - 90, c.Y, c.Width + 90, c.Height);
        v = U.Segmented(Ui.Id("s.overlap"), ovr, new[] { "Normal", "Çok küçük", "Fare gelince" }, st.AppOverlapMode);
        if (v != st.AppOverlapMode) { st.AppOverlapMode = v; Changed(); }

        c = Row(ref y, b, "Üzerine gelince aç", "Tıklamadan, imleç adada kısa süre durunca açılır", 50);
        if (U.Toggle(Ui.Id("s.hover"), c, st.OpenOnHover)) { st.OpenOnHover = !st.OpenOnHover; Changed(); }
        c = Row(ref y, b, "Kapanma gecikmesi", $"İmleç çıktıktan {st.CollapseDelayMs / 1000f:0.0} sn sonra kapanır", 50);
        float d = (st.CollapseDelayMs - 150) / 1350f;
        if (U.SliderPlain(Ui.Id("s.delay"), new Rect(c.X + 10, c.Y, c.Width - 20, c.Height), ref d, Theme.Accent))
        { st.CollapseDelayMs = (int)(150 + d * 1350); Changed(); }
        c = Row(ref y, b, "Animasyon hızı", "Yay animasyonlarının temposu", 50);
        v = U.Segmented(Ui.Id("s.anim"), c, new[] { "Yumuşak", "Normal", "Hızlı" }, st.AnimSpeed);
        if (v != st.AnimSpeed) { st.AnimSpeed = v; Changed(); }
        c = Row(ref y, b, "Tam ekranda gizle", "Oyun ve tam ekran videoda ada çekilir", 50);
        if (U.Toggle(Ui.Id("s.fs"), c, st.HideOnFullscreen)) { st.HideOnFullscreen = !st.HideOnFullscreen; Changed(); }

        G.Text("BİLDİRİMLER", new Rect(b.X, y + 10, 200, 16), 10, Theme.Text3, FontWeight.Bold);
        y += 28;
        c = Row(ref y, b, "Kopyalama", null, 40);
        if (U.Toggle(Ui.Id("s.n1"), c, st.NotifyClipboard)) { st.NotifyClipboard = !st.NotifyClipboard; Changed(); }
        c = Row(ref y, b, "Şarkı değişimi", null, 40);
        if (U.Toggle(Ui.Id("s.n2"), c, st.NotifyTrack)) { st.NotifyTrack = !st.NotifyTrack; Changed(); }
        c = Row(ref y, b, "Hatırlatıcılar ve sayaç", null, 40);
        if (U.Toggle(Ui.Id("s.n3"), c, st.NotifyReminder)) { st.NotifyReminder = !st.NotifyReminder; Changed(); }
        c = Row(ref y, b, "Hatırlatıcı sesi", null, 40);
        if (U.Toggle(Ui.Id("s.n4"), c, st.ReminderSound)) { st.ReminderSound = !st.ReminderSound; Changed(); }
    }

    void PageFeatures(Rect b)
    {
        var st = Store.Current;
        float y = b.Y;
        FeatureCard(ref y, b, "Üst ada", "Ekranın üstündeki dynamic island (sekmeler, medya, bildirimler).", "",
            st.IslandEnabled, () => { st.IslandEnabled = !st.IslandEnabled; Changed(); });
        FeatureCard(ref y, b, "Dock", "Ekranın altındaki MacBook tarzı uygulama çubuğu.", "",
            st.DockEnabled, () => { st.DockEnabled = !st.DockEnabled; Changed(); });
        FeatureCard(ref y, b, "İmleç menüsü (daire)", "Sağ tuşu basılı tutunca imlecin etrafında açılan başlatıcı.", "",
            st.RadialEnabled, () => { st.RadialEnabled = !st.RadialEnabled; Changed(); });
        FeatureCard(ref y, b, "Yapay zeka asistanı", "Bilgisayarı kullanan sohbet asistanı — Ctrl+Shift+Space ile her yerden açılır.", "",
            st.AiEnabled, () => { st.AiEnabled = !st.AiEnabled; Changed(); });
        G.Text("Her özellik birbirinden bağımsız. İstersen sadece yapay zekayı ya da sadece imleç menüsünü kullanabilirsin; " +
               "üst ada kapalıyken bile yapay zeka Ctrl+Shift+Space ile açılır.",
            new Rect(b.X, y + 8, b.Width, 40), 11.5f, Theme.Text3, FontWeight.Normal, font: Theme.TextFont, wrap: true);
    }

    void FeatureCard(ref float y, Rect b, string title, string desc, string glyph, bool on, Action toggle)
    {
        var r = new Rect(b.X, y, b.Width, 64);
        G.FillRound(r, 14, Theme.Mix(Theme.Rgb(0xFFFFFF, 0.03f), Theme.Accent.WithA(0.1f), on ? 1 : 0));
        G.StrokeRound(r, 14, on ? Theme.Accent.WithA(0.4f) : Theme.Rgb(0xFFFFFF, 0.06f), 1);
        var ic = new Vector2(r.X + 30, r.CenterY);
        G.FillCircle(ic, 18, on ? Theme.Accent.WithA(0.9f) : Theme.Rgb(0xFFFFFF, 0.08f));
        G.Icon(glyph, ic, 15, on ? Theme.Rgb(0x0B140D) : Theme.Text2);
        G.Text(title, new Rect(r.X + 58, r.Y + 12, r.Width - 160, 20), 14, Theme.Text, FontWeight.Bold);
        G.Text(desc, new Rect(r.X + 58, r.Y + 34, r.Width - 160, 18), 11, Theme.Text3, FontWeight.Normal, font: Theme.TextFont);
        if (U.Toggle(Ui.Id("feat", title.GetHashCode()), new Rect(r.Right - 70, r.CenterY - 14, 54, 28), on)) toggle();
        y += 72;
    }

    void PageAi(Rect b)
    {
        var st = Store.Current;
        var ai = I.Ai;
        float y = b.Y;
        if (!st.AiEnabled)
        {
            var c = Row(ref y, b, "Yapay zekayı aç", "Özellikler sayfasından da açılır", 54);
            if (U.Toggle(Ui.Id("ai.on"), c, false)) { st.AiEnabled = true; Changed(); }
            return;
        }

        Section(ref y, b, "SAĞLAYICI");
        var provs = Providers.All;
        // sağlayıcı seçimi: dikey liste
        for (int i = 0; i < provs.Length; i++)
        {
            var p = provs[i];
            var r = new Rect(b.X, y, b.Width, 44);
            bool sel = st.AiProvider == p.Key;
            float hv = U.Anim(Ui.Id("ai.prov", i), U.Hit(r) ? 1 : 0);
            G.FillRound(r, 12, sel ? Theme.Accent.WithA(0.18f) : Theme.Rgb(0xFFFFFF, 0.03f + 0.03f * hv));
            if (sel) G.StrokeRound(r, 12, Theme.Accent.WithA(0.5f), 1);
            G.StrokeCircle(new Vector2(r.X + 20, r.CenterY), 6, sel ? Theme.Accent : Theme.Text3, 1.5f);
            if (sel) G.FillCircle(new Vector2(r.X + 20, r.CenterY), 3.2f, Theme.Accent);
            G.Text(p.Name, new Rect(r.X + 38, r.Y + 6, 240, 18), 13, Theme.Text, sel ? FontWeight.Bold : FontWeight.SemiBold);
            G.Text(p.Note, new Rect(r.X + 38, r.Y + 24, r.Width - 48, 14), 10.5f, p.Note.Contains("Ücretsiz") || p.Note.Contains("ücretsiz") ? Theme.Green : Theme.Text3, FontWeight.SemiBold);
            if (U.Click(Ui.Id("ai.prov", i), r) && !sel) { st.AiProvider = p.Key; Changed(); }
            y += 50;
        }

        var cur = Providers.Get(st.AiProvider);
        var prof = st.Ai();
        Section(ref y, b, "BAĞLANTI");
        if (cur.Key != "ollama")
        {
            var kr = Row(ref y, b, "API anahtarı", cur.KeyUrl.Length > 0 ? "Ücretsiz almak için: " + cur.KeyUrl : "Sağlayıcıdan al", 56);
            DrawKeyBox(new Rect(kr.X - 120, kr.Y, kr.Width + 120, 32), prof);
            if (cur.KeyUrl.Length > 0)
            {
                var gr = Row(ref y, b, "Anahtar sayfasını aç", cur.KeyUrl, 48);
                if (U.Pill(Ui.Id("ai.keyurl"), new Rect(gr.Right - 120, gr.Y, 120, 32), "Tarayıcıda aç", Theme.Surface, Theme.Text, Glyph.Folder))
                    Win32.ShellExecuteW(IntPtr.Zero, "open", cur.KeyUrl, null, null, Win32.SW_SHOWNORMAL);
            }
        }
        var mr = Row(ref y, b, "Model", "Boş bırakırsan: " + cur.DefaultModel, 56);
        DrawTextSetting(new Rect(mr.X - 60, mr.Y, mr.Width + 60, 32), _modelBox, prof.Model, v => { prof.Model = v; Changed(); }, cur.DefaultModel);
        if (cur.Key is "custom" or "ollama")
        {
            var br = Row(ref y, b, "Sunucu adresi", "OpenAI uyumlu /v1 adresi", 56);
            DrawTextSetting(new Rect(br.X - 60, br.Y, br.Width + 60, 32), _baseBox, prof.BaseUrl, v => { prof.BaseUrl = v; Changed(); }, cur.DefaultBase);
        }
        var tr = Row(ref y, b, "Bağlantıyı test et", _testResult.Length > 0 ? _testResult : "Seçili sağlayıcıya bir mesaj gönderir", 56);
        if (U.Pill(Ui.Id("ai.test"), new Rect(tr.Right - 120, tr.Y, 120, 32), _testing ? "Test ediliyor…" : "Test et", Theme.Accent, Theme.Rgb(0x0B140D)) && !_testing)
        {
            _testing = true; _testResult = "";
            ai.RunTest(r => { _testResult = r; _testing = false; _dirty = true; });
        }

        Section(ref y, b, "YETENEKLER");
        var w1 = Row(ref y, b, "Çalışma klasörü", st.AiWorkDir.Length > 0 ? st.AiWorkDir : "Masaüstü (varsayılan)", 56);
        if (U.Pill(Ui.Id("ai.wd"), new Rect(w1.Right - 120, w1.Y, 120, 32), "Klasör seç", Theme.Surface, Theme.Text, Glyph.Folder))
            Dispatcher.Post(() => { var p = AppsService.PickFile(Hwnd, "Çalışma klasöründen bir dosya seç"); if (p != null) { st.AiWorkDir = System.IO.Path.GetDirectoryName(p) ?? ""; Changed(); } });
        var c2 = Row(ref y, b, "İnternette arama yapabilsin", "web_search ve fetch_url araçları", 50);
        if (U.Toggle(Ui.Id("ai.web"), c2, st.AiWebSearch)) { st.AiWebSearch = !st.AiWebSearch; Changed(); }
        var c3 = Row(ref y, b, "Her yıkıcı işlemde onay iste", "Silme, dosya yazma ve komutları önce sana sorar (önerilir)", 50);
        if (U.Toggle(Ui.Id("ai.cfm"), c3, st.AiConfirm)) { st.AiConfirm = !st.AiConfirm; Changed(); }
        var c4 = Row(ref y, b, "Bilgisayarı kullanabilsin", "Ekranı görüp fare/klavye ile WhatsApp vb. uygulamaları kullanır (görüntü destekli model gerekir)", 56);
        if (U.Toggle(Ui.Id("ai.comp"), c4, st.AiComputerUse)) { st.AiComputerUse = !st.AiComputerUse; Changed(); }

        Section(ref y, b, "KISAYOL");
        var hk = Row(ref y, b, "Yapay zeka penceresi", I.AiHotkeyOk ? "Her yerden açar" : "⚠ Kısayol kullanımda", 54);
        int v = U.Segmented(Ui.Id("ai.hk"), hk, new[] { "Ctrl+Shift+Space", "Ctrl+Alt+A", "Kapalı" }, st.AiHotkey);
        if (v != st.AiHotkey) { st.AiHotkey = v; Changed(); }

        G.Text("Anahtarların yalnızca bu bilgisayarda saklanır. Ollama'yı seçersen hiç anahtar gerekmez ve her şey bilgisayarında çalışır.",
            new Rect(b.X, y + 6, b.Width, 36), 11, Theme.Text3, FontWeight.Normal, font: Theme.TextFont, wrap: true);
        _lastY = MathF.Max(_lastY, y + 40);
    }

    readonly TextBox _keyBox = new() { Placeholder = "sk-…" };
    readonly TextBox _modelBox = new();
    readonly TextBox _baseBox = new();
    TextBox? _focusField;
    int _focusFieldId;
    bool _keyVisible, _testing;
    string _testResult = "";

    void DrawKeyBox(Rect r, AiProfile prof)
    {
        int id = Ui.Id("ai.keybox");
        bool focused = _focusFieldId == id;
        if (!focused) _keyBox.Text = _keyVisible ? prof.ApiKey : new string('•', Math.Min(prof.ApiKey.Length, 32));
        var inner = new Rect(r.X, r.Y, r.Width - 76, r.Height);
        if (U.Click(id, inner)) { _focusField = _keyBox; _focusFieldId = id; _keyBox.Text = prof.ApiKey; _keyBox.Caret = _keyBox.Text.Length; _keyVisible = true; BeginKey(); }
        _keyBox.Placeholder = "API anahtarını yapıştır";
        _keyBox.Draw(U, id, inner, focused);
        if (focused) { prof.ApiKey = _keyBox.Text; }
        if (U.IconButton(Ui.Id("ai.keyshow"), new Vector2(r.Right - 56, r.CenterY), 14, _keyVisible ? "" : "", 12, Theme.Text2)) _keyVisible = !_keyVisible;
        if (U.IconButton(Ui.Id("ai.keypaste"), new Vector2(r.Right - 22, r.CenterY), 14, "", 12, Theme.Text2))
        {
            var clip = Services.ClipboardService.ReadText(Hwnd);
            if (!string.IsNullOrWhiteSpace(clip)) { prof.ApiKey = clip.Trim(); Changed(); }
        }
    }

    void DrawTextSetting(Rect r, TextBox box, string value, Action<string> set, string placeholder)
    {
        int id = Ui.Id("ai.txt", box.GetHashCode());
        bool focused = _focusFieldId == id;
        if (!focused) box.Text = value;
        if (U.Click(id, r)) { _focusField = box; _focusFieldId = id; box.Caret = box.Text.Length; BeginKey(); }
        box.Placeholder = placeholder;
        box.Draw(U, id, r, focused);
        if (focused && box.Text != value) set(box.Text);
    }

    void BeginKey()
    {
        _kbActive = true;
        int ex = Win32.GetWindowLongW(Hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLongW(Hwnd, Win32.GWL_EXSTYLE, ex & ~Win32.WS_EX_NOACTIVATE);
        Win32.SetForegroundWindow(Hwnd);
    }
    bool _kbActive;

    void PageDock(Rect b)
    {
        var st = Store.Current;
        float y = b.Y;
        Section(ref y, b, "GENEL");
        var c = Row(ref y, b, "Dock'u göster", "Ekranın altında MacBook tarzı uygulama çubuğu", 50);
        if (U.Toggle(Ui.Id("d.on"), c, st.DockEnabled)) { st.DockEnabled = !st.DockEnabled; Changed(); }
        c = Row(ref y, b, "Windows görev çubuğunu gizle", "Tamamen kaldırır; Ada kapanınca ya da bu kapatılınca geri gelir", 50);
        if (U.Toggle(Ui.Id("d.tb"), c, st.HideTaskbar)) { st.HideTaskbar = !st.HideTaskbar; Changed(); }

        Section(ref y, b, "GÖRÜNÜM");
        c = Row(ref y, b, "Stil", "Buzlu cam: duvar kağıdının bulanık yansıması", 50);
        int v = U.Segmented(Ui.Id("d.style"), c, new[] { "Buzlu cam", "Koyu", "Şeffaf" }, st.DockStyle);
        if (v != st.DockStyle) { st.DockStyle = v; Changed(); }
        float size = st.DockIconSize;
        if (SliderRow(ref y, b, "d.size", "Simge boyutu", $"{size:0} px", ref size, 32, 96)) { st.DockIconSize = MathF.Round(size); Changed(); }
        float sp = st.DockSpacing;
        if (SliderRow(ref y, b, "d.gap", "Simge aralığı", $"{sp:0} px", ref sp, 2, 24)) { st.DockSpacing = MathF.Round(sp); Changed(); }
        float rad = st.DockRadius;
        if (SliderRow(ref y, b, "d.rad", "Köşe yuvarlaklığı", $"{rad:0} px", ref rad, 6, 40)) { st.DockRadius = MathF.Round(rad); Changed(); }
        float op = st.DockOpacity;
        if (SliderRow(ref y, b, "d.op", "Arka plan yoğunluğu", $"%{op * 100:0}", ref op, 0.2f, 1f)) { st.DockOpacity = op; Changed(); }

        Section(ref y, b, "BÜYÜTME VE EFEKTLER");
        c = Row(ref y, b, "Büyütme", "Üzerine gelince simgeler macOS gibi büyür", 50);
        if (U.Toggle(Ui.Id("d.mag"), c, st.DockMagnify)) { st.DockMagnify = !st.DockMagnify; Changed(); }
        if (st.DockMagnify)
        {
            float mg = st.DockMagnification;
            if (SliderRow(ref y, b, "d.magv", "Büyütme oranı", $"{mg:0.00}×", ref mg, 1.1f, 2.4f)) { st.DockMagnification = mg; Changed(); }
        }
        c = Row(ref y, b, "Açılırken zıplama", "Uygulama başlarken simge zıplar", 50);
        if (U.Toggle(Ui.Id("d.bounce"), c, st.DockBounce)) { st.DockBounce = !st.DockBounce; Changed(); }
        c = Row(ref y, b, "Uygulama adları", "Üzerine gelince ad etiketi", 50);
        if (U.Toggle(Ui.Id("d.labels"), c, st.DockLabels)) { st.DockLabels = !st.DockLabels; Changed(); }
        c = Row(ref y, b, "Çalışma noktaları", "Açık uygulamaların altında nokta", 50);
        if (U.Toggle(Ui.Id("d.dots"), c, st.DockIndicators)) { st.DockIndicators = !st.DockIndicators; Changed(); }

        Section(ref y, b, "PENCERELER (DOCKDOOR)");
        c = Row(ref y, b, "Pencere önizlemeleri", "Simgenin üzerinde durunca pencereler görünür; tıkla → geç, × → kapat", 50);
        if (U.Toggle(Ui.Id("d.prev"), c, st.DockPreviews)) { st.DockPreviews = !st.DockPreviews; Changed(); }
        c = Row(ref y, b, "Açık uygulamaları göster", "Sabitlenmemiş açık uygulamalar ayraçtan sonra görünür", 50);
        if (U.Toggle(Ui.Id("d.run"), c, st.DockShowRunning)) { st.DockShowRunning = !st.DockShowRunning; Changed(); }
        c = Row(ref y, b, "Otomatik gizle", "Fare ekranın altına gelince belirir", 50);
        if (U.Toggle(Ui.Id("d.auto"), c, st.DockAutoHide)) { st.DockAutoHide = !st.DockAutoHide; Changed(); }
        c = Row(ref y, b, "Pencereler Dock'un üstünde dursun", "Büyütülmüş pencereler Dock'un arkasına girmez", 50);
        if (U.Toggle(Ui.Id("d.res"), c, st.DockReserve)) { st.DockReserve = !st.DockReserve; Changed(); }
    }

    void Section(ref float y, Rect b, string title)
    {
        G.Text(title, new Rect(b.X, y + 12, b.Width, 16), 10, Theme.Accent, FontWeight.Bold);
        y += 34;
        _lastY = MathF.Max(_lastY, y);
    }

    bool SliderRow(ref float y, Rect b, string key, string title, string value, ref float v, float min, float max)
    {
        var c = Row(ref y, b, title, value, 50);
        float t = (v - min) / (max - min);
        bool ch = U.SliderPlain(Ui.Id(key), new Rect(c.X + 10, c.Y, c.Width - 20, c.Height), ref t, Theme.Accent);
        if (ch) v = min + Math.Clamp(t, 0, 1) * (max - min);
        return ch;
    }
    void PageSearch(Rect b)
    {
        var st = Store.Current;
        float y = b.Y;
        string hkDesc = st.SearchHotkey switch
        {
            0 => "Ctrl+Space · not: VS Code'daki Ctrl+Space önerisini kullanamazsın",
            1 => "Alt+Space ile her yerden açılır",
            2 => "Windows tuşuna basınca Başlat yerine Uygulamalar (Launcher) açılır (Win+E gibi kombinasyonlar çalışmaya devam eder)",
            _ => "Kısayol kapalı — Dock'tan ya da imleç menüsünden açabilirsin",
        };
        var c = Row(ref y, b, "Kısayol", I.SearchHotkeyOk ? hkDesc : "⚠ Bu kısayol başka bir uygulamada kullanımda", 64);
        var hr = new Rect(c.X - 110, c.Y, c.Width + 110, c.Height);
        int v = U.Segmented(Ui.Id("q.hk"), hr, new[] { "Ctrl+Space", "Alt+Space", "Windows tuşu", "Kapalı" }, st.SearchHotkey);
        if (v != st.SearchHotkey) { st.SearchHotkey = v; Changed(); }
        c = Row(ref y, b, "Dosyaları ara", "Masaüstü, Belgeler, İndirilenler, Resimler, Müzik, Videolar", 58);
        if (U.Toggle(Ui.Id("q.files"), c, st.SearchFiles)) { st.SearchFiles = !st.SearchFiles; Changed(); }
        c = Row(ref y, b, "Web araması", "Son satırda “web'de ara” seçeneği", 58);
        if (U.Toggle(Ui.Id("q.web"), c, st.SearchWeb)) { st.SearchWeb = !st.SearchWeb; Changed(); }
        c = Row(ref y, b, "Şimdi dene", "Uygulama adı, dosya adı ya da 12*(3+4) gibi bir işlem yaz", 58);
        if (U.Pill(Ui.Id("q.try"), new Rect(c.Right - 160, c.Y, 160, 32), "Spotlight'ı aç", Theme.Accent, Theme.TileOnText, ""))
            Dispatcher.Post(I.ToggleSpotlight);
        G.Text("Fn+Space kullanılamaz: Fn tuşu klavyenin kendi donanımında işlenir, Windows'a hiç ulaşmaz.",
            new Rect(b.X, y + 14, b.Width, 16), 11, Theme.Text3, FontWeight.Normal, font: Theme.TextFont);
    }

    void PageRadial(Rect b)
    {
        var st = Store.Current;
        float y = b.Y;
        Section(ref y, b, "AÇMA");
        var c = Row(ref y, b, "Sağ tuşu basılı tut", "Kısa sağ tık normal çalışır; ~0,25 sn basılı tutunca daire açılır (tam ekran oyunda kapalı)", 54);
        if (U.Toggle(Ui.Id("r.hold"), c, st.RadialRightHold)) { st.RadialRightHold = !st.RadialRightHold; Changed(); }
        c = Row(ref y, b, "Bırakınca", st.RadialReleaseMode == 0 ? "Üzerinde olduğun öğe açılır; boşta bırakırsan kapanır" : "Daire açık kalır; öğeye tıkla, boş ekrana tıklayınca kapanır", 54);
        int v = U.Segmented(Ui.Id("r.rel"), c, new[] { "Üzerindekini aç", "Açık kalsın" }, st.RadialReleaseMode);
        if (v != st.RadialReleaseMode) { st.RadialReleaseMode = v; Changed(); }
        c = Row(ref y, b, "Kısayol", I.RadialHotkeyOk ? "Klavyeden de açılır" : "⚠ Bu kısayol başka uygulamada kullanımda", 54);
        v = U.Segmented(Ui.Id("r.hk"), c, new[] { "Alt+Q", "Ctrl+Alt+Q", "Kapalı" }, st.RadialHotkey);
        if (v != st.RadialHotkey) { st.RadialHotkey = v; Changed(); }
        c = Row(ref y, b, "Gezgin sağ tık menüsü", "\"Ada dairesine ekle\" ve \"Ada Dock'a sabitle\" (Diğer seçenekleri göster altında)", 54);
        if (U.Toggle(Ui.Id("r.shell"), c, st.ShellPinMenu)) { st.ShellPinMenu = !st.ShellPinMenu; Changed(); }

        Section(ref y, b, $"ÖĞELER ({st.RadialApps.Count}/12)");
        var list = st.RadialApps;
        if (list.Count == 0)
        {
            G.Text("Henüz öğe yok. Aşağıdan ekle, daire açıkken üzerine sürükle ya da Gezgin'de sağ tık → Ada dairesine ekle.",
                new Rect(b.X, y, b.Width, 36), 11.5f, Theme.Text3, FontWeight.Normal, font: Theme.TextFont, wrap: true);
            y += 40;
        }
        for (int i = 0; i < list.Count; i++)
        {
            var app = list[i];
            var r = new Rect(b.X, y, b.Width, 42);
            float hv = U.Anim(Ui.Id("r.row", app.Path.GetHashCode()), U.Hit(r) ? 1 : 0);
            G.FillRound(r, 12, Theme.Rgb(0xFFFFFF, 0.03f + 0.03f * hv));
            var bmp = I.Apps.Icon(app.Path, 32);
            var ir = new Rect(r.X + 12, r.CenterY - 13, 26, 26);
            if (bmp != null) G.Bitmap(bmp, ir); else { G.FillRound(ir, 7, Theme.SurfaceHi); G.Icon(Glyph.Apps, new Vector2(ir.CenterX, ir.CenterY), 11, Theme.Text); }
            G.Text($"{i + 1}.  {app.Name}", new Rect(r.X + 50, r.Y, r.Width - 220, r.Height), 13, Theme.Text, FontWeight.SemiBold);
            if (i > 0 && U.IconButton(Ui.Id("r.up", i), new Vector2(r.Right - 100, r.CenterY), 13, "", 10, Theme.Text2))
            { (list[i - 1], list[i]) = (list[i], list[i - 1]); Changed(); }
            if (i < list.Count - 1 && U.IconButton(Ui.Id("r.dn", i), new Vector2(r.Right - 70, r.CenterY), 13, "", 10, Theme.Text2))
            { (list[i + 1], list[i]) = (list[i], list[i + 1]); Changed(); }
            if (U.IconButton(Ui.Id("r.del", i), new Vector2(r.Right - 30, r.CenterY), 13, "", 10, Theme.Red))
            { list.RemoveAt(i); Changed(); break; }
            y += 48;
        }
        if (list.Count < 12)
        {
            if (U.Pill(Ui.Id("r.add"), new Rect(b.X, y + 4, 170, 32), "Uygulama ekle…", Theme.Accent, Theme.TileOnText, Glyph.Add))
                Dispatcher.Post(() => { var p = AppsService.PickFile(Hwnd, "Daireye eklenecek uygulamayı seç"); if (p != null) I.AddToRadial(p, toast: false); });
            if (U.Pill(Ui.Id("r.adddock"), new Rect(b.X + 182, y + 4, 200, 32), "Dock'takileri kopyala", Theme.Surface, Theme.Text))
            {
                foreach (var a in Store.Current.Apps) I.AddToRadial(a.Path, toast: false, name: a.Name);
                Changed();
            }
            y += 44;
        }
        _lastY = MathF.Max(_lastY, y);
    }

    void PageTabs(Rect b)
    {
        var st = Store.Current;
        float y = b.Y;
        for (int k = 0; k < st.TabOrder.Count; k++)
        {
            int t = st.TabOrder[k];
            var p = I.Panels[t];
            var r = new Rect(b.X, y, b.Width, 50);
            bool visible = !st.HiddenTabs.Contains(t);
            float hv = U.Anim(Ui.Id("s.tabrow", t), U.Hit(r) ? 1 : 0);
            G.FillRound(r, 14, Theme.Rgb(0xFFFFFF, 0.03f + 0.03f * hv));
            G.FillCircle(new Vector2(r.X + 26, r.CenterY), 15, Theme.Rgb(0xFFFFFF, 0.06f));
            G.Icon(p.Glyph, new Vector2(r.X + 26, r.CenterY), 13, visible ? Theme.Accent : Theme.Text3);
            G.Text($"{k + 1}.  {p.Name}", new Rect(r.X + 52, r.Y, 260, r.Height), 13.5f, visible ? Theme.Text : Theme.Text3, FontWeight.SemiBold);
            if (k > 0 && U.IconButton(Ui.Id("s.up", t), new Vector2(r.Right - 140, r.CenterY), 14, "", 11, Theme.Text2))
            { (st.TabOrder[k - 1], st.TabOrder[k]) = (st.TabOrder[k], st.TabOrder[k - 1]); Changed(); }
            if (k < st.TabOrder.Count - 1 && U.IconButton(Ui.Id("s.down", t), new Vector2(r.Right - 108, r.CenterY), 14, "", 11, Theme.Text2))
            { (st.TabOrder[k + 1], st.TabOrder[k]) = (st.TabOrder[k], st.TabOrder[k + 1]); Changed(); }
            if (U.Toggle(Ui.Id("s.vis", t), new Rect(r.Right - 66, r.Y, 54, r.Height), visible))
            {
                if (visible && st.VisibleTabs().Count > 1) st.HiddenTabs.Add(t);
                else st.HiddenTabs.Remove(t);
                Changed();
            }
            y += 56;
        }
        G.Text("En az bir sekme görünür kalmalı · Açıkken 1–7 tuşları bu sırayla çalışır", new Rect(b.X, y + 4, b.Width, 16), 11, Theme.Text3, FontWeight.Normal, font: Theme.TextFont);
    }

    void PageGeneral(Rect b)
    {
        var st = Store.Current;
        float y = b.Y;
        var c = Row(ref y, b, "Kısayol tuşu", I.HotkeyOk ? $"{Island.HotkeyNames[st.Hotkey]} · adayı her yerden açar" : "⚠ Bu kısayol başka uygulamada kullanımda", 64);
        int v = U.Segmented(Ui.Id("s.hk"), c, new[] { "Ctrl+Alt", "Ctrl+Shift", "Alt+Shift", "Kapalı" }, st.Hotkey);
        if (v != st.Hotkey) { st.Hotkey = v; Changed(); }

        c = Row(ref y, b, "Windows ile başlat", "Oturum açınca Ada otomatik çalışsın", 60);
        bool startup = AppsService.StartupEnabled;
        if (U.Toggle(Ui.Id("s.startup"), c, startup)) { AppsService.StartupEnabled = !startup; _dirty = true; }

        c = Row(ref y, b, "Veri klasörü", Store.Dir, 60);
        if (U.Pill(Ui.Id("s.folder"), new Rect(c.Right - 140, c.Y, 140, 32), "Klasörü aç", Theme.Surface, Theme.Text, Glyph.Folder))
        { Directory.CreateDirectory(Store.Dir); Win32.ShellExecuteW(IntPtr.Zero, "open", Store.Dir, null, null, Win32.SW_SHOWNORMAL); }

        if (_confirmReset && DateTime.Now > _confirmUntil) _confirmReset = false;
        c = Row(ref y, b, "Ayarları sıfırla", "Görevlerin ve uygulamaların korunur", 60);
        if (U.Pill(Ui.Id("s.reset"), new Rect(c.Right - 180, c.Y, 180, 32), _confirmReset ? "Emin misin? Tekrar tıkla" : "Varsayılana döndür",
                _confirmReset ? Theme.Red.WithA(0.25f) : Theme.Surface, _confirmReset ? Theme.Red : Theme.Text))
        {
            if (_confirmReset) { st.ResetSettings(); _confirmReset = false; Changed(); }
            else { _confirmReset = true; _confirmUntil = DateTime.Now.AddSeconds(3); }
        }

        c = Row(ref y, b, "Ada'dan çık", "Adayı kapatır (Başlat menüsünden yeniden açabilirsin)", 60);
        if (U.Pill(Ui.Id("s.quit"), new Rect(c.Right - 140, c.Y, 140, 32), "Çık", Theme.Red.WithA(0.18f), Theme.Red))
        { Store.Save(); Win32.PostQuitMessage(0); }
    }

    void PageAbout(Rect b)
    {
        var card = new Rect(b.X, b.Y, b.Width, 120);
        G.VGradientRound(card, 18, Theme.Rgb(0x1A1F2E), Theme.Rgb(0x10131C));
        G.Glow(new Vector2(card.X + 90, card.CenterY), 200, 110, Theme.Accent.WithA(0.25f), card, 18);
        var pill = new Rect(card.X + 28, card.CenterY - 16, 110, 32);
        G.FillRound(pill, 16, Theme.Rgb(0x000000));
        G.StrokeRound(pill, 16, Theme.Rgb(0xFFFFFF, 0.16f), 1);
        G.FillCircle(new Vector2(pill.X + 18, pill.CenterY), 4, Theme.Accent);
        G.Text(DateTime.Now.ToString("HH:mm"), pill, 13, Theme.Text, FontWeight.SemiBold, TextAlignment.Center);
        G.Text("Ada", new Rect(card.X + 160, card.Y + 30, 300, 32), 24, Theme.Text, FontWeight.Bold);
        G.Text("Windows için dynamic island · sürüm 1.1", new Rect(card.X + 160, card.Y + 62, 400, 18), 12, Theme.Text2, FontWeight.Normal, font: Theme.TextFont);

        // canlı kaynak kullanımı
        var pmc = new PROCESS_MEMORY_COUNTERS { cb = (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS>() };
        Win32.K32GetProcessMemoryInfo(Win32.GetCurrentProcess(), out pmc, (int)pmc.cb);
        float y = card.Bottom + 18;
        Stat(new Rect(b.X, y, (b.Width - 24) / 3, 70), "BELLEK (ÖZEL)", StatsService.Bytes(pmc.PagefileUsage));
        Stat(new Rect(b.X + (b.Width - 24) / 3 + 12, y, (b.Width - 24) / 3, 70), "ÇALIŞMA KÜMESİ", StatsService.Bytes(pmc.WorkingSetSize));
        Stat(new Rect(b.X + ((b.Width - 24) / 3 + 12) * 2, y, (b.Width - 24) / 3, 70), "ÇALIŞMA SÜRESİ", Uptime());

    }

    void Stat(Rect r, string label, string value)
    {
        G.FillRound(r, 14, Theme.Rgb(0xFFFFFF, 0.04f));
        G.Text(label, new Rect(r.X + 14, r.Y + 12, r.Width - 20, 14), 9.5f, Theme.Text3, FontWeight.Bold);
        G.Text(value, new Rect(r.X + 14, r.Y + 30, r.Width - 20, 28), 18, Theme.Text, FontWeight.Bold);
    }

    static string Uptime()
    {
        var t = DateTime.Now - Process.GetCurrentProcess().StartTime;
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours} sa {t.Minutes} dk" : $"{t.Minutes} dk {t.Seconds} sn";
    }
}
