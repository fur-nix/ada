using System.Numerics;
using Ada.Core;
using Ada.Services;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Panels;

public sealed class SystemPanel : Panel
{
    public SystemPanel(Island i) : base(i) { }

    public override void OnShow() { I.Stats.Sample(); I.Invalidate(); }
    public override void OnHide() => I.Stats.Pause();
    public override void Tick() { I.Stats.Sample(); I.Invalidate(); }

    public override void Draw(Rect a)
    {
        var s = I.Stats;
        // ---- üst sol: halkalar ----
        var ringsCard = new Rect(a.X, a.Y, 336, 132);
        Card(ringsCard);
        Ring(new Vector2(ringsCard.X + 58, ringsCard.CenterY - 6), "CPU", s.Cpu, Theme.Mint, "cpu");
        Ring(new Vector2(ringsCard.X + 168, ringsCard.CenterY - 6), "RAM", s.Ram, Theme.Sky, "ram",
            $"{StatsService.Bytes(s.RamUsed)}");
        if (s.Gpu >= 0) Ring(new Vector2(ringsCard.X + 278, ringsCard.CenterY - 6), "GPU", s.Gpu, Theme.Pink, "gpu");
        else Ring(new Vector2(ringsCard.X + 278, ringsCard.CenterY - 6), "GPU", 0, Theme.Text3, "gpu", "yok");

        // ---- üst sağ: CPU geçmişi ----
        var graph = new Rect(ringsCard.Right + 12, a.Y, a.Right - ringsCard.Right - 12, 132);
        Card(graph);
        G.Text("İşlemci · son 60 sn", new Rect(graph.X + 16, graph.Y + 10, graph.Width - 100, 20), 12, Theme.Text2, FontWeight.SemiBold);
        G.Text($"{s.Cpu * 100:0}%", new Rect(graph.Right - 80, graph.Y + 8, 64, 24), 16, Theme.Mint, FontWeight.Bold, TextAlignment.Trailing);
        var gr = new Rect(graph.X + 14, graph.Y + 40, graph.Width - 28, graph.Height - 52);
        for (int i = 1; i <= 3; i++)
        {
            float gy = gr.Top + gr.Height * i / 4;
            G.Line(new Vector2(gr.Left, gy), new Vector2(gr.Right, gy), Theme.Rgb(0xFFFFFF, 0.04f), 1);
        }
        G.Sparkline(gr, s.CpuHist, 1f, Theme.Mint);

        // ---- alt: 4 kart ----
        float y = a.Y + 144, h = a.Bottom - y, gap = 12;
        float cw = (a.Width - gap * 3) / 4;
        var c1 = new Rect(a.X, y, cw, h);
        var c2 = new Rect(c1.Right + gap, y, cw, h);
        var c3 = new Rect(c2.Right + gap, y, cw, h);
        var c4 = new Rect(c3.Right + gap, y, cw, h);

        Stat(c1, Glyph.Down, "İndirme", StatsService.Bytes(s.NetDown) + "/s", Theme.Sky, null);
        Stat(c2, Glyph.Up, "Yükleme", StatsService.Bytes(s.NetUp) + "/s", Theme.Pink, null);
        float dk = s.DiskTotal > 0 ? (float)(s.DiskUsed / s.DiskTotal) : 0;
        Stat(c3, Glyph.Disk, "Disk C:", $"{StatsService.Bytes(s.DiskTotal - s.DiskUsed)} boş", Theme.Amber, dk);
        if (s.Battery >= 0)
            Stat(c4, s.Charging ? Glyph.Charging : Glyph.Battery, s.Charging ? "Şarj oluyor" : "Pil", $"{s.Battery}%",
                s.Battery < 20 && !s.Charging ? Theme.Red : Theme.Green, s.Battery / 100f);
        else Stat(c4, Glyph.Clock, "Açık kalma", StatsService.Uptime(), Theme.Accent, null);
    }

    void Ring(Vector2 c, string label, float v, Color4 col, string key, string? sub = null)
    {
        const float r = 34, th = 7;
        float shown = U.Anim(Ui.Id("ring." + key), v, 7);
        G.StrokeCircle(c, r, Theme.Rgb(0xFFFFFF, 0.07f), th);
        G.Arc(c, r, 0, 360 * Ease.Clamp01(shown), col, th);
        G.Text($"{shown * 100:0}%", new Rect(c.X - 30, c.Y - 11, 60, 22), 15, Theme.Text, FontWeight.Bold, TextAlignment.Center);
        G.Text(label, new Rect(c.X - 40, c.Y + r + 8, 80, 16), 11, Theme.Text2, FontWeight.SemiBold, TextAlignment.Center);
        if (sub != null) G.Text(sub, new Rect(c.X - 30, c.Y + 9, 60, 14), 9.5f, Theme.Text3, FontWeight.SemiBold, TextAlignment.Center);
    }

    void Stat(Rect r, string glyph, string label, string value, Color4 col, float? bar)
    {
        Card(r);
        var ic = new Vector2(r.X + 26, r.Y + 26);
        G.FillCircle(ic, 13, col.WithA(0.16f));
        G.Icon(glyph, ic, 12, col);
        G.Text(label, new Rect(r.X + 46, r.Y + 16, r.Width - 52, 20), 11.5f, Theme.Text2, FontWeight.SemiBold);
        G.Text(value, new Rect(r.X + 16, r.Y + 46, r.Width - 28, 26), 17, Theme.Text, FontWeight.Bold);
        if (bar is float b)
        {
            var br = new Rect(r.X + 16, r.Bottom - 18, r.Width - 32, 5);
            G.FillRound(br, 2.5f, Theme.Rgb(0xFFFFFF, 0.08f));
            float shown = U.Anim(Ui.Id("bar." + label), b, 8);
            G.FillRound(new Rect(br.X, br.Y, MathF.Max(5, br.Width * shown), 5), 2.5f, col);
        }
    }
}
