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

    static readonly string[] Pages = { "Görünüm", "Boşta", "Davranış", "Dock", "Arama", "Sekmeler", "Genel", "Hakkında" };
    public const int DockPage = 3;
    static readonly string[] PageGlyphs = { Glyph.Theme, Glyph.Clock, Glyph.Sliders, "", "", Glyph.Apps, Glyph.Settings, "" };
    static readonly string[] PageSubs =
    {
        "Adanın şekli, boyutu ve rengi",
        "Kapalıyken solda, ortada ve sağda ne görünsün",
        "Açılma, kapanma ve bildirimler",
        "MacBook tarzı alt çubuk ve Windows görev çubuğu",
        "Spotlight: uygulama, dosya, hesap makinesi, web",
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
            case Win32.WM_KEYDOWN:
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
        G.Begin(pw, ph);
        G.PushTranslate(M, M);
        Draw();
        G.PopTransform();
        G.End();
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
        switch (p)
        {
            case 0: PageLook(body); break;
            case 1: PageIdle(body); break;
            case 2: PageBehavior(body); break;
            case 3: PageDock(body); break;
            case 4: PageSearch(body); break;
            case 5: PageTabs(body); break;
            case 6: PageGeneral(body); break;
            case 7: PageAbout(body); break;
        }
    }

    // ---------- ortak satır ----------
    Rect Row(ref float y, Rect body, string title, string? desc, float h = 56)
    {
        var r = new Rect(body.X, y, body.Width, h);
        G.Text(title, new Rect(r.X, r.Y + (desc == null ? 0 : 8), r.Width - 320, desc == null ? h : 20), 13.5f, Theme.Text, FontWeight.SemiBold);
        if (desc != null) G.Text(desc, new Rect(r.X, r.Y + 28, r.Width - 320, 18), 11.5f, Theme.Text3, FontWeight.Normal, font: Theme.TextFont);
        G.Line(new Vector2(r.X, r.Bottom), new Vector2(r.Right, r.Bottom), Theme.Rgb(0xFFFFFF, 0.05f), 1);
        y += h;
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

    void PageDock(Rect b)
    {
        var st = Store.Current;
        float y = b.Y;
        var c = Row(ref y, b, "Dock'u göster", "Ekranın altında MacBook tarzı uygulama çubuğu", 54);
        if (U.Toggle(Ui.Id("d.on"), c, st.DockEnabled)) { st.DockEnabled = !st.DockEnabled; Changed(); }
        c = Row(ref y, b, "Windows görev çubuğunu gizle", "Tamamen kaldırır; Ada kapanınca ya da bu kapatılınca geri gelir", 54);
        if (U.Toggle(Ui.Id("d.tb"), c, st.HideTaskbar)) { st.HideTaskbar = !st.HideTaskbar; Changed(); }
        c = Row(ref y, b, "Boyut", "Dock simgelerinin büyüklüğü", 54);
        int v = U.Segmented(Ui.Id("d.size"), c, new[] { "Küçük", "Orta", "Büyük" }, st.DockSize);
        if (v != st.DockSize) { st.DockSize = v; Changed(); }
        c = Row(ref y, b, "Büyütme", "Üzerine gelince simgeler macOS gibi büyür", 54);
        if (U.Toggle(Ui.Id("d.mag"), c, st.DockMagnify)) { st.DockMagnify = !st.DockMagnify; Changed(); }
        c = Row(ref y, b, "Otomatik gizle", "Fare ekranın altına gelince belirir", 54);
        if (U.Toggle(Ui.Id("d.auto"), c, st.DockAutoHide)) { st.DockAutoHide = !st.DockAutoHide; Changed(); }
        c = Row(ref y, b, "Pencereler Dock'un üstünde dursun", "Büyütülmüş pencereler Dock'un arkasına girmez", 54);
        if (U.Toggle(Ui.Id("d.res"), c, st.DockReserve)) { st.DockReserve = !st.DockReserve; Changed(); }
        c = Row(ref y, b, "Açık uygulamaları göster", "Sabitlenmemiş açık uygulamalar ayraçtan sonra görünür", 54);
        if (U.Toggle(Ui.Id("d.run"), c, st.DockShowRunning)) { st.DockShowRunning = !st.DockShowRunning; Changed(); }
    }

    void PageSearch(Rect b)
    {
        var st = Store.Current;
        float y = b.Y;
        var c = Row(ref y, b, "Kısayol", I.SearchHotkeyOk
            ? (st.SearchHotkey == 0 ? "Ctrl+Space · not: VS Code'daki Ctrl+Space önerisini kullanamazsın" : "Spotlight'ı her yerden açar")
            : "⚠ Bu kısayol başka bir uygulamada kullanımda", 64);
        int v = U.Segmented(Ui.Id("q.hk"), c, new[] { "Ctrl+Space", "Alt+Space", "Kapalı" }, st.SearchHotkey);
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

        y += 92;
        G.Text("YAKINDA", new Rect(b.X, y, 200, 16), 10, Theme.Text3, FontWeight.Bold);
        string[] road =
        {
            "Dock'ta pencere önizlemeleri ve klasör yığınları",
            "Üst menü çubuğu (macOS tarzı, uygulama menüleriyle)",
            "Hava durumu, pano görselleri, takvim senkronu",
            "NativeAOT derlemesi (daha da az RAM)"
        };
        for (int i = 0; i < road.Length; i++)
        {
            G.FillCircle(new Vector2(b.X + 6, y + 32 + i * 26), 3, Theme.Accent);
            G.Text(road[i], new Rect(b.X + 18, y + 22 + i * 26, b.Width - 20, 20), 12.5f, Theme.Text2, FontWeight.Normal, font: Theme.TextFont);
        }
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
