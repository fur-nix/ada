using System.Numerics;
using Ada.Core;
using Ada.Data;
using Ada.Services;
using Vortice.DirectWrite;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Panels;

/// <summary>Uygulamalar: arama + sabitlenenler + Başlat menüsündeki tüm uygulamalar.</summary>
public sealed class AppsPanel : Panel
{
    readonly TextBox _search = new() { Placeholder = "Uygulama ara…" };
    static readonly int SearchId = Ui.Id("apps.search");
    Spring _scroll = new(0, 260, 32);
    int _sel;

    public AppsPanel(Island i) : base(i) { }

    public override string Name => "Uygulamalar";
    public override string Glyph => Core.Glyph.Apps;

    public override void OnShow()
    {
        I.AppIndex.EnsureLoaded();
        _scroll.Snap(0); _sel = 0;
        // arama kutusuna hemen yazılabilsin (Spotlight hissi)
        if (I.KeyboardMode) Focus(_search, SearchId);
    }

    List<AppIndex.Entry> Results() => AppIndex.Search(I.AppIndex.All, _search.Text, 60);

    protected override void OnTextChanged(TextBox box) { _sel = 0; _scroll.Target = 0; }
    protected override void OnSubmit(TextBox box) { var r = Results(); if (_sel < r.Count) Launch(r[_sel].Path, r[_sel].Name); }

    public override bool OnKey(int vk, bool ctrl)
    {
        if (vk == 0x28) { _sel = Math.Min(Results().Count - 1, _sel + 1); return true; }
        if (vk == 0x26) { _sel = Math.Max(0, _sel - 1); return true; }
        return base.OnKey(vk, ctrl);
    }

    void Launch(string path, string name)
    {
        Dispatcher.Post(() => AppsService.Launch(new PinnedApp { Name = name, Path = path }));
        _search.Clear();
        I.Collapse();
    }

    public override void Draw(Rect a)
    {
        Box(_search, SearchId, new Rect(a.X, a.Y, a.Width, 34));
        G.Text("Ctrl+Alt+Space", new Rect(a.Right - 130, a.Y, 116, 34), 10.5f, Theme.Text3, FontWeight.SemiBold, TextAlignment.Trailing);

        float y = a.Y + 44;
        bool searching = _search.Text.Trim().Length > 0;
        if (!searching)
        {
            G.Text("SABİTLENENLER", new Rect(a.X + 4, y, 200, 14), 9.5f, Theme.Text3, FontWeight.Bold);
            DrawPinned(new Rect(a.X, y + 16, a.Width, 64));
            y += 88;
        }
        G.Text(searching ? "SONUÇLAR" : "TÜM UYGULAMALAR", new Rect(a.X + 4, y, 200, 14), 9.5f, Theme.Text3, FontWeight.Bold);
        var list = new Rect(a.X, y + 16, a.Width, a.Bottom - y - 16);
        DrawList(list);
    }

    void DrawPinned(Rect r)
    {
        var apps = Store.Current.Apps;
        const float cw = 74;
        PinnedApp? remove = null;
        for (int i = 0; i <= apps.Count && i * cw < r.Width; i++)
        {
            var cell = new Rect(r.X + i * cw, r.Y, cw - 6, r.Height);
            bool isAdd = i == apps.Count;
            int id = Ui.Id("pin", isAdd ? -1 : apps[i].Path.GetHashCode());
            bool clicked = U.Click(id, cell);
            float h = U.Anim(id, U.Hit(cell) ? 1 : 0, 18);
            if (h > 0.01f) G.FillRound(cell, 14, Theme.Surface.WithA(h * 1.4f));
            var ic = new Rect(cell.CenterX - 16, cell.Y + 6 - 2 * h, 32, 32);
            if (isAdd)
            {
                G.FillRound(ic, 10, Theme.Rgb(0xFFFFFF, 0.06f));
                G.Icon(Core.Glyph.Add, new Vector2(ic.CenterX, ic.CenterY), 13, Theme.Accent);
                G.Text("Ekle", new Rect(cell.X, cell.Y + 42, cell.Width, 16), 10.5f, Theme.Text2, FontWeight.SemiBold, TextAlignment.Center);
                if (clicked) Dispatcher.Post(AddApp);
                continue;
            }
            var app = apps[i];
            var bmp = I.Apps.Icon(app.Path);
            if (bmp != null) G.Bitmap(bmp, ic);
            else { G.FillRound(ic, 10, Theme.SurfaceHi); G.Icon(app.Path.StartsWith("ms-settings") ? Core.Glyph.Settings : Core.Glyph.Apps, new Vector2(ic.CenterX, ic.CenterY), 13, Theme.Text); }
            G.Text(app.Name, new Rect(cell.X + 2, cell.Y + 42, cell.Width - 4, 16), 10.5f, Theme.Mix(Theme.Text2, Theme.Text, h), FontWeight.SemiBold, TextAlignment.Center);
            if (clicked) Launch(app.Path, app.Name);
            if (U.RightClick(cell)) remove = app;
        }
        if (remove != null) { apps.Remove(remove); Store.Save(); }
    }

    void DrawList(Rect list)
    {
        var res = Results();
        if (I.AppIndex.All.Count == 0) { G.Text("Uygulamalar taranıyor…", list, 12, Theme.Text3, FontWeight.SemiBold, TextAlignment.Center); U.NeedsFrame = true; return; }
        if (res.Count == 0) { G.Text("Eşleşen uygulama yok", list, 12, Theme.Text3, FontWeight.SemiBold, TextAlignment.Center); return; }
        if (_sel >= res.Count) _sel = res.Count - 1;
        const float rowH = 30;
        float maxScroll = MathF.Max(0, res.Count * rowH - list.Height);
        if (U.Hit(list) && U.Wheel != 0) _scroll.Target = Math.Clamp(_scroll.Target - U.Wheel * 70, 0, maxScroll);
        float sy = _sel * rowH;
        if (sy - _scroll.Target > list.Height - rowH) _scroll.Target = sy - list.Height + rowH;
        if (sy < _scroll.Target) _scroll.Target = sy;
        _scroll.Target = Math.Clamp(_scroll.Target, 0, maxScroll);
        _scroll.Step(U.Dt);
        if (!_scroll.Settled) U.NeedsFrame = true;

        G.PushClip(list);
        for (int i = 0; i < res.Count; i++)
        {
            float y = list.Y + i * rowH - _scroll.Value;
            if (y > list.Bottom || y + rowH < list.Y) continue;
            var e = res[i];
            var r = new Rect(list.X, y, list.Width, rowH - 2);
            int id = Ui.Id("app.row", e.Path.GetHashCode());
            bool hit = U.Hit(r) && U.Hit(list);
            float h = U.Anim(id, hit || i == _sel ? 1 : 0);
            if (h > 0.01f) G.FillRound(r, 9, Theme.Surface.WithA(0.6f + h));
            var bmp = I.Apps.Icon(e.Path);
            var ir = new Rect(r.X + 10, r.CenterY - 9, 18, 18);
            if (bmp != null) G.Bitmap(bmp, ir); else G.FillRound(ir, 5, Theme.SurfaceHi);
            G.Text(e.Name, new Rect(r.X + 38, r.Y, r.Width - 170, r.Height), 12.5f, Theme.Text, FontWeight.SemiBold);
            G.Text(i == _sel ? "Enter: aç" : e.Kind, new Rect(r.Right - 120, r.Y, 108, r.Height), 10.5f, i == _sel ? Theme.Accent : Theme.Text3, FontWeight.SemiBold, TextAlignment.Trailing);
            if (U.Click(id, r) && U.Hit(list)) Launch(e.Path, e.Name);
            if (U.RightClick(r) && !Store.Current.Apps.Any(x => x.Path == e.Path))
            {
                Store.Current.Apps.Add(new PinnedApp { Name = e.Name, Path = e.Path });
                Store.Save();
                I.Toast("Sabitlendi", e.Name, Core.Glyph.Add, Theme.Accent, 4);
            }
        }
        G.PopClip();
    }

    void AddApp()
    {
        var path = AppsService.PickFile(I.Hwnd);
        if (path == null || Store.Current.Apps.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase))) return;
        Store.Current.Apps.Add(AppsService.FromPath(path));
        Store.Save();
        I.Invalidate();
    }
}
