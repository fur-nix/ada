using System.Numerics;
using Ada.Core;
using Ada.Data;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Panels;

/// <summary>Bugün: büyük saat, haftalık şerit, yaklaşanlar, pomodoro, yapılacaklar.</summary>
public sealed class TodayPanel : Panel
{
    readonly TextBox _box = new() { Placeholder = "Görev ekle…  (18:30 toplantı, 30dk çay)" };
    static readonly int BoxId = Ui.Id("today.box");
    static readonly int[] Presets = { 5, 10, 15, 25, 45 };
    int _preset = 3;
    DateTime? _end;
    TimeSpan _remain = TimeSpan.FromMinutes(25);
    Spring _scroll = new(0, 260, 32);

    public TodayPanel(Island i) : base(i) { }

    public override string Name => "Bugün";
    public override string Glyph => Core.Glyph.Calendar;

    public override void Tick() => I.Invalidate();

    /// <summary>Panel kapalıyken de çalışır: pomodoro bitişi.</summary>
    public void BackgroundTick()
    {
        if (_end is DateTime e && DateTime.Now >= e)
        {
            _end = null; _remain = TimeSpan.Zero;
            I.Toast("Süre doldu", $"{Presets[_preset]} dakikalık odak tamamlandı", Core.Glyph.Clock, Theme.Orange, 1, 8f);
            I.Beep();
        }
    }

    public string? TimerBadge => _end is DateTime e ? Fmt(e - DateTime.Now) : null;

    protected override void OnSubmit(TextBox box)
    {
        if (string.IsNullOrWhiteSpace(box.Text)) return;
        var (text, due) = ReminderParser.Parse(box.Text, DateTime.Now);
        Store.Current.Todos.Add(new TodoItem { Text = text, Due = due });
        Store.Save();
        box.Clear();
    }

    public override void Draw(Rect a)
    {
        var now = DateTime.Now;
        // ---- büyük saat ----
        G.Text(now.ToString("HH"), new Rect(a.X, a.Y - 4, 110, 56), 50, Theme.Text, FontWeight.Black);
        G.Text(now.ToString("mm"), new Rect(a.X, a.Y + 50, 110, 56), 50, Theme.Accent, FontWeight.Black);
        G.Text(now.ToString("dddd", Island.Tr), new Rect(a.X, a.Y + 114, 110, 18), 12, Theme.Text2, FontWeight.SemiBold);
        G.Text(now.ToString("d MMMM", Island.Tr), new Rect(a.X, a.Y + 132, 110, 18), 12, Theme.Text3, FontWeight.SemiBold);
        I.Stats.SampleBatteryOnly();
        if (I.Stats.Battery >= 0)
            G.Text($"Pil %{I.Stats.Battery}", new Rect(a.X, a.Bottom - 20, 110, 18), 11, Theme.Text3, FontWeight.SemiBold);

        float mx = a.X + 122, rw = 256, mw = a.Width - 122 - rw - 14;
        var mid = new Rect(mx, a.Y, mw, a.Height);
        DrawWeek(new Rect(mid.X, mid.Y, mid.Width, 74));
        DrawUpcoming(new Rect(mid.X, mid.Y + 82, mid.Width, 46));
        DrawPomodoro(new Rect(mid.X, mid.Y + 136, mid.Width, mid.Height - 136));
        DrawTodos(new Rect(a.Right - rw, a.Y, rw, a.Height));
    }

    void DrawWeek(Rect r)
    {
        Card(r, 16);
        var today = DateTime.Today;
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        G.Text("Bu hafta", new Rect(r.X + 14, r.Y + 8, 120, 18), 12, Theme.Text, FontWeight.Bold);
        G.Text($"{monday:d MMM} – {monday.AddDays(6):d MMM}".Replace(".", ""), new Rect(r.Right - 140, r.Y + 8, 126, 18), 10.5f, Theme.Text3, FontWeight.SemiBold, TextAlignment.Trailing);
        string[] wd = { "Pt", "Sa", "Ça", "Pe", "Cu", "Ct", "Pz" };
        float cw = (r.Width - 20) / 7;
        var due = Store.Current.Todos.Where(t => !t.Done && t.Due != null).Select(t => t.Due!.Value.Date).ToHashSet();
        for (int i = 0; i < 7; i++)
        {
            var d = monday.AddDays(i);
            var c = new Vector2(r.X + 10 + cw * i + cw / 2, r.Y + 50);
            bool isToday = d == today;
            if (isToday) G.FillRound(new Rect(c.X - 15, c.Y - 18, 30, 36), 10, Theme.Accent);
            var fg = isToday ? Theme.TileOnText : d < today ? Theme.Text3 : Theme.Text;
            G.Text(wd[i], new Rect(c.X - 15, c.Y - 18, 30, 16), 9.5f, fg.WithA(0.75f), FontWeight.SemiBold, TextAlignment.Center);
            G.Text(d.Day.ToString(), new Rect(c.X - 15, c.Y - 3, 30, 18), 12.5f, fg, FontWeight.Bold, TextAlignment.Center);
            if (due.Contains(d)) G.FillCircle(new Vector2(c.X, c.Y + 21), 2, isToday ? Theme.Accent : Theme.Orange);
        }
    }

    void DrawUpcoming(Rect r)
    {
        Card(r, 14);
        var next = Store.Current.Todos.Where(t => !t.Done && t.Due >= DateTime.Now).OrderBy(t => t.Due).FirstOrDefault();
        G.FillRound(new Rect(r.X + 10, r.Y + 10, 4, r.Height - 20), 2, next == null ? Theme.Red : Theme.Orange);
        if (next == null)
        {
            G.Text("Yaklaşan bir şey yok", new Rect(r.X + 24, r.Y + 6, r.Width - 30, 18), 12.5f, Theme.Text, FontWeight.Bold);
            G.Text("Takvimin boş", new Rect(r.X + 24, r.Y + 23, r.Width - 30, 16), 11, Theme.Text3, FontWeight.SemiBold);
        }
        else
        {
            G.Text(next.Text, new Rect(r.X + 24, r.Y + 6, r.Width - 30, 18), 12.5f, Theme.Text, FontWeight.Bold);
            G.Text(ReminderParser.Describe(next.Due!.Value, DateTime.Now), new Rect(r.X + 24, r.Y + 23, r.Width - 30, 16), 11, Theme.Orange, FontWeight.SemiBold);
        }
    }

    void DrawPomodoro(Rect r)
    {
        Card(r, 16);
        var c = new Vector2(r.X + 50, r.CenterY);
        float total = Presets[_preset] * 60f;
        var remain = _end is DateTime e ? e - DateTime.Now : _remain;
        if (remain < TimeSpan.Zero) remain = TimeSpan.Zero;
        float frac = Math.Clamp((float)remain.TotalSeconds / total, 0, 1);
        G.StrokeCircle(c, 34, Theme.Rgb(0xFFFFFF, 0.08f), 5);
        G.Arc(c, 34, 0, 360 * frac, Theme.Orange, 5);
        G.Text(Fmt(remain), new Rect(c.X - 34, c.Y - 11, 68, 22), 15, Theme.Text, FontWeight.Bold, TextAlignment.Center);

        float x = r.X + 100, w = r.Right - x - 12;
        G.Text("Odak zamanlayıcı", new Rect(x, r.Y + 10, w, 18), 11.5f, Theme.Text2, FontWeight.SemiBold);
        var labels = Presets.Select(p => p + "dk").ToArray();
        int sel = U.Chips(Ui.Id("pomo.chips"), new Rect(x, r.Y + 32, w, 26), labels, _preset, Theme.Orange);
        if (sel != _preset && _end == null) { _preset = sel; _remain = TimeSpan.FromMinutes(Presets[sel]); }
        float by = r.Bottom - 40;
        bool running = _end != null;
        if (U.Pill(Ui.Id("pomo.start"), new Rect(x, by, (w - 8) / 2, 30), running ? "Duraklat" : "Başlat", Theme.Orange, Theme.Rgb(0x2A1608)))
        {
            if (running) { _remain = _end!.Value - DateTime.Now; _end = null; }
            else { if (_remain <= TimeSpan.Zero) _remain = TimeSpan.FromMinutes(Presets[_preset]); _end = DateTime.Now + _remain; }
        }
        if (U.Pill(Ui.Id("pomo.reset"), new Rect(x + (w - 8) / 2 + 8, by, (w - 8) / 2, 30), "Sıfırla", Theme.Surface, Theme.Text))
        { _end = null; _remain = TimeSpan.FromMinutes(Presets[_preset]); }
    }

    void DrawTodos(Rect r)
    {
        Card(r, 16);
        var todos = Store.Current.Todos;
        int open = todos.Count(t => !t.Done);
        G.Text("Bugün", new Rect(r.X + 14, r.Y + 8, 100, 20), 13, Theme.Text, FontWeight.Bold);
        G.Text(open > 0 ? $"{open} açık" : "", new Rect(r.Right - 90, r.Y + 9, 76, 18), 10.5f, Theme.Accent, FontWeight.SemiBold, TextAlignment.Trailing);

        var list = new Rect(r.X + 8, r.Y + 34, r.Width - 16, r.Height - 34 - 46);
        var ordered = todos.OrderBy(t => t.Done).ThenBy(t => t.Due ?? DateTime.MaxValue).ThenByDescending(t => t.Created).ToList();
        const float rowH = 30;
        float maxScroll = MathF.Max(0, ordered.Count * rowH - list.Height);
        if (U.Hit(list) && U.Wheel != 0) _scroll.Target = Math.Clamp(_scroll.Target - U.Wheel * 60, 0, maxScroll);
        _scroll.Target = Math.Clamp(_scroll.Target, 0, maxScroll);
        _scroll.Step(U.Dt);
        if (!_scroll.Settled) U.NeedsFrame = true;

        if (ordered.Count == 0)
            G.Text("Liste boş — aşağıdan ekle", list, 11.5f, Theme.Text3, FontWeight.SemiBold, TextAlignment.Center);
        TodoItem? remove = null; bool changed = false;
        G.PushClip(list);
        for (int i = 0; i < ordered.Count; i++)
        {
            var t = ordered[i];
            int key = t.Created.GetHashCode() ^ t.Text.GetHashCode();
            float y = list.Y + U.Anim(Ui.Id("td.y", key), i * rowH, 14) - _scroll.Value;
            if (y > list.Bottom || y + rowH < list.Y) continue;
            var row = new Rect(list.X, y, list.Width, rowH - 2);
            bool hit = U.Hit(row) && U.Hit(list);
            float h = U.Anim(Ui.Id("td.h", key), hit ? 1 : 0);
            float done = U.Anim(Ui.Id("td.d", key), t.Done ? 1 : 0, 12);
            if (h > 0.01f) G.FillRound(row, 9, Theme.Surface.WithA(h));
            var cb = new Vector2(row.X + 14, row.CenterY);
            if (U.Click(Ui.Id("td.cb", key), new Rect(cb.X - 12, cb.Y - 12, 24, 24))) { t.Done = !t.Done; changed = true; }
            G.StrokeCircle(cb, 7, Theme.Mix(Theme.Text3, Theme.Accent, done), 1.5f);
            if (done > 0.01f) { G.FillCircle(cb, 7 * done, Theme.Accent); G.Icon(Core.Glyph.Check, cb, 8 * done, Theme.TileOnText); }
            float tw = row.Width - 36 - (t.Due != null ? 46 : 0) - (h > 0.01f ? 22 : 0);
            G.Text(t.Text, new Rect(row.X + 28, row.Y, tw, row.Height), 12, Theme.Mix(Theme.Text, Theme.Text3, done), FontWeight.Medium, font: Theme.TextFont);
            if (done > 0.01f)
            {
                float lw = MathF.Min(tw, G.Measure(t.Text, 12, FontWeight.Medium, Theme.TextFont)) * done;
                G.Line(new Vector2(row.X + 28, row.CenterY + 1), new Vector2(row.X + 28 + lw, row.CenterY + 1), Theme.Text3, 1.2f);
            }
            if (t.Due is DateTime d)
                G.Text(d.ToString("HH:mm"), new Rect(row.Right - 46 - (h > 0.01f ? 22 : 0), row.Y, 42, row.Height), 10.5f,
                    !t.Done && d < DateTime.Now ? Theme.Red : Theme.Orange, FontWeight.SemiBold, TextAlignment.Trailing);
            if (h > 0.01f)
            {
                G.Alpha *= h;
                bool del = U.IconButton(Ui.Id("td.del", key), new Vector2(row.Right - 12, row.CenterY), 10, Core.Glyph.Delete, 9, Theme.Red);
                G.Alpha /= h;
                if (del) remove = t;
            }
        }
        G.PopClip();
        if (remove != null) { todos.Remove(remove); changed = true; }
        if (changed) Store.Save();

        var box = new Rect(r.X + 8, r.Bottom - 42, r.Width - 54, 34);
        Box(_box, BoxId, box);
        if (U.IconButton(Ui.Id("today.add"), new Vector2(r.Right - 26, box.CenterY), 15, Core.Glyph.Add, 12, Theme.TileOnText, true, Theme.Accent))
        {
            if (_box.Text.Length > 0) OnSubmit(_box); else Focus(_box, BoxId);
        }
    }

    static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
}
