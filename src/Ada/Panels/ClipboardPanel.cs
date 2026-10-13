using System.Numerics;
using Ada.Core;
using Ada.Data;
using Vortice.DirectWrite;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Panels;

/// <summary>Pano: arama, filtre, liste + önizleme.</summary>
public sealed class ClipboardPanel : Panel
{
    readonly TextBox _search = new() { Placeholder = "Panoda ara…" };
    static readonly int SearchId = Ui.Id("cb.search");
    Spring _scroll = new(0, 260, 32);
    int _filter;
    int _sel;

    public ClipboardPanel(Island i) : base(i) { }

    public override string Name => "Pano";
    public override string Glyph => Core.Glyph.Clipboard;

    public override void OnShow() { _scroll.Snap(0); _sel = 0; }

    static bool IsLink(string t) => t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("www.", StringComparison.OrdinalIgnoreCase);

    List<ClipItem> Filtered()
    {
        var q = _search.Text.Trim();
        return I.Clip.History.Items.Where(it =>
                (_filter == 0 || (_filter == 1 && !IsLink(it.Text)) || (_filter == 2 && IsLink(it.Text))) &&
                (q.Length == 0 || it.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();
    }

    protected override void OnTextChanged(TextBox box) { _sel = 0; _scroll.Target = 0; }
    protected override void OnSubmit(TextBox box) { var f = Filtered(); if (_sel < f.Count) Copy(f[_sel]); }

    public override bool OnKey(int vk, bool ctrl)
    {
        var f = Filtered();
        if (vk == 0x28) { _sel = Math.Min(f.Count - 1, _sel + 1); return true; }
        if (vk == 0x26) { _sel = Math.Max(0, _sel - 1); return true; }
        if (base.OnKey(vk, ctrl)) return true;
        if (vk == 0x0D && _sel < f.Count) { Copy(f[_sel]); return true; }
        return false;
    }

    void Copy(ClipItem it)
    {
        I.Clip.History.Add(it.Text, DateTime.Now);
        I.Clip.SetText(it.Text);
        I.Collapse();
        I.Toast("Panoya kopyalandı", it.Preview, Core.Glyph.Check, Theme.Accent, 3);
    }

    public override void Draw(Rect a)
    {
        var all = I.Clip.History.Items;
        var items = Filtered();
        if (_sel >= items.Count) _sel = Math.Max(0, items.Count - 1);

        // üst çubuk
        Box(_search, SearchId, new Rect(a.X, a.Y, a.Width - 300, 34));
        G.Icon("", new Vector2(a.X + a.Width - 300 - 18, a.Y + 17), 11, Theme.Text3);
        int f = U.Chips(Ui.Id("cb.filter"), new Rect(a.Right - 288, a.Y + 3, 200, 28), new[] { "Tümü", "Metin", "Bağlantı" }, _filter, Theme.Accent);
        if (f != _filter) { _filter = f; _sel = 0; _scroll.Snap(0); }
        if (all.Count > 0 && U.Pill(Ui.Id("cb.clear"), new Rect(a.Right - 80, a.Y + 3, 80, 28), "Temizle", Theme.Surface, Theme.Text2))
            I.Clip.History.Clear();

        var body = new Rect(a.X, a.Y + 44, a.Width, a.Height - 44);
        if (items.Count == 0)
        {
            Empty(body, Core.Glyph.Clipboard, all.Count == 0 ? "Henüz bir şey kopyalanmadı" : "Eşleşen öğe yok",
                all.Count == 0 ? "Kopyaladığın metinler burada birikir" : "Aramayı ya da filtreyi değiştir");
            return;
        }

        var list = new Rect(body.X, body.Y, body.Width * 0.58f, body.Height);
        const float rowH = 34;
        float maxScroll = MathF.Max(0, items.Count * rowH - list.Height);
        if (U.Hit(list) && U.Wheel != 0) _scroll.Target = Math.Clamp(_scroll.Target - U.Wheel * 70, 0, maxScroll);
        float selY = _sel * rowH;
        if (selY - _scroll.Target > list.Height - rowH) _scroll.Target = selY - list.Height + rowH;
        if (selY < _scroll.Target) _scroll.Target = selY;
        _scroll.Target = Math.Clamp(_scroll.Target, 0, maxScroll);
        _scroll.Step(U.Dt);
        if (!_scroll.Settled) U.NeedsFrame = true;

        G.Text("SON KOPYALANANLAR", new Rect(list.X + 4, list.Y - 2, 200, 14), 9.5f, Theme.Text3, FontWeight.Bold);
        var inner = new Rect(list.X, list.Y + 16, list.Width, list.Height - 16);
        G.PushClip(inner);
        ClipItem? remove = null;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            float y = inner.Y + i * rowH - _scroll.Value;
            if (y > inner.Bottom || y + rowH < inner.Y) continue;
            var r = new Rect(inner.X, y, inner.Width, rowH - 3);
            int id = Ui.Id("cb.row", it.GetHashCode());
            bool hit = U.Hit(r) && U.Hit(inner);
            if (hit) _sel = U.Pressed ? i : _sel;
            float h = U.Anim(id, hit || i == _sel ? 1 : 0);
            if (h > 0.01f) G.FillRound(r, 9, Theme.Surface.WithA(0.6f + h));
            bool link = IsLink(it.Text);
            G.Icon(link ? "" : "", new Vector2(r.X + 16, r.CenterY), 11, link ? Theme.Sky : Theme.Text2);
            G.Text(it.Preview, new Rect(r.X + 32, r.Y, r.Width - 80, r.Height), 12, Theme.Text, FontWeight.SemiBold, font: Theme.TextFont);
            G.Text(Ago(it.Time), new Rect(r.Right - 50, r.Y, 42, r.Height), 10.5f, Theme.Text3, FontWeight.SemiBold, TextAlignment.Trailing);
            if (U.Click(id, r) && U.Hit(inner)) { _sel = i; Copy(it); }
            if (U.RightClick(r)) remove = it;
        }
        G.PopClip();
        if (remove != null) I.Clip.History.Remove(remove);

        // önizleme
        var pv = new Rect(list.Right + 14, body.Y, body.Right - list.Right - 14, body.Height);
        Card(pv, 16);
        if (_sel < items.Count)
        {
            var it = items[_sel];
            bool link = IsLink(it.Text);
            var tr = new Rect(pv.X + 14, pv.Y + 12, pv.Width - 28, pv.Height - 92);
            G.PushClip(tr);
            G.Text(it.Text.Length > 600 ? it.Text[..600] + "…" : it.Text, tr, 12.5f, Theme.Text, FontWeight.Normal,
                TextAlignment.Leading, ParagraphAlignment.Near, Theme.TextFont, wrap: true);
            G.PopClip();
            float y = pv.Bottom - 74;
            G.Line(new Vector2(pv.X + 14, y), new Vector2(pv.Right - 14, y), Theme.Rgb(0xFFFFFF, 0.06f), 1);
            Meta(pv, y + 8, "Tür", link ? "Bağlantı" : "Metin");
            Meta(pv, y + 26, "Boyut", $"{it.Text.Length} karakter");
            Meta(pv, y + 44, "Kopyalandı", it.Time.ToString("HH:mm"));
        }
        G.Text("Enter: kopyala · ↑↓: seç · sağ tık: sil", new Rect(a.X, a.Bottom + 2, a.Width, 14), 10, Theme.Text3, FontWeight.Normal, TextAlignment.Trailing, font: Theme.TextFont);
    }

    void Meta(Rect pv, float y, string k, string v)
    {
        G.Text(k, new Rect(pv.X + 14, y, 90, 16), 10.5f, Theme.Text3, FontWeight.SemiBold);
        G.Text(v, new Rect(pv.X + 100, y, pv.Width - 114, 16), 10.5f, Theme.Text2, FontWeight.SemiBold);
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
