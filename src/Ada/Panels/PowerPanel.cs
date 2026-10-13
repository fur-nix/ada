using System.Numerics;
using Ada.Core;
using Ada.Services;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Panels;

/// <summary>Güç: Kilitle · Uyku · Yeniden başlat · Kapat (son ikisi çift tık onaylı).</summary>
public sealed class PowerPanel : Panel
{
    int _confirm = -1;
    DateTime _until;

    public PowerPanel(Island i) : base(i) { }

    public override string Name => "Güç";
    public override string Glyph => Core.Glyph.Power;

    public override void Tick()
    {
        if (_confirm >= 0 && DateTime.Now > _until) { _confirm = -1; I.Invalidate(); }
    }

    public override void Draw(Rect a)
    {
        string[] names = { "Kilitle", "Uyku", "Yeniden başlat", "Kapat" };
        string[] glyphs = { Core.Glyph.Lock, Core.Glyph.Moon, Core.Glyph.Restart, Core.Glyph.Power };
        Color4[] cols = { Theme.Text, Theme.Sky, Theme.Amber, Theme.Red };
        float gap = 14, w = (a.Width - gap * 3) / 4, h = MathF.Min(150, a.Height - 30);
        float y = a.Y + (a.Height - h) / 2 - 8;
        for (int i = 0; i < 4; i++)
        {
            var r = new Rect(a.X + i * (w + gap), y, w, h);
            int id = Ui.Id("pw", i);
            bool armed = _confirm == i;
            bool clicked = U.Click(id, r);
            float hv = U.Anim(id, U.Hit(r) ? 1 : 0);
            float k = U.Anim(id + 9, armed ? 1 : 0, 14);
            float p = U.Anim(id + 3, U.ActiveId == id && U.Down && U.Hit(r) ? 1 : 0, 24);
            float s = 1 - 0.04f * p;
            var rr = new Rect(r.CenterX - r.Width * s / 2, r.CenterY - r.Height * s / 2, r.Width * s, r.Height * s);
            G.FillRound(rr, 22, Theme.Mix(Theme.Surface.WithA(1 + 1.2f * hv), Theme.Red.WithA(0.22f), k));
            if (k > 0.01f) G.StrokeRound(rr, 22, Theme.Red.WithA(0.7f * k), 1.5f);
            var c = new Vector2(rr.CenterX, rr.CenterY - 16);
            G.FillCircle(c, 26 + 2 * hv, Theme.Mix(Theme.SurfaceHi, cols[i].WithA(0.25f), hv));
            G.Icon(glyphs[i], c, 20, armed ? Theme.Red : cols[i]);
            G.Text(armed ? "Emin misin? Tekrar tıkla" : names[i], new Rect(rr.X + 6, c.Y + 36, rr.Width - 12, 20), 12.5f,
                armed ? Theme.Red : Theme.Text, FontWeight.Bold, TextAlignment.Center);
            if (clicked) Power(i);
        }
        G.Text("Yeniden başlat ve kapat ikinci tıklamada çalışır", new Rect(a.X, a.Bottom - 16, a.Width, 16), 10.5f, Theme.Text3,
            FontWeight.Normal, TextAlignment.Center, font: Theme.TextFont);
    }

    void Power(int i)
    {
        if (i == 0) { I.Collapse(); ControlsService.Lock(); return; }
        if (i == 1) { I.Collapse(); ControlsService.Sleep(); return; }
        if (_confirm != i) { _confirm = i; _until = DateTime.Now.AddSeconds(3); return; }
        _confirm = -1;
        if (i == 2) ControlsService.Restart(); else ControlsService.Shutdown();
    }
}
