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
/// macOS Spotlight tarzı arama: Ctrl+Space (ayarlanabilir) ile ekranın ortasında açılır.
/// Uygulamalar, dosyalar, hesap makinesi, Ada eylemleri ve web araması.
/// </summary>
public sealed unsafe class SpotlightWindow : ISurface
{
    const float W = 700, InputH = 62, RowH = 46, MaxRows = 8, M = 30, R = 20;
    static bool _registered;

    readonly Island I;
    Gfx G => I.G;
    readonly Ui U;
    public IntPtr Hwnd { get; private set; }
    public bool Animating { get; private set; }
    bool _dirty = true, _open, _shown;
    readonly Stopwatch _sw = Stopwatch.StartNew();
    double _last;
    Smooth _vis = new(0, 16);
    Spring _height = new(InputH, 420, 36);
    Spring _selY = new(0, 520, 40);
    readonly TextBox _box = new() { Placeholder = "Spotlight'ta ara" };
    List<Item> _items = new();
    int _sel;
    int _pw, _ph;

    sealed record Item(string Title, string Sub, string Kind, string? IconPath, string Glyph, Action Run, string? Key = null);

    public SpotlightWindow(Island island)
    {
        I = island;
        U = new Ui(island.G);
        var inst = Win32.GetModuleHandleW(IntPtr.Zero);
        if (!_registered)
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Island.ProcPtr, hInstance = inst,
                hCursor = Win32.LoadCursorW(IntPtr.Zero, 32512), lpszClassName = Marshal.StringToHGlobalUni("AdaSpotlight")
            };
            Win32.RegisterClassExW(ref wc);
            _registered = true;
        }
        float s = I.Scale;
        _pw = (int)MathF.Ceiling((W + M * 2) * s);
        _ph = (int)MathF.Ceiling((InputH + RowH * MaxRows + 24 + M * 2) * s);
        var mon = I.MonitorRect;
        int x = (mon.Left + mon.Right - _pw) / 2, y = mon.Top + (int)((mon.Bottom - mon.Top) * 0.2f);
        Hwnd = Win32.CreateWindowExW(Win32.WS_EX_LAYERED | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST, "AdaSpotlight", "Ada Spotlight",
            Win32.WS_POPUP, x, y, _pw, _ph, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
    }

    public bool IsOpen => _open;

    /// <summary>--snap: verilen sorguyla açık hali PNG'ye çizer.</summary>
    public void SnapRender(string query, string path)
    {
        _open = true; _shown = true; _box.Text = query; _box.Caret = query.Length;
        I.AppIndex.EnsureLoaded(); I.Files.EnsureLoaded();
        Rebuild();
        for (int i = 0; i < 90; i++) Frame();
        G.SavePng(path, _pw, _ph);
    }

    public void Toggle() { if (_open) Hide(); else Show(); }

    public void Show()
    {
        _open = true;
        _box.Clear();
        _sel = 0; _selY.Snap(0);
        I.AppIndex.EnsureLoaded();
        if (Store.Current.SearchFiles) I.Files.EnsureLoaded();
        Rebuild();
        _height.Snap(InputH);
        if (!_shown) { Win32.ShowWindow(Hwnd, 5); _shown = true; }
        Win32.SetForegroundWindow(Hwnd);
        _dirty = true; Animating = true;
    }

    public void Hide()
    {
        if (!_open) return;
        _open = false;
        _dirty = true; Animating = true;
    }

    public void Invalidate() => _dirty = true;
    public void FrameIfNeeded() { if (_shown && (_dirty || Animating)) Frame(); }

    public void Tick()
    {
        I.Files.Trim();
        if (_open && (I.AppIndex.All.Count == 0 || I.Files.Loading)) { Rebuild(); _dirty = true; }
    }

    // ---------------- sonuçlar ----------------
    static int Count(string key) => Store.Current.LaunchCounts.TryGetValue(key, out var n) ? n : 0;

    Item AppItem(string name, string path, string sub) =>
        new(name, sub, "Uygulama", path, Glyph.Apps, () => AppsService.Launch(new PinnedApp { Name = name, Path = path }), path);

    static void Open(string target) => Win32.ShellExecuteW(IntPtr.Zero, "open", target, null, null, Win32.SW_SHOWNORMAL);

    void Rebuild()
    {
        var q = _box.Text.Trim();
        var items = new List<Item>();
        var tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
        var counts = Store.Current.LaunchCounts;
        if (q.Length == 0)
        {
            // Launchy gibi: en sık açtıkların; henüz yoksa sabitlenenler
            foreach (var kv in counts.OrderByDescending(kv => kv.Value).Take(7))
            {
                var app = I.AppIndex.All.FirstOrDefault(a => a.Path.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
                if (app != null) items.Add(AppItem(app.Name, app.Path, $"Sık kullanılan · {kv.Value} kez"));
                else if (File.Exists(kv.Key) || Directory.Exists(kv.Key))
                    items.Add(new Item(Path.GetFileName(kv.Key), $"Sık kullanılan · {kv.Value} kez", "Dosya", kv.Key, Glyph.Folder, () => Open(kv.Key), kv.Key));
            }
            if (items.Count == 0)
                foreach (var a in Store.Current.Apps.Take(6))
                    items.Add(new Item(a.Name, "Sabitlenmiş", "Uygulama", a.Path, Glyph.Apps, () => AppsService.Launch(a), a.Path));
            _items = items; Clamp(); return;
        }

        // önekler: g / yt / w / > / adres
        var (kind, arg) = LaunchQuery.Parse(q);
        switch (kind)
        {
            case LaunchQuery.Kind.Google: items.Add(new Item($"Google: {arg}", "Enter: tarayıcıda ara", "Web", null, "", () => Open(LaunchQuery.UrlFor(kind, arg)))); break;
            case LaunchQuery.Kind.YouTube: items.Add(new Item($"YouTube: {arg}", "Enter: YouTube'da ara", "Web", null, "", () => Open(LaunchQuery.UrlFor(kind, arg)))); break;
            case LaunchQuery.Kind.Wikipedia: items.Add(new Item($"Vikipedi: {arg}", "Enter: Vikipedi'de ara", "Web", null, "", () => Open(LaunchQuery.UrlFor(kind, arg)))); break;
            case LaunchQuery.Kind.Url: items.Add(new Item(arg, "Enter: adresi aç", "Adres", null, "", () => Open(arg))); break;
            case LaunchQuery.Kind.Command:
                if (arg.Length > 0)
                    items.Add(new Item($"Çalıştır: {arg}", "Komut istemi açılır ve komut çalışır", "Komut", null, "", () =>
                        Win32.ShellExecuteW(IntPtr.Zero, "open", "cmd.exe", "/k " + arg, null, Win32.SW_SHOWNORMAL)));
                _items = items; Clamp(); return;
        }

        if (Calc.TryEval(q, out var v))
        {
            var res = Calc.Format(v);
            items.Add(new Item("= " + res, q + "  ·  Enter: kopyala", "Hesap", null, "", () =>
            {
                I.Clip.SetText(res.Replace(".", ""));
                I.Toast("Kopyalandı", "= " + res, Glyph.Copy, Theme.Accent, 3);
            }));
        }

        // uygulamalar: eşleşme puanı + kullanım sıklığı (Launchy'nin öğrenen sıralaması)
        var apps = new List<(AppIndex.Entry e, float score)>();
        foreach (var e in I.AppIndex.All)
        {
            int sc = AppIndex.Score(e.Name, q, tr);
            if (sc > 0) apps.Add((e, sc * 10 + MathF.Min(Count(e.Path), 40) / 4f));
        }
        foreach (var (e, _) in apps.OrderByDescending(x => x.score).ThenBy(x => x.e.Name.Length).Take(6))
            items.Add(AppItem(e.Name, e.Path, Count(e.Path) > 0 ? $"{e.Kind} · {Count(e.Path)} kez açıldı" : e.Kind));

        (string name, string glyph, Action run)[] actions =
        {
            ("Ada Ayarları", Glyph.Settings, () => I.OpenSettings()),
            ("Ekran alıntısı", "", () => ControlsService.OpenSettings("ms-screenclip:")),
            ("Ekranı kilitle", Glyph.Lock, ControlsService.Lock),
            ("Windows Ayarları", Glyph.Settings, () => ControlsService.OpenSettings()),
            ("Pano geçmişi", Glyph.Clipboard, () => I.Expand(3)),
            ("Duvar kağıdı", Glyph.Theme, () => I.Expand(5)),
            ("Dock ayarları", "", () => I.OpenSettings(SettingsWindow.DockPage)),
        };
        foreach (var (name, glyph, run) in actions)
            if (AppIndex.Score(name, q, tr) > 0) items.Add(new Item(name, "Ada eylemi", "Eylem", null, glyph, run));
        if (Store.Current.SearchFiles)
            foreach (var f in I.Files.Search(q, 12).OrderByDescending(f => Count(f.Path)).Take(5))
                items.Add(new Item(f.Name, f.Folder, Directory.Exists(f.Path) ? "Klasör" : "Dosya", f.Path, Glyph.Folder, () => Open(f.Path), f.Path));
        if (Store.Current.SearchWeb && kind == LaunchQuery.Kind.None)
            items.Add(new Item($"“{q}” için web'de ara", "g, yt, w önekleriyle de arayabilirsin", "Web", null, "", () =>
                Open(LaunchQuery.UrlFor(LaunchQuery.Kind.Google, q))));
        _items = items.Take((int)MaxRows).ToList();
        Clamp();
    }

    void Clamp() { _sel = Math.Clamp(_sel, 0, Math.Max(0, _items.Count - 1)); }

    void RunSelected()
    {
        if (_sel >= _items.Count) return;
        var it = _items[_sel];
        if (it.Key != null)
        {
            var c = Store.Current.LaunchCounts;
            c[it.Key] = (c.TryGetValue(it.Key, out var n) ? n : 0) + 1;
            Store.Save();
        }
        Hide();
        Dispatcher.Post(it.Run);
    }
    // ---------------- mesajlar ----------------
    public IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l)
    {
        const uint WM_SETCURSOR = 0x20;
        switch (m)
        {
            case Win32.WM_MOUSEMOVE:
                U.Mouse = new Vector2(Win32.LoWord(l) / I.Scale - M, Win32.HiWord(l) / I.Scale - M);
                U.Inside = true; _dirty = true; return 0;
            case Win32.WM_LBUTTONDOWN:
                U.Down = true; U.Pressed = true; _dirty = true; return 0;
            case Win32.WM_LBUTTONUP:
                U.Down = false; U.Released = true; _dirty = true; return 0;
            case Win32.WM_MOUSEWHEEL:
                _sel = Math.Clamp(_sel - Math.Sign(Win32.HiWord(w)), 0, Math.Max(0, _items.Count - 1)); _dirty = true; return 0;
            case WM_SETCURSOR:
                if (U.WantHand && Win32.LoWord(l) == 1) { Win32.SetCursor(Win32.LoadCursorW(IntPtr.Zero, 32649)); return 1; }
                break;
            case Win32.WM_ACTIVATE:
                if (Win32.LoWord(w) == 0) Hide(); // başka yere tıklanınca kapan
                return 0;
            case Win32.WM_CHAR:
                if (_box.OnChar((char)w)) { _sel = 0; Rebuild(); }
                _dirty = true; return 0;
            case Win32.WM_KEYDOWN:
            case Win32.WM_SYSKEYDOWN:
            {
                int vk = (int)w;
                bool ctrl = Win32.GetKeyState(0x11) < 0;
                if (vk == 0x1B) { if (_box.Text.Length > 0) { _box.Clear(); Rebuild(); } else Hide(); }
                else if (vk == 0x28) _sel = Math.Min(_items.Count - 1, _sel + 1);
                else if (vk == 0x26) _sel = Math.Max(0, _sel - 1);
                else if (vk == 0x0D) RunSelected();
                else
                {
                    var before = _box.Text;
                    _box.OnKey(vk, ctrl, () => ClipboardService.ReadText(Hwnd));
                    if (before != _box.Text) { _sel = 0; Rebuild(); }
                }
                _dirty = true; return 0;
            }
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

        _vis.Target = _open ? 1 : 0; _vis.Speed = _open ? 16 : 22; _vis.Step(dt);
        float listH = _items.Count == 0 ? 0 : _items.Count * RowH + 16;
        _height.Target = InputH + listH; _height.Step(dt);
        _selY.Target = _sel * RowH; _selY.Step(dt);

        if (G.InDraw) { _dirty = true; return; }
        G.Begin(_pw, _ph);
        float a = Ease.OutCubic(_vis.Value);
        try
        {
            G.Alpha = a;
            // açılış: hafif büyüyerek ve aşağı kayarak gelir
            G.PushTranslate(M, M - 10 * (1 - a), 0.96f + 0.04f * a, new Vector2(W / 2, 0));
            Draw();
            G.PopTransform();
        }
        finally { G.End(); }
        G.PresentInPlace(Hwnd, _pw, _ph);

        Animating = U.NeedsFrame || !_vis.Settled || !_height.Settled || !_selY.Settled;
        _dirty = false;
        U.EndFrame();
        if (!_open && _vis.Value < 0.01f && _shown)
        {
            Win32.ShowWindow(Hwnd, 0); _shown = false; Animating = false;
            if (I.Apps.IconCount > 120) I.Apps.ClearIconCache();
        }
    }

    void Draw()
    {
        float h = _height.Value;
        var panel = new Rect(0, 0, W, h);
        G.Shadow(panel, R, 22, 0.6f, 10);
        G.FillRound(panel, R, Theme.Rgb(0x161922, 0.97f));
        G.VGradientRound(new Rect(0, 0, W, MathF.Min(h, 90)), R, Theme.Rgb(0xFFFFFF, 0.06f), Theme.Rgb(0xFFFFFF, 0));
        G.StrokeRound(new Rect(0.5f, 0.5f, W - 1, h - 1), R, Theme.Rgb(0xFFFFFF, 0.12f), 1);

        // arama satırı
        G.Icon("", new Vector2(30, InputH / 2), 17, Theme.Text2);
        var tr = new Rect(56, 0, W - 140, InputH);
        if (_box.Text.Length == 0) G.Text(_box.Placeholder, tr, 21, Theme.Text3, FontWeight.Normal, font: Theme.TextFont);
        float caretX = G.Measure(_box.Text[.._box.Caret], 21, FontWeight.Normal, Theme.TextFont);
        float off = MathF.Max(0, caretX - (tr.Width - 8));
        G.PushClip(tr);
        G.Text(_box.Text, new Rect(tr.X - off, tr.Y, 4000, tr.Height), 21, Theme.Text, FontWeight.Normal, font: Theme.TextFont);
        float blink = ((int)(U.Time / 0.53f)) % 2 == 0 ? 1 : 0.15f;
        G.Line(new Vector2(tr.X - off + caretX + 1, InputH / 2 - 13), new Vector2(tr.X - off + caretX + 1, InputH / 2 + 13), Theme.Accent.WithA(blink), 2);
        G.PopClip();
        if (_open) U.NeedsFrame = true; // imleç yanıp söner (pencere yalnızca açıkken)
        if (I.AppIndex.All.Count == 0 || I.Files.Loading)
            G.Text("dizinleniyor…", new Rect(W - 140, 0, 120, InputH), 11, Theme.Text3, FontWeight.SemiBold, TextAlignment.Trailing);

        if (_items.Count == 0 || h < InputH + 10) return;
        G.Line(new Vector2(0, InputH), new Vector2(W, InputH), Theme.Rgb(0xFFFFFF, 0.08f), 1);
        var list = new Rect(8, InputH + 8, W - 16, h - InputH - 16);
        G.PushClip(new Rect(0, InputH, W, h - InputH));
        G.FillRound(new Rect(list.X, list.Y + _selY.Value, list.Width, RowH - 4), 11, Theme.Accent.WithA(0.9f));
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            var r = new Rect(list.X, list.Y + i * RowH, list.Width, RowH - 4);
            if (U.Hit(r))
            {
                U.WantHand = true;
                if (U.Pressed) _sel = i;
                if (U.Released && _sel == i) { RunSelected(); return; }
            }
            bool sel = i == _sel;
            var fg = sel ? Theme.TileOnText : Theme.Text;
            var ir = new Rect(r.X + 12, r.CenterY - 13, 26, 26);
            var bmp = it.IconPath != null ? I.Apps.Icon(it.IconPath, 32) : null;
            if (bmp != null) G.Bitmap(bmp, ir);
            else { G.FillRound(ir, 7, sel ? Theme.Rgb(0x000000, 0.12f) : Theme.Rgb(0xFFFFFF, 0.08f)); G.Icon(it.Glyph, new Vector2(ir.CenterX, ir.CenterY), 12, fg); }
            G.Text(it.Title, new Rect(r.X + 50, r.Y + 3, r.Width - 190, 22), 14, fg, FontWeight.SemiBold);
            G.Text(it.Sub, new Rect(r.X + 50, r.Y + 22, r.Width - 190, 16), 11, fg.WithA(0.6f), FontWeight.Normal, font: Theme.TextFont);
            G.Text(it.Kind, new Rect(r.Right - 130, r.Y, 116, r.Height), 11, fg.WithA(sel ? 0.75f : 0.45f), FontWeight.SemiBold, TextAlignment.Trailing);
        }
        G.PopClip();
    }
}
