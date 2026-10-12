using System.Numerics;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Core;

/// <summary>Immediate-mode giriş durumu + animasyonlu küçük bileşenler.</summary>
public sealed class Ui
{
    public readonly Gfx G;
    public Vector2 Mouse = new(-9999, -9999);
    public bool Inside, Down, Pressed, Released, RightReleased;
    public float Wheel;
    public float Dt = 1 / 60f;
    public float Time;
    /// <summary>Bir bileşen hâlâ animasyon yapıyorsa true; döngü bir kare daha çizer.</summary>
    public bool NeedsFrame;
    public bool WantHand;
    public int ActiveId;
    public int FocusId;

    readonly Dictionary<int, Smooth> _anim = new();

    public Ui(Gfx g) => G = g;

    public static int Id(string name, int i = 0) => HashCode.Combine(name, i);

    public bool Hit(Rect r) => Inside && r.Contains(Mouse);

    public float Anim(int id, float target, float speed = 16f)
    {
        if (!_anim.TryGetValue(id, out var s)) s = new Smooth(target, speed);
        s.Target = target; s.Speed = speed;
        s.Step(Dt);
        if (!s.Settled) NeedsFrame = true;
        _anim[id] = s;
        return s.Value;
    }

    public bool Click(int id, Rect r)
    {
        bool h = Hit(r);
        if (h) WantHand = true;
        if (h && Pressed) ActiveId = id;
        return h && Released && ActiveId == id;
    }

    public bool RightClick(Rect r) => Hit(r) && RightReleased;

    public void EndFrame()
    {
        Pressed = Released = RightReleased = false;
        Wheel = 0;
        if (!Down && !Pressed) ActiveId = 0;
    }

    public void GcAnims() { if (_anim.Count > 600) _anim.Clear(); }

    // ---------------- bileşenler ----------------

    /// <summary>Yuvarlak ikon butonu; hover'da arka plan, basılıyken küçülme.</summary>
    public bool IconButton(int id, Vector2 c, float radius, string glyph, float iconSize, Color4 color, bool filled = false, Color4? fill = null)
    {
        var r = new Rect(c.X - radius, c.Y - radius, radius * 2, radius * 2);
        bool clicked = Click(id, r);
        float h = Anim(id, Hit(r) ? 1 : 0);
        float p = Anim(id + 1, ActiveId == id && Down && Hit(r) ? 1 : 0, 24);
        float s = 1 - 0.08f * p + 0.04f * h;
        var bg = fill ?? Theme.SurfaceHi;
        if (filled) G.FillCircle(c, radius * s, bg);
        else if (h > 0.01f) G.FillCircle(c, radius * s, Theme.Surface.WithA(h * 1.6f));
        G.Icon(glyph, c, iconSize * s, color);
        return clicked;
    }

    /// <summary>Yatay kaydırıcı; sürüklenirken değer döner.</summary>
    public bool Slider(int id, Rect r, ref float value, Color4 color, string glyph)
    {
        bool h = Hit(r);
        if (h) WantHand = true;
        if (h && Pressed) ActiveId = id;
        bool changed = false;
        if (ActiveId == id && Down)
        {
            float v = Math.Clamp((Mouse.X - r.Left) / r.Width, 0, 1);
            if (MathF.Abs(v - value) > 0.001f) { value = v; changed = true; }
        }
        if (h && Wheel != 0)
        {
            value = Math.Clamp(value + Wheel * 0.04f, 0, 1); changed = true;
        }
        float hv = Anim(id, h || ActiveId == id ? 1 : 0);
        float shown = Anim(id + 7, value, 22);
        float rad = r.Height / 2;
        G.FillRound(r, rad, Theme.Surface.WithA(1 + hv * 0.6f));
        float fw = MathF.Max(r.Height, r.Width * shown);
        var fr = new Rect(r.X, r.Y, fw, r.Height);
        G.HGradientRound(fr, rad, color.WithA(0.55f + 0.15f * hv), color);
        G.Icon(glyph, new Vector2(r.X + rad + 2, r.CenterY), 14, new Color4(0, 0, 0, 0.75f));
        // yüzde
        G.Text($"{MathF.Round(value * 100)}%", new Rect(r.Right - 60, r.Y, 50, r.Height), 12, Theme.Text.WithA(0.5f + 0.5f * hv),
            FontWeight.SemiBold, TextAlignment.Trailing);
        return changed;
    }

    /// <summary>Kontrol merkezi kutucuğu (Wi-Fi, Bluetooth...).</summary>
    public bool Tile(int id, Rect r, string glyph, string label, string? sub, bool on, Color4 onColor)
    {
        bool clicked = Click(id, r);
        float h = Anim(id, Hit(r) ? 1 : 0);
        float o = Anim(id + 3, on ? 1 : 0, 12);
        float p = Anim(id + 5, ActiveId == id && Down && Hit(r) ? 1 : 0, 24);
        float s = 1 - 0.035f * p;
        var rr = new Rect(r.CenterX - r.Width * s / 2, r.CenterY - r.Height * s / 2, r.Width * s, r.Height * s);
        G.FillRound(rr, 16, Theme.Surface.WithA(1 + h * 0.8f));
        if (o > 0.01f) G.FillRound(rr, 16, onColor.WithA(0.20f * o));
        var ic = new Vector2(rr.X + 24, rr.CenterY);
        G.FillCircle(ic, 15, Theme.Mix(Theme.SurfaceHi, onColor, o));
        G.Icon(glyph, ic, 14, Theme.Mix(Theme.Text, new Color4(0.02f, 0.02f, 0.03f, 1), o));
        G.Text(label, new Rect(rr.X + 46, rr.Y + (sub == null ? 0 : -8), rr.Width - 50, rr.Height), 13, Theme.Text, FontWeight.SemiBold);
        if (sub != null) G.Text(sub, new Rect(rr.X + 46, rr.Y + 9, rr.Width - 50, rr.Height), 11, Theme.Text2);
        return clicked;
    }

    /// <summary>Küçük hap butonu (metin).</summary>
    public bool Pill(int id, Rect r, string text, Color4 bg, Color4 fg, string? glyph = null)
    {
        bool clicked = Click(id, r);
        float h = Anim(id, Hit(r) ? 1 : 0);
        float p = Anim(id + 1, ActiveId == id && Down && Hit(r) ? 1 : 0, 24);
        float s = 1 - 0.05f * p;
        var rr = new Rect(r.CenterX - r.Width * s / 2, r.CenterY - r.Height * s / 2, r.Width * s, r.Height * s);
        G.FillRound(rr, rr.Height / 2, bg.WithA(1 + 0.5f * h));
        if (glyph != null)
        {
            G.Icon(glyph, new Vector2(rr.X + 16, rr.CenterY), 12, fg);
            G.Text(text, new Rect(rr.X + 28, rr.Y, rr.Width - 34, rr.Height), 12, fg, FontWeight.SemiBold);
        }
        else G.Text(text, rr, 12, fg, FontWeight.SemiBold, TextAlignment.Center);
        return clicked;
    }
}

/// <summary>Tek satırlık metin kutusu (Türkçe karakterler dahil).</summary>
public sealed class TextBox
{
    public string Text = "";
    public int Caret;
    public string Placeholder = "";

    public bool OnChar(char ch)
    {
        if (ch < 32 || ch == 127) return false;
        if (Text.Length >= 200) return true;
        Text = Text.Insert(Caret, ch.ToString());
        Caret++;
        return true;
    }

    /// <summary>true → Enter ile gönderildi.</summary>
    public bool OnKey(int vk, bool ctrl, Func<string?> paste)
    {
        switch (vk)
        {
            case 0x08: // backspace
                if (Caret > 0)
                {
                    int from = ctrl ? WordStart(Caret) : Caret - 1;
                    Text = Text.Remove(from, Caret - from); Caret = from;
                }
                break;
            case 0x2E: if (Caret < Text.Length) Text = Text.Remove(Caret, 1); break;
            case 0x25: Caret = Math.Max(0, ctrl ? WordStart(Caret) : Caret - 1); break;
            case 0x27: Caret = Math.Min(Text.Length, Caret + 1); break;
            case 0x24: Caret = 0; break;
            case 0x23: Caret = Text.Length; break;
            case 0x56 when ctrl:
                var p = paste()?.Replace("\r", " ").Replace("\n", " ");
                if (!string.IsNullOrEmpty(p)) { if (p.Length > 200) p = p[..200]; Text = Text.Insert(Caret, p); Caret += p.Length; }
                break;
            case 0x0D: return true;
        }
        return false;
    }

    int WordStart(int i)
    {
        while (i > 0 && Text[i - 1] == ' ') i--;
        while (i > 0 && Text[i - 1] != ' ') i--;
        return i;
    }

    public void Clear() { Text = ""; Caret = 0; }

    public void Draw(Ui ui, int id, Rect r, bool focused)
    {
        var g = ui.G;
        float f = ui.Anim(id, focused ? 1 : 0);
        float h = ui.Anim(id + 1, ui.Hit(r) ? 1 : 0);
        g.FillRound(r, 12, Theme.Surface.WithA(1 + h * 0.5f + f * 0.5f));
        g.StrokeRound(r, 12, Theme.Accent.WithA(0.15f + 0.55f * f), 1.2f);
        var tr = new Rect(r.X + 14, r.Y, r.Width - 28, r.Height);
        if (Text.Length == 0 && !focused) g.Text(Placeholder, tr, 13, Theme.Text3, FontWeight.Normal, font: Theme.TextFont);
        else if (Text.Length == 0) g.Text(Placeholder, tr, 13, Theme.Text3.WithA(0.6f), FontWeight.Normal, font: Theme.TextFont);
        // kaydırma: caret görünür kalsın
        float caretX = g.Measure(Text[..Caret], 13, FontWeight.Normal, Theme.TextFont);
        float off = MathF.Max(0, caretX - (tr.Width - 6));
        g.PushClip(tr);
        g.Text(Text, new Rect(tr.X - off, tr.Y, 4000, tr.Height), 13, Theme.Text, FontWeight.Normal, font: Theme.TextFont);
        if (focused)
        {
            // yanıp sönme 530 ms'lik zamanlayıcıyla sürülür (sürekli kare çizmeyiz)
            float blink = ((int)(ui.Time / 0.53f)) % 2 == 0 ? 1f : 0.15f;
            float cx = tr.X - off + caretX + 1;
            g.Line(new Vector2(cx, r.CenterY - 9), new Vector2(cx, r.CenterY + 9), Theme.Accent.WithA(blink), 1.6f);
        }
        g.PopClip();
    }
}
