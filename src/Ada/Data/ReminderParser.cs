using System.Text.RegularExpressions;

namespace Ada.Data;

/// <summary>
/// "18:30 toplantı", "yarın 9:00 spor", "30dk çay", "2 saat sonra ara", "toplantı 14.00" gibi girdilerden zaman çıkarır.
/// </summary>
public static partial class ReminderParser
{
    [GeneratedRegex(@"(?<!\d)(\d{1,2})[:.](\d{2})(?!\d)")]
    private static partial Regex ClockRx();

    [GeneratedRegex(@"(?<!\S)(\d{1,3})\s*(dk|dakika|min|sa|saat|h)\b\.?(\s*sonra)?", RegexOptions.IgnoreCase)]
    private static partial Regex RelRx();

    [GeneratedRegex(@"(?<!\S)(yarın|yarin)(?!\S)", RegexOptions.IgnoreCase)]
    private static partial Regex TomorrowRx();

    public static (string text, DateTime? due) Parse(string input, DateTime now)
    {
        var s = input.Trim();
        DateTime? due = null;

        var rel = RelRx().Match(s);
        if (rel.Success)
        {
            int n = int.Parse(rel.Groups[1].Value);
            var unit = rel.Groups[2].Value.ToLowerInvariant();
            due = unit is "sa" or "saat" or "h" ? now.AddHours(n) : now.AddMinutes(n);
            s = Remove(s, rel);
        }
        else
        {
            var clock = ClockRx().Match(s);
            if (clock.Success)
            {
                int h = int.Parse(clock.Groups[1].Value), m = int.Parse(clock.Groups[2].Value);
                if (h < 24 && m < 60)
                {
                    var tm = TomorrowRx().Match(s);
                    var day = now.Date;
                    var d = day.AddHours(h).AddMinutes(m);
                    if (tm.Success) d = d.AddDays(1);
                    else if (d <= now) d = d.AddDays(1);
                    due = d;
                    s = Remove(s, clock);
                    var tm2 = TomorrowRx().Match(s);
                    if (tm2.Success) s = Remove(s, tm2);
                }
            }
        }

        s = Regex.Replace(s, @"\s{2,}", " ").Trim(' ', '-', ',', ':');
        if (s.Length == 0) s = "Hatırlatıcı";
        return (s, due);
    }

    static string Remove(string s, Match m) => (s[..m.Index] + " " + s[(m.Index + m.Length)..]).Trim();

    public static string Describe(DateTime due, DateTime now)
    {
        var t = due.ToString("HH:mm");
        if (due.Date == now.Date) return "Bugün " + t;
        if (due.Date == now.Date.AddDays(1)) return "Yarın " + t;
        return due.ToString("d MMM HH:mm", new System.Globalization.CultureInfo("tr-TR"));
    }
}
