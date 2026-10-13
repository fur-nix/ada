using System.Numerics;
using Ada.Core;
using Ada.Services;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Panels;

/// <summary>İstatistik: CPU/GPU/RAM halkaları, en çok RAM kullananlar, hız testi.</summary>
public sealed class SystemPanel : Panel
{
    int _tick;
    public SystemPanel(Island i) : base(i) { }

    public override string Name => "İstatistik";
    public override string Glyph => Core.Glyph.Pulse;

    public override void OnShow() { I.Stats.Sample(); I.Stats.RefreshTopApps(); I.Invalidate(); }
    public override void OnHide() { base.OnHide(); I.Stats.Pause(); }
    public override void Tick()
    {
        I.Stats.Sample();
        if (++_tick % 3 == 0) I.Stats.RefreshTopApps();
        I.Invalidate();
    }

    public override void Draw(Rect a)
    {
        var s = I.Stats;
        float rightW = 300;
        var left = new Rect(a.X, a.Y, a.Width - rightW - 14, a.Height);

        // ---- halkalar ----
        var rings = new Rect(left.X, left.Y, left.Width, 140);
        Card(rings);
        float cw = rings.Width / 3;
        Ring(new Vector2(rings.X + cw * 0.5f, rings.Y + 58), "CPU", s.Cpu, $"{Environment.ProcessorCount} çekirdek", "cpu");
        Ring(new Vector2(rings.X + cw * 1.5f, rings.Y + 58), "GPU", MathF.Max(0, s.Gpu), s.Gpu < 0 ? "okunamadı" : "3D motor", "gpu");
        Ring(new Vector2(rings.X + cw * 2.5f, rings.Y + 58), "RAM", s.Ram, $"{StatsService.Bytes(s.RamUsed)} / {StatsService.Bytes(s.RamTotal)}", "ram");

        // ---- alt: disk / ağ / açık kalma ----
        float y = rings.Bottom + 12, h = left.Bottom - y, gap = 10, w = (left.Width - gap * 2) / 3;
        Mini(new Rect(left.X, y, w, h), "DİSK C:", $"{StatsService.Bytes(s.DiskUsed)} / {StatsService.Bytes(s.DiskTotal)}",
            s.DiskTotal > 0 ? (float)(s.DiskUsed / s.DiskTotal) : 0);
        var net = new Rect(left.X + w + gap, y, w, h);
        Mini(net, "AĞ", $"↓ {StatsService.Bytes(s.NetDown)}/s", null);
        G.Text($"↑ {StatsService.Bytes(s.NetUp)}/s", new Rect(net.X + 12, net.Y + 50, net.Width - 20, 20), 12, Theme.Text2, FontWeight.SemiBold);
        Mini(new Rect(left.X + (w + gap) * 2, y, w, h), "AÇIK KALMA", StatsService.Uptime(), null);

        // ---- sağ: en çok RAM ----
        var top = new Rect(a.Right - rightW, a.Y, rightW, 140);
        Card(top);
        G.Text("En çok kullananlar · RAM", new Rect(top.X + 14, top.Y + 8, 200, 18), 11.5f, Theme.Text2, FontWeight.SemiBold);
        var apps = s.TopApps;
        long max = apps.Count > 0 ? apps[0].Bytes : 1;
        for (int i = 0; i < apps.Count; i++)
        {
            var ap = apps[i];
            float ry = top.Y + 32 + i * 26;
            var icon = ap.Path != null ? I.Apps.Icon(ap.Path) : null;
            var ir = new Rect(top.X + 14, ry + 2, 18, 18);
            if (icon != null) G.Bitmap(icon, ir); else G.FillRound(ir, 5, Theme.SurfaceHi);
            G.Text(ap.Name, new Rect(top.X + 40, ry, rightW - 120, 16), 12, Theme.Text, FontWeight.SemiBold);
            G.Text(StatsService.Bytes(ap.Bytes), new Rect(top.Right - 80, ry, 66, 16), 11, Theme.Text2, FontWeight.SemiBold, TextAlignment.Trailing);
            float bw = (rightW - 54) * (float)ap.Bytes / max;
            float shown = U.Anim(Ui.Id("top.bar", i), bw, 10);
            G.FillRound(new Rect(top.X + 40, ry + 18, rightW - 54, 3), 1.5f, Theme.Rgb(0xFFFFFF, 0.06f));
            G.FillRound(new Rect(top.X + 40, ry + 18, MathF.Max(3, shown), 3), 1.5f, Theme.Accent);
        }

        // ---- sağ: hız testi ----
        var sp = new Rect(top.X, top.Bottom + 12, rightW, a.Bottom - top.Bottom - 12);
        Card(sp);
        G.Text("İnternet hızı", new Rect(sp.X + 14, sp.Y + 8, 140, 18), 11.5f, Theme.Text2, FontWeight.SemiBold);
        G.Text(s.SpeedStatus, new Rect(sp.Right - 170, sp.Y + 8, 156, 18), 10.5f, Theme.Text3, FontWeight.SemiBold, TextAlignment.Trailing);
        string down = s.SpeedDown < 0 ? "--" : s.SpeedDown.ToString(s.SpeedDown < 10 ? "0.0" : "0");
        string up = s.SpeedUp < 0 ? "--" : s.SpeedUp.ToString(s.SpeedUp < 10 ? "0.0" : "0");
        float dw = G.Measure(down, 26, FontWeight.Black);
        G.Text(down, new Rect(sp.X + 14, sp.Y + 26, 150, 34), 26, Theme.Text, FontWeight.Black);
        G.Text("Mbps ↓", new Rect(sp.X + 20 + dw, sp.Y + 38, 60, 18), 10.5f, Theme.Text2, FontWeight.SemiBold);
        G.Text(up + "  Mbps ↑", new Rect(sp.X + 150, sp.Y + 36, 130, 20), 13, Theme.Text2, FontWeight.Bold);
        var btn = new Rect(sp.X + 14, sp.Bottom - 38, sp.Width - 28, 28);
        if (U.Pill(Ui.Id("speed.go"), btn, s.SpeedTesting ? "Test ediliyor…" : "Hız testini başlat",
                s.SpeedTesting ? Theme.Surface : Theme.Accent, s.SpeedTesting ? Theme.Text2 : Theme.TileOnText) && !s.SpeedTesting)
            s.RunSpeedTest();
        if (s.SpeedTesting) U.NeedsFrame = true;
    }

    void Ring(Vector2 c, string label, float v, string sub, string key)
    {
        const float r = 32, th = 6;
        float shown = U.Anim(Ui.Id("ring." + key), v, 7);
        G.StrokeCircle(c, r, Theme.Rgb(0xFFFFFF, 0.07f), th);
        var col = shown > 0.85f ? Theme.Red : shown > 0.65f ? Theme.Orange : Theme.Accent;
        G.Arc(c, r, 0, 360 * Ease.Clamp01(shown), col, th);
        G.Text($"{shown * 100:0}%", new Rect(c.X - 30, c.Y - 11, 60, 22), 15, Theme.Text, FontWeight.Bold, TextAlignment.Center);
        G.Text(label, new Rect(c.X - 60, c.Y + r + 6, 120, 16), 12, Theme.Text, FontWeight.Bold, TextAlignment.Center);
        G.Text(sub, new Rect(c.X - 70, c.Y + r + 22, 140, 14), 10, Theme.Text3, FontWeight.SemiBold, TextAlignment.Center);
    }

    void Mini(Rect r, string label, string value, float? bar)
    {
        Card(r, 14);
        G.Text(label, new Rect(r.X + 12, r.Y + 10, r.Width - 20, 14), 10, Theme.Text3, FontWeight.Bold);
        G.Text(value, new Rect(r.X + 12, r.Y + 28, r.Width - 20, 22), 13, Theme.Text, FontWeight.Bold);
        if (bar is float b)
        {
            var br = new Rect(r.X + 12, r.Bottom - 16, r.Width - 24, 4);
            G.FillRound(br, 2, Theme.Rgb(0xFFFFFF, 0.08f));
            G.FillRound(new Rect(br.X, br.Y, MathF.Max(4, br.Width * b), 4), 2, Theme.Accent);
        }
    }
}
