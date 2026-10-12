using System.Numerics;
using Ada.Core;
using Ada.Data;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Panels;

public sealed class TasksPanel : Panel
{
    readonly TextBox _box = new() { Placeholder = "Yeni görev…  örn. 18:30 toplantı · 30dk çay · yarın 9:00 spor" };
    DateTime _month = new(DateTime.Now.Year, DateTime.Now.Month, 1);
    Spring _scroll = new(0, 260, 32);
    static readonly int BoxId = Ui.Id("tasks.box");

    public TasksPanel(Island i) : base(i) { }

    public override void OnShow() { _month = new(DateTime.Now.Year, DateTime.Now.Month, 1); _scroll.Snap(0); }
    public override void OnHide() => U.FocusId = 0;

    public override bool OnChar(char c)
    {
        if (U.FocusId != BoxId) return false;
        return _box.OnChar(c);
    }

    public override bool OnKey(int vk, bool ctrl)
    {
        if (U.FocusId != BoxId) return false;
        if (vk == 0x1B) { U.FocusId = 0; I.EndKeyboard(true); return true; }
        if (_box.OnKey(vk, ctrl, () => Services.ClipboardService.ReadText(I.Hwnd))) Submit();
        return true;
    }

    void Submit()
    {
        if (string.IsNullOrWhiteSpace(_box.Text)) return;
        var (text, due) = ReminderParser.Parse(_box.Text, DateTime.Now);
        Store.Current.Todos.Add(new TodoItem { Text = text, Due = due });
        Store.Save();
        _box.Clear();
    }

    public override void Draw(Rect a)
    {
        DrawCalendar(new Rect(a.X, a.Y, 236, a.Height));
        DrawTodos(new Rect(a.X + 236 + 14, a.Y, a.Width - 250, a.Height));
    }

    void DrawCalendar(Rect r)
    {
        Card(r);
        var tr = Island.Tr;
        string title = _month.ToString("MMMM yyyy", tr);
        title = char.ToUpper(title[0], tr) + title[1..];
        G.Text(title, new Rect(r.X + 16, r.Y + 10, r.Width - 90, 26), 14, Theme.Text, FontWeight.Bold);
        if (U.IconButton(Ui.Id("cal.prev"), new Vector2(r.Right - 50, r.Y + 23), 13, Glyph.Left, 10, Theme.Text2)) _month = _month.AddMonths(-1);
        if (U.IconButton(Ui.Id("cal.next"), new Vector2(r.Right - 22, r.Y + 23), 13, Glyph.Right, 10, Theme.Text2)) _month = _month.AddMonths(1);

        string[] wd = { "Pt", "Sa", "Ça", "Pe", "Cu", "Ct", "Pz" };
        float gx = r.X + 12, gw = (r.Width - 24) / 7, gy = r.Y + 44;
        for (int i = 0; i < 7; i++)
            G.Text(wd[i], new Rect(gx + i * gw, gy, gw, 18), 10.5f, i >= 5 ? Theme.Text3 : Theme.Text2, FontWeight.SemiBold, TextAlignment.Center);

        int offset = ((int)_month.DayOfWeek + 6) % 7;
        int days = DateTime.DaysInMonth(_month.Year, _month.Month);
        var today = DateTime.Today;
        var dueDays = new HashSet<int>();
        foreach (var t in Store.Current.Todos)
            if (!t.Done && t.Due is DateTime d && d.Year == _month.Year && d.Month == _month.Month) dueDays.Add(d.Day);

        float ch = (r.Bottom - gy - 22 - 8) / 6;
        for (int d = 1; d <= days; d++)
        {
            int idx = offset + d - 1;
            int row = idx / 7, col = idx % 7;
            var c = new Vector2(gx + col * gw + gw / 2, gy + 22 + row * ch + ch / 2);
            var date = new DateTime(_month.Year, _month.Month, d);
            bool isToday = date == today;
            if (isToday)
            {
                G.FillCircle(c, MathF.Min(gw, ch) / 2 - 1, Theme.Accent);
            }
            var col2 = isToday ? new Color4(0.02f, 0.02f, 0.04f, 1) : col >= 5 ? Theme.Text2 : Theme.Text;
            if (date < today) col2 = col2.WithA(isToday ? 1 : 0.45f);
            G.Text(d.ToString(), new Rect(c.X - gw / 2, c.Y - ch / 2, gw, ch), 11.5f, col2, isToday ? FontWeight.Bold : FontWeight.Medium, TextAlignment.Center);
            if (dueDays.Contains(d)) G.FillCircle(new Vector2(c.X, c.Y + ch / 2 - 2), 1.8f, isToday ? Theme.Text : Theme.Amber);
        }
    }

    void DrawTodos(Rect r)
    {
        // giriş kutusu
        var box = new Rect(r.X, r.Y, r.Width, 40);
        bool focused = U.FocusId == BoxId;
        if (U.Click(BoxId, box))
        {
            U.FocusId = BoxId;
            _box.Caret = _box.Text.Length;
            I.BeginKeyboard();
        }
        else if (U.Pressed && focused && !U.Hit(box)) { U.FocusId = 0; I.EndKeyboard(true); }
        _box.Draw(U, BoxId, box, U.FocusId == BoxId);

        var todos = Store.Current.Todos;
        var list = new Rect(r.X, box.Bottom + 10, r.Width, r.Bottom - box.Bottom - 10);
        if (todos.Count == 0)
        {
            Empty(list, Glyph.Tasks, "Liste boş", "Yaz ve Enter'a bas; saat yazarsan hatırlatırım");
            return;
        }

        var ordered = todos
            .OrderBy(t => t.Done)
            .ThenBy(t => t.Due ?? DateTime.MaxValue)
            .ThenByDescending(t => t.Created)
            .ToList();

        const float rowH = 42, gap = 6;
        float content = ordered.Count * (rowH + gap) - gap;
        float maxScroll = MathF.Max(0, content - list.Height);
        if (U.Hit(list) && U.Wheel != 0) _scroll.Target = Math.Clamp(_scroll.Target - U.Wheel * 80, 0, maxScroll);
        _scroll.Target = Math.Clamp(_scroll.Target, 0, maxScroll);
        _scroll.Step(U.Dt);
        if (!_scroll.Settled) U.NeedsFrame = true;

        TodoItem? remove = null;
        bool changed = false;
        G.PushClip(list);
        for (int i = 0; i < ordered.Count; i++)
        {
            var t = ordered[i];
            int key = t.Created.GetHashCode() ^ t.Text.GetHashCode();
            // sıralama değişince satırlar yumuşakça kayar
            float targetY = i * (rowH + gap);
            float y = list.Y + U.Anim(Ui.Id("td.y", key), targetY, 14) - _scroll.Value;
            if (y > list.Bottom || y + rowH < list.Y) continue;
            var row = new Rect(list.X, y, list.Width, rowH);
            bool hit = U.Hit(row) && U.Hit(list);
            float h = U.Anim(Ui.Id("td.h", key), hit ? 1 : 0);
            float done = U.Anim(Ui.Id("td.d", key), t.Done ? 1 : 0, 12);
            G.FillRound(row, 13, Theme.Surface.WithA(1 + h));

            // onay kutusu
            var cb = new Vector2(row.X + 22, row.CenterY);
            var cbr = new Rect(cb.X - 14, cb.Y - 14, 28, 28);
            if (U.Click(Ui.Id("td.cb", key), cbr)) { t.Done = !t.Done; changed = true; }
            G.StrokeCircle(cb, 9, Theme.Mix(Theme.Text3, Theme.Green, done), 1.6f);
            if (done > 0.01f)
            {
                G.FillCircle(cb, 9 * done, Theme.Green);
                G.Icon(Glyph.Check, cb, 10 * done, new Color4(0.02f, 0.05f, 0.03f, 1));
            }

            // metin + üstü çizili
            float tx = row.X + 42;
            float chipW = 0;
            if (t.Due is DateTime d)
            {
                var label = ReminderParser.Describe(d, DateTime.Now);
                bool overdue = !t.Done && d < DateTime.Now;
                chipW = G.Measure(label, 11, FontWeight.SemiBold) + 34;
                var chip = new Rect(row.Right - chipW - (h > 0.01f ? 40 : 12), row.CenterY - 12, chipW, 24);
                var cc = overdue ? Theme.Red : Theme.Amber;
                G.FillRound(chip, 12, cc.WithA(0.14f * (1 - done * 0.6f)));
                G.Icon(Glyph.Bell, new Vector2(chip.X + 14, chip.CenterY), 10, cc.WithA(1 - done * 0.6f));
                G.Text(label, new Rect(chip.X + 24, chip.Y, chipW - 28, 24), 11, cc.WithA(1 - done * 0.6f), FontWeight.SemiBold);
            }
            float tw = row.Right - tx - chipW - 52;
            var txtCol = Theme.Mix(Theme.Text, Theme.Text3, done);
            G.Text(t.Text, new Rect(tx, row.Y, tw, rowH), 13.5f, txtCol, FontWeight.Medium, font: Theme.TextFont);
            if (done > 0.01f)
            {
                float lw = MathF.Min(tw, G.Measure(t.Text, 13.5f, FontWeight.Medium, Theme.TextFont)) * done;
                G.Line(new Vector2(tx, row.CenterY + 1), new Vector2(tx + lw, row.CenterY + 1), Theme.Text3, 1.4f);
            }

            if (h > 0.01f)
            {
                G.Alpha *= h;
                bool del = U.IconButton(Ui.Id("td.del", key), new Vector2(row.Right - 22, row.CenterY), 13, Glyph.Delete, 11, Theme.Red);
                G.Alpha /= h;
                if (del) remove = t;
            }
        }
        G.PopClip();
        if (remove != null) { todos.Remove(remove); changed = true; }
        if (changed) Store.Save();
    }
}
