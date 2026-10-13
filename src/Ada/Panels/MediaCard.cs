using System.Numerics;
using Ada.Core;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Panels;

/// <summary>Albüm renginde müzik kartı: kapak, başlık, dalgalı ilerleme, turuncu oynat butonu.</summary>
public sealed class MediaCard
{
    readonly Island I;
    Gfx G => I.G;
    Ui U => I.U;
    ID2D1Bitmap? _oldArt;
    float _artFade = 1;

    public MediaCard(Island i) => I = i;

    public void PushOldArt(ID2D1Bitmap? old) { _oldArt?.Dispose(); _oldArt = old; _artFade = 0; }

    public void Draw(Rect r)
    {
        var m = I.Media;
        var bg = Theme.Mix(I.MediaColor, Theme.Island, m.HasSession ? 0.70f : 0.9f);
        G.FillRound(r, 18, bg);
        G.StrokeRound(r, 18, Theme.Rgb(0xFFFFFF, 0.05f), 1);
        if (!m.HasSession)
        {
            G.Icon(Glyph.Music, new Vector2(r.X + 34, r.CenterY), 16, Theme.Text2);
            G.Text("Şu an bir şey çalmıyor", new Rect(r.X + 60, r.Y, r.Width - 80, r.Height), 13, Theme.Text2, FontWeight.SemiBold);
            return;
        }

        // kaynak
        G.Text("♪  " + m.Source, new Rect(r.X + 16, r.Y + 8, 240, 16), 10.5f, Theme.Text3, FontWeight.SemiBold);

        // kapak
        float s = MathF.Min(52, r.Height - 40);
        var art = new Rect(r.X + 16, r.Y + 28, s, s);
        if (_artFade < 1) { _artFade = MathF.Min(1, _artFade + U.Dt / 0.35f); U.NeedsFrame = true; }
        if (_oldArt != null && _artFade < 1) G.BitmapRound(_oldArt, art, 12, 1 - _artFade);
        if (I.MediaArt != null) G.BitmapRound(I.MediaArt, art, 12, Ease.OutCubic(_artFade));
        else { G.FillRound(art, 12, I.MediaColor.WithA(0.4f)); G.Icon(Glyph.Music, new Vector2(art.CenterX, art.CenterY), 16, Theme.Text); }

        // başlık
        float tx = art.Right + 14, tw = r.Right - tx - 150;
        G.Text(m.Title.ToUpper(Island.Tr), new Rect(tx, art.Y + 2, tw, 24), 16, Theme.Text, FontWeight.Black);
        G.Text(m.Artist, new Rect(tx, art.Y + 26, tw, 18), 12, Theme.Text2, FontWeight.Medium);

        // kontroller
        float cy = art.CenterY;
        if (U.IconButton(Ui.Id("mc.prev"), new Vector2(r.Right - 116, cy), 15, Glyph.Prev, 12, Theme.Text)) m.Prev();
        if (U.IconButton(Ui.Id("mc.play"), new Vector2(r.Right - 74, cy), 19, m.Playing ? Glyph.Pause : Glyph.Play, 13,
                Theme.Rgb(0x2A1608), true, Theme.Orange)) m.TogglePlay();
        if (U.IconButton(Ui.Id("mc.next"), new Vector2(r.Right - 32, cy), 15, Glyph.Next, 12, Theme.Text)) m.Next();

        // dalgalı ilerleme
        var pos = m.LivePosition; var dur = m.Duration;
        float frac = dur.TotalSeconds > 0 ? (float)(pos.TotalSeconds / dur.TotalSeconds) : 0;
        float py = r.Bottom - 16, x0 = r.X + 56, x1 = r.Right - 56;
        var bar = new Rect(x0, py - 8, x1 - x0, 16);
        int pid = Ui.Id("mc.prog");
        bool hov = U.Hit(bar) && dur.TotalSeconds > 0;
        if (hov) U.WantHand = true;
        if (hov && U.Pressed) U.ActiveId = pid;
        if (U.ActiveId == pid && U.Down) frac = Ease.Clamp01((U.Mouse.X - x0) / (x1 - x0));
        if (U.ActiveId == pid && U.Released) m.Seek(Ease.Clamp01((U.Mouse.X - x0) / (x1 - x0)));
        float xp = x0 + (x1 - x0) * frac;
        float amp = U.Anim(Ui.Id("mc.amp"), m.Playing ? 2.6f : 0f, 6);
        if (m.Playing) U.NeedsFrame = true; // dalga akar
        G.Wave(x0, xp, py, amp, 13, -U.Time * 5f, Theme.Text, 2.4f);
        G.Line(new Vector2(xp + 4, py), new Vector2(x1, py), Theme.Rgb(0xFFFFFF, 0.25f), 2.4f);
        G.FillRound(new Rect(xp - 2, py - 7, 4, 14), 2, Theme.Text);
        G.Text(Fmt(pos), new Rect(r.X + 10, py - 8, 42, 16), 10, Theme.Text2, FontWeight.SemiBold, TextAlignment.Trailing);
        G.Text(Fmt(dur), new Rect(x1 + 6, py - 8, 44, 16), 10, Theme.Text2, FontWeight.SemiBold);
    }

    static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
