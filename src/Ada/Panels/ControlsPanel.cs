using System.Numerics;
using Ada.Core;
using Ada.Services;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Panels;

/// <summary>
/// macOS Denetim Merkezi tarzı kontroller: bağlantı kartı, hızlı kutular, kalın beyaz kaydırıcılar, Şimdi Çalıyor.
/// </summary>
public sealed class ControlsPanel : Panel
{
    public readonly MediaCard Media;
    static readonly Color4 On = Theme.Rgb(0x0A84FF); // macOS mavisi: açık durum

    public ControlsPanel(Island i) : base(i) => Media = new MediaCard(i);

    public override string Name => "Kontroller";
    public override string Glyph => Core.Glyph.Sliders;

    public override void OnShow()
    {
        var c = I.Controls;
        c.ReadVolume(); c.ReadTheme(); c.ReadRadios(); c.ReadBrightness(I.Monitor);
    }

    public override void Tick() => I.Invalidate();

    public override void Draw(Rect a)
    {
        var c = I.Controls;
        const float gap = 12;
        float lw = (a.Width - gap) * 0.5f;
        var left = new Rect(a.X, a.Y, lw, a.Height);
        var right = new Rect(a.X + lw + gap, a.Y, a.Width - lw - gap, a.Height);

        // ---- sol üst: bağlantı kartı ----
        var conn = new Rect(left.X, left.Y, left.Width, 150);
        Module(conn);
        float rowH = (conn.Height - 16) / 3;
        if (ConnRow(Ui.Id("cc.wifi"), new Rect(conn.X + 8, conn.Y + 8, conn.Width - 16, rowH), Core.Glyph.Wifi, "Wi-Fi",
                c.Wifi switch { true => "Bağlı", false => "Kapalı", _ => "Ayarlar" }, c.Wifi == true))
            c.ToggleRadio(true);
        if (ConnRow(Ui.Id("cc.bt"), new Rect(conn.X + 8, conn.Y + 8 + rowH, conn.Width - 16, rowH), Core.Glyph.Bluetooth, "Bluetooth",
                c.Bluetooth switch { true => "Açık", false => "Kapalı", _ => "Ayarlar" }, c.Bluetooth == true))
            c.ToggleRadio(false);
        if (ConnRow(Ui.Id("cc.dark"), new Rect(conn.X + 8, conn.Y + 8 + rowH * 2, conn.Width - 16, rowH), Core.Glyph.Contrast, "Karanlık mod",
                c.DarkMode ? "Açık" : "Kapalı", c.DarkMode))
            c.ToggleDark();

        // ---- sol alt: üç hızlı kutu ----
        float ty = conn.Bottom + gap, th = left.Bottom - ty, tw = (left.Width - gap * 2) / 3;
        if (Square(Ui.Id("cc.snip"), new Rect(left.X, ty, tw, th), "", "Ekran alıntısı"))
        { I.Collapse(); Dispatcher.Post(() => ControlsService.OpenSettings("ms-screenclip:")); }
        if (Square(Ui.Id("cc.night"), new Rect(left.X + tw + gap, ty, tw, th), Core.Glyph.Moon, "Gece ışığı"))
        { ControlsService.OpenSettings("ms-settings:nightlight"); I.Collapse(); }
        if (Square(Ui.Id("cc.mute"), new Rect(left.X + (tw + gap) * 2, ty, tw, th), c.Muted ? Core.Glyph.Mute : Core.Glyph.Volume,
                c.Muted ? "Sessiz" : "Ses açık", c.Muted))
            c.ToggleMute();

        // ---- sağ: ekran + ses kaydırıcıları ----
        var disp = new Rect(right.X, right.Y, right.Width, 64);
        Module(disp);
        G.Text("Ekran", new Rect(disp.X + 14, disp.Y + 7, 120, 16), 11.5f, Theme.Text, FontWeight.SemiBold);
        if (c.Brightness >= 0)
        {
            float b = c.Brightness;
            if (BigSlider(Ui.Id("cc.bri"), new Rect(disp.X + 12, disp.Y + 27, disp.Width - 24, 28), ref b, Core.Glyph.Brightness))
                c.SetBrightness(I.Monitor, b);
        }
        else G.Text("Bu ekranda parlaklık desteklenmiyor", new Rect(disp.X + 14, disp.Y + 27, disp.Width - 28, 28), 11.5f, Theme.Text3, FontWeight.Normal, font: Theme.TextFont);

        var snd = new Rect(right.X, disp.Bottom + gap, right.Width, 64);
        Module(snd);
        G.Text("Ses", new Rect(snd.X + 14, snd.Y + 7, 120, 16), 11.5f, Theme.Text, FontWeight.SemiBold);
        if (U.IconButton(Ui.Id("cc.out"), new Vector2(snd.Right - 22, snd.Y + 15), 10, "", 9, Theme.Text2))
        { ControlsService.OpenSettings("ms-settings:sound"); I.Collapse(); }
        float vol = c.Muted ? 0 : c.Volume;
        if (BigSlider(Ui.Id("cc.vol"), new Rect(snd.X + 12, snd.Y + 27, snd.Width - 24, 28), ref vol, c.Muted || vol < 0.01f ? Core.Glyph.Mute : Core.Glyph.Volume))
            c.SetVolume(vol);

        // ---- sağ alt: Şimdi Çalıyor ----
        var np = new Rect(right.X, snd.Bottom + gap, right.Width, right.Bottom - snd.Bottom - gap);
        Media.DrawCompact(np);
    }

    /// <summary>Denetim Merkezi modülü: hafif cam gradyanı + ince kenar.</summary>
    void Module(Rect r)
    {
        G.VGradientRound(r, 18, Theme.Rgb(0xFFFFFF, 0.085f), Theme.Rgb(0xFFFFFF, 0.045f));
        G.StrokeRound(r, 18, Theme.Rgb(0xFFFFFF, 0.07f), 1);
    }

    bool ConnRow(int id, Rect r, string glyph, string label, string sub, bool on)
    {
        bool clicked = U.Click(id, r);
        float hv = U.Anim(id, U.Hit(r) ? 1 : 0);
        float o = U.Anim(id + 3, on ? 1 : 0, 12);
        float p = U.Anim(id + 5, U.ActiveId == id && U.Down && U.Hit(r) ? 1 : 0, 24);
        if (hv > 0.01f) G.FillRound(r, 12, Theme.Rgb(0xFFFFFF, 0.05f * hv));
        var ic = new Vector2(r.X + 22, r.CenterY);
        float rad = 15 * (1 - 0.08f * p);
        G.FillCircle(ic, rad, Theme.Mix(Theme.Rgb(0xFFFFFF, 0.14f), On, o));
        G.Icon(glyph, ic, 13, Theme.Text);
        G.Text(label, new Rect(r.X + 46, r.CenterY - 16, r.Width - 54, 17), 13, Theme.Text, FontWeight.SemiBold);
        G.Text(sub, new Rect(r.X + 46, r.CenterY + 1, r.Width - 54, 15), 11, Theme.Text2, FontWeight.Normal, font: Theme.TextFont);
        return clicked;
    }

    bool Square(int id, Rect r, string glyph, string label, bool on = false)
    {
        bool clicked = U.Click(id, r);
        float hv = U.Anim(id, U.Hit(r) ? 1 : 0);
        float o = U.Anim(id + 3, on ? 1 : 0, 12);
        float p = U.Anim(id + 5, U.ActiveId == id && U.Down && U.Hit(r) ? 1 : 0, 24);
        float s = 1 - 0.04f * p;
        var rr = new Rect(r.CenterX - r.Width * s / 2, r.CenterY - r.Height * s / 2, r.Width * s, r.Height * s);
        Module(rr);
        if (hv > 0.01f) G.FillRound(rr, 18, Theme.Rgb(0xFFFFFF, 0.05f * hv));
        var ic = new Vector2(rr.CenterX, rr.CenterY - 10);
        G.FillCircle(ic, 16, Theme.Mix(Theme.Rgb(0xFFFFFF, 0.14f), Theme.Orange, o));
        G.Icon(glyph, ic, 13, Theme.Text);
        G.Text(label, new Rect(rr.X + 4, ic.Y + 20, rr.Width - 8, 16), 10.5f, Theme.Text, FontWeight.SemiBold, TextAlignment.Center);
        return clicked;
    }

    /// <summary>macOS tarzı kalın kaydırıcı: beyaz dolgu, ikon iz içinde, sürüklerken hafif büyür.</summary>
    bool BigSlider(int id, Rect r, ref float value, string glyph)
    {
        bool h = U.Hit(r);
        if (h) U.WantHand = true;
        if (h && U.Pressed) U.ActiveId = id;
        bool changed = false;
        if (U.ActiveId == id && U.Down)
        {
            float v = Math.Clamp((U.Mouse.X - r.Left) / r.Width, 0, 1);
            if (MathF.Abs(v - value) > 0.002f) { value = v; changed = true; }
        }
        if (h && U.Wheel != 0) { value = Math.Clamp(value + U.Wheel * 0.05f, 0, 1); changed = true; }
        float drag = U.Anim(id, U.ActiveId == id && U.Down ? 1 : 0, 18);
        float shown = U.Anim(id + 7, value, 24);
        var rr = new Rect(r.X, r.Y - drag * 2, r.Width, r.Height + drag * 4);
        float rad = rr.Height / 2;
        G.FillRound(rr, rad, Theme.Rgb(0x000000, 0.35f));
        G.StrokeRound(rr, rad, Theme.Rgb(0xFFFFFF, 0.06f), 1);
        float fw = MathF.Max(rr.Height, rr.Width * shown);
        G.FillRound(new Rect(rr.X, rr.Y, fw, rr.Height), rad, Theme.Rgb(0xF5F5F7));
        // ikon: dolgu üstündeyse koyu, değilse açık
        var ic = new Vector2(rr.X + rad, rr.CenterY);
        G.Icon(glyph, ic, 12, fw > rad * 2 ? Theme.Rgb(0x1C1C1E) : Theme.Text2);
        G.Text($"{MathF.Round(shown * 100)}", new Rect(rr.Right - 46, rr.Y, 36, rr.Height), 11, shown > 0.9f ? Theme.Rgb(0x1C1C1E) : Theme.Text2,
            FontWeight.SemiBold, TextAlignment.Trailing);
        return changed;
    }
}
