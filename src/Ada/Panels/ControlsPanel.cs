using System.Numerics;
using Ada.Core;
using Ada.Services;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Panels;

public sealed class ControlsPanel : Panel
{
    int _confirm = -1;
    DateTime _confirmUntil;

    public ControlsPanel(Island i) : base(i) { }

    public override void OnShow()
    {
        var c = I.Controls;
        c.ReadVolume();
        c.ReadTheme();
        c.ReadRadios();
        c.ReadBrightness(I.Monitor);
    }

    public override void Tick()
    {
        if (_confirm >= 0 && DateTime.Now > _confirmUntil) { _confirm = -1; I.Invalidate(); }
    }

    public override void Draw(Rect a)
    {
        var c = I.Controls;
        float leftW = 330;

        // ---- kaydırıcılar ----
        var vc = new Rect(a.X, a.Y, leftW, 150);
        Card(vc);
        G.Text("Ses", new Rect(vc.X + 18, vc.Y + 12, 120, 20), 12, Theme.Text2, FontWeight.SemiBold);
        var muteC = new Vector2(vc.Right - 30, vc.Y + 22);
        if (U.IconButton(Ui.Id("c.mute"), muteC, 14, c.Muted ? Glyph.Mute : Glyph.Volume, 12, c.Muted ? Theme.Red : Theme.Text2)) c.ToggleMute();
        float vol = c.Muted ? 0 : c.Volume;
        if (U.Slider(Ui.Id("c.vol"), new Rect(vc.X + 16, vc.Y + 38, vc.Width - 32, 34), ref vol, Theme.Text, c.Muted ? Glyph.Mute : Glyph.Volume))
            c.SetVolume(vol);

        G.Text("Parlaklık", new Rect(vc.X + 18, vc.Y + 80, 120, 20), 12, Theme.Text2, FontWeight.SemiBold);
        if (c.Brightness >= 0)
        {
            float b = c.Brightness;
            if (U.Slider(Ui.Id("c.bri"), new Rect(vc.X + 16, vc.Y + 104, vc.Width - 32, 34), ref b, Theme.Amber, Glyph.Brightness))
                c.SetBrightness(I.Monitor, b);
        }
        else
        {
            var r = new Rect(vc.X + 16, vc.Y + 104, vc.Width - 32, 34);
            G.FillRound(r, 17, Theme.Surface);
            G.Text("Bu ekranda desteklenmiyor", r, 12, Theme.Text3, FontWeight.Normal, TextAlignment.Center, font: Theme.TextFont);
        }

        // ---- güç ----
        var pc = new Rect(a.X, vc.Bottom + 12, leftW, a.Bottom - vc.Bottom - 12);
        Card(pc);
        string[] names = { "Kilitle", "Uyku", "Yeniden başlat", "Kapat" };
        string[] glyphs = { Glyph.Lock, Glyph.Moon, Glyph.Restart, Glyph.Power };
        Color4[] cols = { Theme.Text, Theme.Sky, Theme.Amber, Theme.Red };
        float cell = pc.Width / 4;
        for (int i = 0; i < 4; i++)
        {
            var ctr = new Vector2(pc.X + cell * i + cell / 2, pc.Y + 30);
            bool armed = _confirm == i;
            float k = U.Anim(Ui.Id("c.arm", i), armed ? 1 : 0, 14);
            if (k > 0.01f) G.FillCircle(ctr, 21 + 2 * k, Theme.Red.WithA(0.22f * k));
            if (U.IconButton(Ui.Id("c.pw", i), ctr, 19, glyphs[i], 15, armed ? Theme.Red : cols[i], true, Theme.SurfaceHi))
                Power(i);
            var label = armed ? "Emin misin?" : names[i];
            G.Text(label, new Rect(ctr.X - cell / 2, ctr.Y + 24, cell, 16), 10.5f, armed ? Theme.Red : Theme.Text2, FontWeight.SemiBold, TextAlignment.Center);
        }

        // ---- kutucuklar ----
        float tx = a.X + leftW + 12, tw = (a.Right - tx - 12) / 2, th = (a.Height - 12 * 2) / 3;
        var t1 = new Rect(tx, a.Y, tw, th);
        var t2 = new Rect(tx + tw + 12, a.Y, tw, th);
        var t3 = new Rect(tx, a.Y + th + 12, tw, th);
        var t4 = new Rect(tx + tw + 12, a.Y + th + 12, tw, th);
        var t5 = new Rect(tx, a.Y + (th + 12) * 2, tw, th);
        var t6 = new Rect(tx + tw + 12, a.Y + (th + 12) * 2, tw, th);

        if (U.Tile(Ui.Id("t.wifi"), t1, Glyph.Wifi, "Wi-Fi", c.Wifi switch { true => "Açık", false => "Kapalı", _ => "Ayarlar" }, c.Wifi == true, Theme.Sky))
            c.ToggleRadio(true);
        if (U.Tile(Ui.Id("t.bt"), t2, Glyph.Bluetooth, "Bluetooth", c.Bluetooth switch { true => "Açık", false => "Kapalı", _ => "Ayarlar" }, c.Bluetooth == true, Theme.Accent))
            c.ToggleRadio(false);
        if (U.Tile(Ui.Id("t.dark"), t3, Glyph.Moon, "Karanlık mod", c.DarkMode ? "Açık" : "Kapalı", c.DarkMode, Theme.Pink))
            c.ToggleDark();
        if (U.Tile(Ui.Id("t.notch"), t4, Glyph.Notch, "Çentik modu", I.NotchStyle ? "Üste yapışık" : "Yüzen ada", I.NotchStyle, Theme.Amber))
            I.NotchStyle = !I.NotchStyle;
        if (U.Tile(Ui.Id("t.snd"), t5, Glyph.Volume, "Ses çıkışı", "Cihaz seç", false, Theme.Mint))
        { ControlsService.OpenSettings("ms-settings:sound"); I.Collapse(); }
        if (U.Tile(Ui.Id("t.set"), t6, Glyph.Settings, "Ayarlar", "Windows", false, Theme.Text))
        { ControlsService.OpenSettings(); I.Collapse(); }
    }

    void Power(int i)
    {
        if (i == 0) { I.Collapse(); ControlsService.Lock(); return; }
        if (i == 1) { I.Collapse(); ControlsService.Sleep(); return; }
        if (_confirm != i) { _confirm = i; _confirmUntil = DateTime.Now.AddSeconds(3); return; }
        _confirm = -1;
        if (i == 2) ControlsService.Restart(); else ControlsService.Shutdown();
    }
}
