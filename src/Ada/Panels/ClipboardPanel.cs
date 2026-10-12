using System.Numerics;
using Ada.Core;
using Ada.Data;
using Vortice.DirectWrite;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Panels;

public sealed class ClipboardPanel : Panel
{
    Spring _scroll = new(0, 260, 32);

    public ClipboardPanel(Island i) : base(i) { }

    public override void OnShow() => _scroll.Snap(0);

    public override void Draw(Rect a)
    {
        var items = I.Clip.History.Items;
        // başlık
        G.Text("Pano geçmişi", new Rect(a.X + 4, a.Y, 200, 26), 15, Theme.Text, FontWeight.Bold);
        G.Text(items.Count == 0 ? "" : $"{items.Count} öğe · yalnızca bu oturumda saklanır", new Rect(a.X + 120, a.Y + 1, 300, 26), 11.5f, Theme.Text3, FontWeight.Normal, font: Theme.TextFont);
        if (items.Count > 0 && U.Pill(Ui.Id("cb.clear"), new Rect(a.Right - 92, a.Y, 92, 26), "Temizle", Theme.Surface, Theme.Text2, Glyph.Clear))
            I.Clip.History.Clear();

        var list = new Rect(a.X, a.Y + 36, a.Width, a.Height - 36);
        if (items.Count == 0)
        {
            Empty(list, Glyph.Clipboard, "Henüz bir şey kopyalanmadı", "Kopyaladığın metinler burada birikir; tıkla, tekrar kopyala");
            return;
        }

        const float rowH = 46, gap = 6;
        float content = items.Count * (rowH + gap) - gap;
        float maxScroll = MathF.Max(0, content - list.Height);
        if (U.Hit(list) && U.Wheel != 0) _scroll.Target = Math.Clamp(_scroll.Target - U.Wheel * 90, 0, maxScroll);
        _scroll.Target = Math.Clamp(_scroll.Target, 0, maxScroll);
        _scroll.Step(U.Dt);
        if (!_scroll.Settled) U.NeedsFrame = true;

        G.PushClip(list);
        ClipItem? remove = null;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            float y = list.Y + i * (rowH + gap) - _scroll.Value;
            if (y > list.Bottom || y + rowH < list.Y) continue;
            var r = new Rect(list.X, y, list.Width, rowH);
            int id = Ui.Id("cb.row", it.GetHashCode());
            bool hit = U.Hit(r) && U.Hit(list);
            float h = U.Anim(id, hit ? 1 : 0);
            G.FillRound(r, 14, Theme.Surface.WithA(1 + 1.2f * h));
            if (h > 0.01f) G.FillRound(new Rect(r.X, r.Y + 12, 3, rowH - 24), 1.5f, Theme.Mint.WithA(h));

            var ic = new Vector2(r.X + 24, r.CenterY);
            G.FillCircle(ic, 13, Theme.Rgb(0xFFFFFF, 0.06f));
            G.Text((i + 1).ToString(), new Rect(ic.X - 13, ic.Y - 13, 26, 26), 11, Theme.Text2, FontWeight.Bold, TextAlignment.Center);
            G.Text(it.Preview, new Rect(r.X + 46, r.Y, r.Width - 46 - 110, rowH), 13, Theme.Text, FontWeight.Normal, font: Theme.TextFont);
            G.Text(Ago(it.Time), new Rect(r.Right - 106, r.Y, 66, rowH), 11, Theme.Text3, FontWeight.SemiBold, TextAlignment.Trailing);

            // sil
            var del = new Vector2(r.Right - 22, r.CenterY);
            if (h > 0.01f)
            {
                G.Alpha *= h;
                bool d = U.IconButton(Ui.Id("cb.del", it.GetHashCode()), del, 13, Glyph.Delete, 11, Theme.Red);
                G.Alpha /= h;
                if (d) { remove = it; continue; }
            }
            if (U.Click(id, r) && !U.Hit(new Rect(del.X - 14, del.Y - 14, 28, 28)))
            {
                I.Clip.History.Add(it.Text, DateTime.Now);
                I.Clip.SetText(it.Text);
                I.Collapse();
                I.Toast("Panoya kopyalandı", it.Preview, Glyph.Check, Theme.Mint, 3);
            }
            if (U.RightClick(r)) remove = it;
        }
        G.PopClip();
        if (remove != null) I.Clip.History.Remove(remove);

        // kaydırma ipucu: alt kenarda solma
        if (maxScroll > 0 && _scroll.Value < maxScroll - 2)
            G.VGradientRound(new Rect(list.X, list.Bottom - 26, list.Width, 26), 0, Theme.Island.WithA(0), Theme.Island);
    }

    static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalSeconds < 60) return "şimdi";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} dk";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} sa";
        return t.ToString("d MMM", Island.Tr);
    }
}
