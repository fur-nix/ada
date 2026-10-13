using System.Text.RegularExpressions;

namespace Ada.Data;

/// <summary>Launchy tarzı önekler: "g …" Google, "yt …" YouTube, "w …" Vikipedi, "> komut", çıplak adres.</summary>
public static partial class LaunchQuery
{
    public enum Kind { None, Google, YouTube, Wikipedia, Command, Url }

    [GeneratedRegex(@"^(https?://\S+|www\.\S+\.\S+|[a-z0-9-]+(\.[a-z0-9-]+)*\.(com|net|org|io|dev|app|ai|tr|co|gov|edu|me|tv|gg|info|xyz)(\.[a-z]{2})?(/\S*)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRx();

    public static (Kind kind, string arg) Parse(string input)
    {
        var q = input.Trim();
        if (q.Length == 0) return (Kind.None, "");
        if (q.StartsWith('>')) return (Kind.Command, q[1..].Trim());
        int sp = q.IndexOf(' ');
        if (sp > 0)
        {
            var head = q[..sp].ToLowerInvariant();
            var rest = q[(sp + 1)..].Trim();
            if (rest.Length > 0)
            {
                switch (head)
                {
                    case "g": case "google": return (Kind.Google, rest);
                    case "yt": case "youtube": return (Kind.YouTube, rest);
                    case "w": case "wiki": return (Kind.Wikipedia, rest);
                }
            }
        }
        if (!q.Contains(' ') && UrlRx().IsMatch(q)) return (Kind.Url, q.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? q : "https://" + q);
        return (Kind.None, q);
    }

    public static string UrlFor(Kind k, string arg) => k switch
    {
        Kind.Google => "https://www.google.com/search?q=" + Uri.EscapeDataString(arg),
        Kind.YouTube => "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(arg),
        Kind.Wikipedia => "https://tr.wikipedia.org/w/index.php?search=" + Uri.EscapeDataString(arg),
        Kind.Url => arg,
        _ => ""
    };
}
