using System.Numerics;
using Ada.Core;
using Ada.Data;
using Ada.Services;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Panels;

public sealed class AppsPanel : Panel
{
    Spring _scroll = new(0, 260, 32);

    public AppsPanel(Island i) : base(i) { }

    public override void OnShow() => _scroll.Snap(0);

    public override void Draw(Rect a)
    {
        var apps = Store.Current.Apps;
        const int cols = 7;
        const float cellH = 104, gap = 8;
        float cellW = (a.Width - gap * (cols - 1)) / cols;
        int count = apps.Count + 1; // + ekle
        int rows = (count + cols - 1) / cols;
        float content = rows * (cellH + gap) - gap;
        float maxScroll = MathF.Max(0, content - a.Height);
        if (U.Hit(a) && U.Wheel != 0) _scroll.Target = Math.Clamp(_scroll.Target - U.Wheel * 90, 0, maxScroll);
        _scroll.Target = Math.Clamp(_scroll.Target, 0, maxScroll);
        _scroll.Step(U.Dt);
        if (!_scroll.Settled) U.NeedsFrame = true;

        PinnedApp? remove = null;
        G.PushClip(a);
        for (int i = 0; i < count; i++)
        {
            int row = i / cols, col = i % cols;
            var r = new Rect(a.X + col * (cellW + gap), a.Y + row * (cellH + gap) - _scroll.Value, cellW, cellH);
            if (r.Bottom < a.Y || r.Y > a.Bottom) continue;
            bool isAdd = i == apps.Count;
            int id = Ui.Id("app", isAdd ? -1 : apps[i].Path.GetHashCode());
            bool hit = U.Hit(r) && U.Hit(a);
            bool clicked = U.Click(id, r) && U.Hit(a);
            float h = U.Anim(id, hit ? 1 : 0, 18);
            float p = U.Anim(id + 1, U.ActiveId == id && U.Down && hit ? 1 : 0, 26);
            // giriş animasyonu: hücreler sırayla belirir
            float lift = -4 * h + 2 * p;

            if (h > 0.01f) G.FillRound(r, 20, Theme.Surface.WithA(h * 1.4f));
            var ic = new Rect(r.CenterX - 26, r.Y + 14 + lift, 52, 52);
            float s = 1 + 0.06f * h - 0.08f * p;
            var icS = new Rect(ic.CenterX - 26 * s, ic.CenterY - 26 * s, 52 * s, 52 * s);

            if (isAdd)
            {
                G.FillRound(icS, 16, Theme.Rgb(0xFFFFFF, 0.06f + 0.04f * h));
                G.StrokeRound(icS, 16, Theme.Accent.WithA(0.25f + 0.4f * h), 1.2f);
                G.Icon(Glyph.Add, new Vector2(icS.CenterX, icS.CenterY), 18, Theme.Accent);
                G.Text("Ekle", new Rect(r.X + 4, r.Y + 72, r.Width - 8, 20), 11.5f, Theme.Text2, FontWeight.SemiBold, TextAlignment.Center);
                if (clicked) Dispatcher.Post(AddApp); // modal pencere çizim karesinin dışında açılsın
                continue;
            }

            var app = apps[i];
            var bmp = I.Apps.Icon(app.Path);
            if (h > 0.01f) G.Shadow(icS, 14, 8, 0.4f * h, 4);
            if (bmp != null) G.Bitmap(bmp, icS);
            else
            {
                G.VGradientRound(icS, 16, Theme.Accent.WithA(0.5f), Theme.Rgb(0x23233A));
                var glyph = app.Path.StartsWith("ms-settings", StringComparison.OrdinalIgnoreCase) ? Glyph.Settings : Glyph.Apps;
                G.Icon(glyph, new Vector2(icS.CenterX, icS.CenterY), 20, Theme.Text);
            }
            G.Text(app.Name, new Rect(r.X + 4, r.Y + 72, r.Width - 8, 20), 11.5f, Theme.Mix(Theme.Text2, Theme.Text, h), FontWeight.SemiBold, TextAlignment.Center);

            if (clicked) { Dispatcher.Post(() => AppsService.Launch(app)); I.Collapse(); }
            if (U.RightClick(r) && U.Hit(a)) remove = app;
        }
        G.PopClip();

        if (remove != null)
        {
            apps.Remove(remove);
            Store.Save();
        }
        // ipucu
        G.Text("Sağ tık: kaldır · Dosyaları adaya sürükleyip bırakarak da ekleyebilirsin",
            new Rect(a.X, a.Bottom + 2, a.Width, 18), 10.5f, Theme.Text3, FontWeight.Normal, TextAlignment.Center, font: Theme.TextFont);
    }

    void AddApp()
    {
        var path = AppsService.PickFile(I.Hwnd);
        if (path == null) return;
        if (Store.Current.Apps.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase))) return;
        Store.Current.Apps.Add(AppsService.FromPath(path));
        Store.Save();
        I.Invalidate();
    }
}
