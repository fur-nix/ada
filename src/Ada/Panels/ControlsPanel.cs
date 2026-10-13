using System.Numerics;
using Ada.Core;
using Ada.Services;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Panels;

/// <summary>Kontroller: yeşil hap kutucuklar, ses/parlaklık, altta müzik kartı.</summary>
public sealed class ControlsPanel : Panel
{
    public readonly MediaCard Media;

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
        const float h = 46, gap = 8;
        float w3 = (a.Width - gap * 2) / 3, w2 = (a.Width - gap) / 2;

        if (Pill(Ui.Id("cp.wifi"), new Rect(a.X, a.Y, w3, h), Core.Glyph.Wifi, "Wi-Fi", c.Wifi switch { true => "Açık", false => "Kapalı", _ => "Ayarlar" }, c.Wifi == true))
            c.ToggleRadio(true);
        if (Pill(Ui.Id("cp.bt"), new Rect(a.X + w3 + gap, a.Y, w3, h), Core.Glyph.Bluetooth, "Bluetooth", c.Bluetooth switch { true => "Açık", false => "Kapalı", _ => "Ayarlar" }, c.Bluetooth == true))
            c.ToggleRadio(false);
        if (Pill(Ui.Id("cp.audio"), new Rect(a.X + (w3 + gap) * 2, a.Y, w3, h), Core.Glyph.Volume, "Ses", c.Muted ? "Sessiz" : $"%{c.Volume * 100:0}", !c.Muted))
            c.ToggleMute();

        float y2 = a.Y + h + gap;
        if (Pill(Ui.Id("cp.night"), new Rect(a.X, y2, w2, h), Core.Glyph.Moon, "Gece ışığı", "Ayarları aç", false))
        { ControlsService.OpenSettings("ms-settings:nightlight"); I.Collapse(); }
        if (Pill(Ui.Id("cp.dark"), new Rect(a.X + w2 + gap, y2, w2, h), Core.Glyph.Contrast, "Karanlık mod", c.DarkMode ? "Açık" : "Kapalı", c.DarkMode))
            c.ToggleDark();

        float y3 = y2 + h + gap;
        float vol = c.Muted ? 0 : c.Volume;
        if (U.Slider(Ui.Id("cp.vol"), new Rect(a.X, y3, w2, 32), ref vol, Theme.TileOn, c.Muted ? Core.Glyph.Mute : Core.Glyph.Volume))
            c.SetVolume(vol);
        if (c.Brightness >= 0)
        {
            float b = c.Brightness;
            if (U.Slider(Ui.Id("cp.bri"), new Rect(a.X + w2 + gap, y3, w2, 32), ref b, Theme.Amber, Core.Glyph.Brightness))
                c.SetBrightness(I.Monitor, b);
        }

        Media.Draw(new Rect(a.X, y3 + 32 + gap + 4, a.Width, a.Bottom - (y3 + 32 + gap + 4)));
    }

    /// <summary>Videodaki gibi yeşil hap kutucuk.</summary>
    bool Pill(int id, Rect r, string glyph, string label, string sub, bool on)
    {
        bool clicked = U.Click(id, r);
        float hv = U.Anim(id, U.Hit(r) ? 1 : 0);
        float o = U.Anim(id + 3, on ? 1 : 0, 12);
        float p = U.Anim(id + 5, U.ActiveId == id && U.Down && U.Hit(r) ? 1 : 0, 24);
        float s = 1 - 0.03f * p;
        var rr = new Rect(r.CenterX - r.Width * s / 2, r.CenterY - r.Height * s / 2, r.Width * s, r.Height * s);
        G.FillRound(rr, 15, Theme.Mix(Theme.Surface.WithA(1 + hv), Theme.TileOn.WithA(0.92f + 0.08f * hv), o));
        var fg = Theme.Mix(Theme.Text, Theme.TileOnText, o);
        var ic = new Vector2(rr.X + 22, rr.CenterY);
        G.FillCircle(ic, 13, Theme.Mix(Theme.SurfaceHi, Theme.Rgb(0x000000, 0.12f), o));
        G.Icon(glyph, ic, 12, fg);
        G.Text(label, new Rect(rr.X + 42, rr.Y + 5, rr.Width - 60, 18), 12.5f, fg, FontWeight.Bold);
        G.Text(sub, new Rect(rr.X + 42, rr.Y + 22, rr.Width - 60, 16), 10.5f, fg.WithA(0.65f), FontWeight.SemiBold);
        G.Icon("", new Vector2(rr.Right - 18, rr.CenterY), 9, fg.WithA(0.6f)); // chevron
        return clicked;
    }
}
