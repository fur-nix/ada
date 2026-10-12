namespace Ada.Data;

public sealed record ClipItem(string Text, DateTime Time)
{
    public string Preview { get; } = MakePreview(Text);

    static string MakePreview(string t)
    {
        var s = t.Replace("\r\n", " ⏎ ").Replace('\n', ' ').Replace('\t', ' ').Trim();
        return s.Length > 160 ? s[..160] + "…" : s;
    }
}

/// <summary>Yalnızca bellekte tutulan metin pano geçmişi (en yeni başta, tekrarlar üste taşınır).</summary>
public sealed class ClipboardHistory
{
    public const int Max = 25;
    public const int MaxChars = 20_000;
    readonly List<ClipItem> _items = new();
    public IReadOnlyList<ClipItem> Items => _items;

    public bool Add(string? text, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Length > MaxChars) text = text[..MaxChars];
        int i = _items.FindIndex(x => x.Text == text);
        if (i == 0) return false;
        if (i > 0) _items.RemoveAt(i);
        _items.Insert(0, new ClipItem(text, now));
        if (_items.Count > Max) _items.RemoveRange(Max, _items.Count - Max);
        return true;
    }

    public void Remove(ClipItem item) => _items.Remove(item);
    public void Clear() => _items.Clear();
}
