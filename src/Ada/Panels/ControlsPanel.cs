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

        // ---- sol: ana görünüm ya da Wi-Fi / Bluetooth listesi (kayarak geçiş) ----
        float st = U.Anim(Ui.Id("cc.sub"), _sub != 0 ? 1 : 0, 14);
        float baseAlpha = G.Alpha;
        G.PushClip(new Rect(left.X - 4, left.Y - 4, left.Width + 8, left.Height + 8));
        if (st < 0.99f)
        {
            G.Alpha = baseAlpha * (1 - st);
            G.PushTranslate(-40 * st, 0);
            bool inside = U.Inside; if (_sub != 0) U.Inside = false;
            DrawMain(left, gap);
            U.Inside = inside;
            G.PopTransform();
        }
        if (st > 0.01f && _sub != 0)
        {
            G.Alpha = baseAlpha * st;
            G.PushTranslate(40 * (1 - st), 0);
            if (_sub == 1) DrawWifi(left); else DrawBluetooth(left);
            G.PopTransform();
        }
        G.PopClip();
        G.Alpha = baseAlpha;
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

    // ================= ana görünüm =================
    int _sub; // 0 ana, 1 Wi-Fi listesi, 2 Bluetooth listesi
    readonly WifiService _wifi = new();
    readonly BluetoothService _bt = new();
    readonly TextBox _pw = new() { Placeholder = "Şifre (kayıtlı ağsa boş bırak)" };
    static readonly int PwId = Ui.Id("cc.pw");
    string? _openNet;
    Spring _listScroll = new(0, 260, 32);

    public override void OnHide() { base.OnHide(); _sub = 0; _openNet = null; }

    /// <summary>--snap testi.</summary>
    public void SnapOpenList(int which) => OpenList(which);

    void OpenList(int which)
    {
        _sub = which; _openNet = null; _listScroll.Snap(0);
        if (which == 1) _wifi.Scan(); else _bt.Refresh();
    }

    void DrawMain(Rect left, float gap)
    {
        var c = I.Controls;
        var conn = new Rect(left.X, left.Y, left.Width, 150);
        Module(conn);
        float rowH = (conn.Height - 16) / 3;
        switch (ConnRow(Ui.Id("cc.wifi"), new Rect(conn.X + 8, conn.Y + 8, conn.Width - 16, rowH), Core.Glyph.Wifi, "Wi-Fi",
                    c.Wifi switch { true => "Açık · ağları gör", false => "Kapalı", _ => "Ayarlar" }, c.Wifi == true, true))
        {
            case 1: c.ToggleRadio(true); break;
            case 2: OpenList(1); break;
        }
        switch (ConnRow(Ui.Id("cc.bt"), new Rect(conn.X + 8, conn.Y + 8 + rowH, conn.Width - 16, rowH), Core.Glyph.Bluetooth, "Bluetooth",
                    c.Bluetooth switch { true => "Açık · cihazları gör", false => "Kapalı", _ => "Ayarlar" }, c.Bluetooth == true, true))
        {
            case 1: c.ToggleRadio(false); break;
            case 2: OpenList(2); break;
        }
        if (ConnRow(Ui.Id("cc.dark"), new Rect(conn.X + 8, conn.Y + 8 + rowH * 2, conn.Width - 16, rowH), Core.Glyph.Contrast, "Karanlık mod",
                c.DarkMode ? "Açık" : "Kapalı", c.DarkMode, false) != 0)
            c.ToggleDark();

        float ty = conn.Bottom + gap, th = left.Bottom - ty, tw = (left.Width - gap * 2) / 3;
        if (Square(Ui.Id("cc.snip"), new Rect(left.X, ty, tw, th), "", "Ekran alıntısı"))
        { I.Collapse(); Dispatcher.Post(() => ControlsService.OpenSettings("ms-screenclip:")); }
        if (Square(Ui.Id("cc.night"), new Rect(left.X + tw + gap, ty, tw, th), Core.Glyph.Moon, "Gece ışığı"))
        { ControlsService.OpenSettings("ms-settings:nightlight"); I.Collapse(); }
        if (Square(Ui.Id("cc.mute"), new Rect(left.X + (tw + gap) * 2, ty, tw, th), c.Muted ? Core.Glyph.Mute : Core.Glyph.Volume,
                c.Muted ? "Sessiz" : "Ses açık", c.Muted))
            c.ToggleMute();
    }

    /// <summary>Liste başlığı: geri, başlık, aç/kapa anahtarı, yenile.</summary>
    Rect ListHeader(Rect r, string title, bool on, Action toggle, Action refresh, bool busy)
    {
        Module(r);
        if (U.IconButton(Ui.Id("cc.back"), new Vector2(r.X + 22, r.Y + 22), 14, "", 11, Theme.Text)) { _sub = 0; _openNet = null; }
        G.Text(title, new Rect(r.X + 42, r.Y + 8, 180, 28), 15, Theme.Text, FontWeight.Bold);
        if (U.IconButton(Ui.Id("cc.refresh"), new Vector2(r.Right - 88, r.Y + 22), 13, busy ? "" : "", 11, Theme.Text2)) refresh();
        if (busy) U.NeedsFrame = true;
        if (U.Toggle(Ui.Id("cc.radio"), new Rect(r.Right - 66, r.Y + 8, 54, 28), on)) toggle();
        return new Rect(r.X + 6, r.Y + 44, r.Width - 12, r.Height - 50);
    }

    void DrawWifi(Rect left)
    {
        var c = I.Controls;
        var list = ListHeader(left, "Wi-Fi", c.Wifi == true, () => c.ToggleRadio(true), _wifi.Scan, _wifi.Scanning);
        if (_wifi.Status.Length > 0)
        {
            G.Text(_wifi.Status, new Rect(list.X + 8, list.Y, list.Width - 16, 16), 10.5f, Theme.Text3, FontWeight.SemiBold);
            list = new Rect(list.X, list.Y + 18, list.Width, list.Height - 18);
        }
        if (c.Wifi == false) { G.Text("Wi-Fi kapalı", list, 12, Theme.Text3, FontWeight.SemiBold, TextAlignment.Center); return; }
        var nets = _wifi.Networks;
        float rowH = 36, openH = 82;
        float content = nets.Sum(n => n.Ssid == _openNet ? rowH + openH : rowH);
        float max = MathF.Max(0, content - list.Height);
        if (U.Hit(list) && U.Wheel != 0) _listScroll.Target = Math.Clamp(_listScroll.Target - U.Wheel * 60, 0, max);
        _listScroll.Target = Math.Clamp(_listScroll.Target, 0, max);
        _listScroll.Step(U.Dt);
        if (!_listScroll.Settled) U.NeedsFrame = true;

        G.PushClip(list);
        float y = list.Y - _listScroll.Value;
        foreach (var n in nets)
        {
            bool open = n.Ssid == _openNet;
            var row = new Rect(list.X, y, list.Width, rowH);
            int id = Ui.Id("wifi.row", n.Ssid.GetHashCode());
            bool hit = U.Hit(row) && U.Hit(list);
            float hv = U.Anim(id, hit || open ? 1 : 0);
            if (hv > 0.01f) G.FillRound(new Rect(row.X, row.Y, row.Width, open ? rowH + openH : rowH), 10, Theme.Rgb(0xFFFFFF, 0.06f * hv));
            Bars(new Vector2(row.X + 18, row.CenterY + 6), n.Bars, n.Connected ? Theme.Rgb(0x0A84FF) : Theme.Text);
            G.Text(n.Ssid, new Rect(row.X + 40, row.Y, row.Width - 120, rowH), 12.5f, Theme.Text, n.Connected ? FontWeight.Bold : FontWeight.SemiBold);
            if (n.Secure) G.Icon(Core.Glyph.Lock, new Vector2(row.Right - 58, row.CenterY), 9, Theme.Text3);
            if (n.Connected) G.Text("Bağlı", new Rect(row.Right - 46, row.Y, 38, rowH), 10.5f, Theme.Rgb(0x0A84FF), FontWeight.Bold, TextAlignment.Trailing);
            else if (_wifi.Connecting == n.Ssid) { G.Text("…", new Rect(row.Right - 46, row.Y, 38, rowH), 12, Theme.Text2, FontWeight.Bold, TextAlignment.Trailing); U.NeedsFrame = true; }
            if (U.Click(id, row) && U.Hit(list)) { _openNet = open ? null : n.Ssid; _pw.Clear(); }
            y += rowH;
            if (open)
            {
                if (n.Connected)
                {
                    if (U.Pill(Ui.Id("wifi.disc"), new Rect(row.X + 40, y + 8, 150, 30), "Bağlantıyı kes", Theme.Surface, Theme.Text)) _wifi.Disconnect();
                }
                else
                {
                    if (n.Secure) Box(_pw, PwId, new Rect(row.X + 40, y + 4, row.Width - 52, 32));
                    var br = new Rect(row.Right - 112, y + (n.Secure ? 42 : 8), 100, 30);
                    if (U.Pill(Ui.Id("wifi.conn"), br, "Bağlan", Theme.Rgb(0x0A84FF), Theme.Text)) ConnectOpen();
                }
                y += openH;
            }
        }
        G.PopClip();
        if (nets.Count == 0 && !_wifi.Scanning && _wifi.Status.Length == 0)
            G.Text("Ağ bulunamadı", list, 12, Theme.Text3, FontWeight.SemiBold, TextAlignment.Center);
    }

    void ConnectOpen()
    {
        var n = _wifi.Networks.FirstOrDefault(x => x.Ssid == _openNet);
        if (n == null) return;
        _wifi.Connect(n, n.Secure && _pw.Text.Length > 0 ? _pw.Text : null);
        _pw.Clear();
        Unfocus();
    }

    protected override void OnSubmit(TextBox box) { if (box == _pw) ConnectOpen(); }

    /// <summary>Wi-Fi sinyal çubukları (0–4).</summary>
    void Bars(Vector2 bl, int bars, Color4 col)
    {
        for (int i = 0; i < 4; i++)
        {
            float h = 4 + i * 3;
            G.FillRound(new Rect(bl.X - 9 + i * 5, bl.Y - h, 3, h), 1.5f, i < bars ? col : Theme.Rgb(0xFFFFFF, 0.18f));
        }
    }

    void DrawBluetooth(Rect left)
    {
        var c = I.Controls;
        var list = ListHeader(left, "Bluetooth", c.Bluetooth == true, () => c.ToggleRadio(false), _bt.Refresh, _bt.Loading);
        if (c.Bluetooth == false) { G.Text("Bluetooth kapalı", list, 12, Theme.Text3, FontWeight.SemiBold, TextAlignment.Center); return; }
        var devs = _bt.Devices;
        const float rowH = 38;
        var area = new Rect(list.X, list.Y, list.Width, list.Height - 40);
        float max = MathF.Max(0, devs.Count * rowH - area.Height);
        if (U.Hit(area) && U.Wheel != 0) _listScroll.Target = Math.Clamp(_listScroll.Target - U.Wheel * 60, 0, max);
        _listScroll.Target = Math.Clamp(_listScroll.Target, 0, max);
        _listScroll.Step(U.Dt);
        if (!_listScroll.Settled) U.NeedsFrame = true;
        G.PushClip(area);
        for (int i = 0; i < devs.Count; i++)
        {
            var d = devs[i];
            var row = new Rect(area.X, area.Y + i * rowH - _listScroll.Value, area.Width, rowH - 2);
            int id = Ui.Id("bt.row", d.Name.GetHashCode());
            float hv = U.Anim(id, U.Hit(row) && U.Hit(area) ? 1 : 0);
            if (hv > 0.01f) G.FillRound(row, 10, Theme.Rgb(0xFFFFFF, 0.06f * hv));
            var ic = new Vector2(row.X + 20, row.CenterY);
            G.FillCircle(ic, 13, d.Connected ? Theme.Rgb(0x0A84FF) : Theme.Rgb(0xFFFFFF, 0.12f));
            G.Icon(d.Kind switch { "Kulaklık" => "", "Fare" => "", "Klavye" => "", "Oyun kumandası" => "", "Telefon" => "", "Hoparlör" => "", _ => Core.Glyph.Bluetooth },
                ic, 11, Theme.Text);
            G.Text(d.Name, new Rect(row.X + 42, row.Y + 3, row.Width - 60, 18), 12.5f, Theme.Text, FontWeight.SemiBold);
            G.Text(d.Connected ? $"{d.Kind} · Bağlı" : $"{d.Kind} · Eşleşmiş", new Rect(row.X + 42, row.Y + 19, row.Width - 60, 15), 10.5f,
                d.Connected ? Theme.Rgb(0x0A84FF) : Theme.Text3, FontWeight.SemiBold);
            if (U.Click(id, row) && U.Hit(area)) { ControlsService.OpenSettings("ms-settings:bluetooth"); I.Collapse(); }
        }
        G.PopClip();
        if (devs.Count == 0 && !_bt.Loading) G.Text("Eşleşmiş cihaz yok", area, 12, Theme.Text3, FontWeight.SemiBold, TextAlignment.Center);
        G.Text("Bağlanmak/ayırmak için cihaza tıkla → Bluetooth ayarları", new Rect(list.X + 8, list.Bottom - 36, list.Width - 140, 30), 10, Theme.Text3, FontWeight.Normal, font: Theme.TextFont);
        if (U.Pill(Ui.Id("bt.add"), new Rect(list.Right - 126, list.Bottom - 34, 120, 28), "Cihaz ekle", Theme.Rgb(0x0A84FF), Theme.Text, Core.Glyph.Add))
        { ControlsService.OpenSettings("ms-settings:bluetooth"); I.Collapse(); }
    }
    /// <summary>Denetim Merkezi modülü: hafif cam gradyanı + ince kenar.</summary>
    void Module(Rect r)
    {
        G.VGradientRound(r, 18, Theme.Rgb(0xFFFFFF, 0.085f), Theme.Rgb(0xFFFFFF, 0.045f));
        G.StrokeRound(r, 18, Theme.Rgb(0xFFFFFF, 0.07f), 1);
    }

    /// <summary>Bölünmüş satır: daireye tıkla → 1 (aç/kapa); split ise satırın geri kalanı → 2 (listeyi aç).</summary>
    int ConnRow(int id, Rect r, string glyph, string label, string sub, bool on, bool split)
    {
        var iconHit = new Rect(r.X, r.Y, 46, r.Height);
        int result = 0;
        if (split)
        {
            if (U.Click(id + 9, iconHit)) result = 1;
            else if (U.Click(id, r) && !U.Hit(iconHit)) result = 2;
        }
        else if (U.Click(id, r)) result = 1;
        bool clicked = result != 0;
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
        if (split) G.Icon("", new Vector2(r.Right - 14, r.CenterY), 10, Theme.Text2.WithA(0.5f + 0.5f * hv));
        _ = clicked;
        return result;
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
