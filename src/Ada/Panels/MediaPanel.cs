using System.Numerics;
using Ada.Core;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Panels;

public sealed class MediaPanel : Panel
{
    ID2D1Bitmap? _oldArt;
    float _artFade = 1;

    public MediaPanel(Island i) : base(i) { }

    /// <summary>Kapak değişince eskisiyle çapraz geçiş yap.</summary>
    public void PushOldArt(ID2D1Bitmap? old)
    {
        _oldArt?.Dispose();
        _oldArt = old;
        _artFade = 0;
    }

    public override void Tick() => I.Invalidate(); // ilerleme çubuğu

    public override void Draw(Rect a)
    {
        var m = I.Media;
        if (!m.HasSession)
        {
            Empty(a, Glyph.Music, "Şu an bir şey çalmıyor", "Spotify, YouTube ya da herhangi bir oynatıcıda müzik başlat");
            return;
        }

        // ---- kapak ----
        const float S = 168;
        var art = new Rect(a.X + 6, a.Y + (a.Height - S) / 2, S, S);
        float playK = U.Anim(Ui.Id("m.playk"), m.Playing ? 1 : 0, 10);
        // çalmıyorken kapak hafifçe küçülür (Apple Music hissi)
        float sc = 0.92f + 0.08f * playK;
        var ar = new Rect(art.CenterX - S * sc / 2, art.CenterY - S * sc / 2, S * sc, S * sc);
        G.Shadow(ar, 20, 16, 0.55f + 0.2f * playK, 8);
        if (_artFade < 1) { _artFade = MathF.Min(1, _artFade + U.Dt / 0.35f); U.NeedsFrame = true; }
        if (_oldArt != null && _artFade < 1) G.BitmapRound(_oldArt, ar, 20, 1 - _artFade);
        if (I.MediaArt != null) G.BitmapRound(I.MediaArt, ar, 20, Ease.OutCubic(_artFade));
        else
        {
            G.VGradientRound(ar, 20, I.MediaColor.WithA(0.55f), Theme.Rgb(0x1A1A22));
            G.Icon(Glyph.Music, new Vector2(ar.CenterX, ar.CenterY), 40, Theme.Text.WithA(0.85f));
        }
        G.StrokeRound(ar, 20, Theme.Rgb(0xFFFFFF, 0.08f), 1);

        // ---- metinler ----
        float x = art.Right + 30, w = a.Right - x - 6;
        float y = a.Y + 18;
        var src = m.Source.ToUpperInvariant();
        G.FillCircle(new Vector2(x + 4, y + 9), 4, I.MediaColor);
        G.Text(src, new Rect(x + 14, y, w - 14, 18), 11, I.MediaColor, FontWeight.Bold);
        if (m.Playing) I.DrawBars(new Vector2(a.Right - 22, y + 9), 14, I.MediaColor, true);
        if (m.Playing) U.NeedsFrame = true; // çubuk animasyonu (sadece açık + medya sekmesinde)
        y += 28;
        G.Text(m.Title, new Rect(x, y, w, 32), 24, Theme.Text, FontWeight.Bold);
        y += 34;
        G.Text(m.Artist, new Rect(x, y, w, 22), 15, Theme.Text2, FontWeight.Medium);
        y += 44;

        // ---- ilerleme ----
        var pos = m.LivePosition; var dur = m.Duration;
        float frac = dur.TotalSeconds > 0 ? (float)(pos.TotalSeconds / dur.TotalSeconds) : 0;
        var bar = new Rect(x, y, w, 18);
        int pid = Ui.Id("m.prog");
        bool hov = U.Hit(bar) && dur.TotalSeconds > 0;
        if (hov) U.WantHand = true;
        if (hov && U.Pressed) U.ActiveId = pid;
        bool drag = U.ActiveId == pid && U.Down;
        if (drag) frac = Ease.Clamp01((U.Mouse.X - bar.X) / bar.Width);
        if (U.ActiveId == pid && U.Released) m.Seek(Ease.Clamp01((U.Mouse.X - bar.X) / bar.Width));
        float hv = U.Anim(pid, hov || drag ? 1 : 0, 18);
        float th = 5 + 3 * hv;
        var track = new Rect(x, bar.CenterY - th / 2, w, th);
        G.FillRound(track, th / 2, Theme.Rgb(0xFFFFFF, 0.13f));
        if (frac > 0) G.HGradientRound(new Rect(x, track.Y, MathF.Max(th, w * frac), th), th / 2, I.MediaColor.WithA(0.75f), Theme.Text);
        if (hv > 0.01f) G.FillCircle(new Vector2(x + w * frac, track.CenterY), 6.5f * hv, Theme.Text);
        y += 22;
        var shown = drag ? TimeSpan.FromSeconds(dur.TotalSeconds * frac) : pos;
        G.Text(Fmt(shown), new Rect(x, y, 80, 16), 11, Theme.Text3, FontWeight.SemiBold);
        G.Text(dur.TotalSeconds > 0 ? "-" + Fmt(dur - shown) : "", new Rect(x + w - 80, y, 80, 16), 11, Theme.Text3, FontWeight.SemiBold, TextAlignment.Trailing);

        // ---- kontroller ----
        float cy = a.Bottom - 36;
        float ccx = x + w / 2;
        if (U.IconButton(Ui.Id("m.prev"), new Vector2(ccx - 72, cy), 22, Glyph.Prev, 18, Theme.Text)) m.Prev();
        if (U.IconButton(Ui.Id("m.play"), new Vector2(ccx, cy), 28, m.Playing ? Glyph.Pause : Glyph.Play, 20,
                new Color4(0.03f, 0.03f, 0.04f, 1), true, Theme.Text)) m.TogglePlay();
        if (U.IconButton(Ui.Id("m.next"), new Vector2(ccx + 72, cy), 22, Glyph.Next, 18, Theme.Text)) m.Next();
    }

    static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
