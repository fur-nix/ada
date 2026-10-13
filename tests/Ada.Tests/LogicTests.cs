using Ada.Core;
using Ada.Data;

namespace Ada.Tests;

public class ReminderParserTests
{
    static readonly DateTime Now = new(2026, 10, 13, 14, 0, 0);

    [Fact]
    public void ClockLaterToday()
    {
        var (text, due) = ReminderParser.Parse("18:30 toplantı", Now);
        Assert.Equal("toplantı", text);
        Assert.Equal(new DateTime(2026, 10, 13, 18, 30, 0), due);
    }

    [Fact]
    public void ClockAlreadyPassedRollsToTomorrow()
    {
        var (_, due) = ReminderParser.Parse("9:00 spor", Now);
        Assert.Equal(new DateTime(2026, 10, 14, 9, 0, 0), due);
    }

    [Fact]
    public void TomorrowKeyword()
    {
        var (text, due) = ReminderParser.Parse("yarın 20.15 annemi ara", Now);
        Assert.Equal("annemi ara", text);
        Assert.Equal(new DateTime(2026, 10, 14, 20, 15, 0), due);
    }

    [Theory]
    [InlineData("30dk çay", "çay", 30)]
    [InlineData("çay demle 15 dakika sonra", "çay demle", 15)]
    [InlineData("2 saat sonra ara", "ara", 120)]
    public void Relative(string input, string expectedText, int minutes)
    {
        var (text, due) = ReminderParser.Parse(input, Now);
        Assert.Equal(expectedText, text);
        Assert.Equal(Now.AddMinutes(minutes), due);
    }

    [Fact]
    public void PlainTextHasNoDue()
    {
        var (text, due) = ReminderParser.Parse("  süt al  ", Now);
        Assert.Equal("süt al", text);
        Assert.Null(due);
    }

    [Fact]
    public void InvalidClockIgnored()
    {
        var (text, due) = ReminderParser.Parse("skor 99:99", Now);
        Assert.Null(due);
        Assert.Equal("skor 99:99", text);
    }
}

public class CalcTests
{
    [Theory]
    [InlineData("2+3*4", 14)]
    [InlineData("(2+3)*4", 20)]
    [InlineData("2^3^2", 512)]
    [InlineData("-5+2", -3)]
    [InlineData("10/4", 2.5)]
    [InlineData("3,5 x 2", 7)]
    [InlineData("17 % 5", 2)]
    public void Evaluates(string input, double expected)
    {
        Assert.True(Calc.TryEval(input, out var v));
        Assert.Equal(expected, v, 9);
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("42")]
    [InlineData("2+")]
    [InlineData("(1+2")]
    [InlineData("1/0")]
    public void RejectsNonMath(string input) => Assert.False(Calc.TryEval(input, out _));

    [Fact]
    public void FormatsTurkish() => Assert.Equal("1.234,5", Calc.Format(1234.5));
}

public class LaunchQueryTests
{
    [Theory]
    [InlineData("g kedi videoları", LaunchQuery.Kind.Google, "kedi videoları")]
    [InlineData("yt lofi", LaunchQuery.Kind.YouTube, "lofi")]
    [InlineData("w Atatürk", LaunchQuery.Kind.Wikipedia, "Atatürk")]
    [InlineData("> ipconfig /all", LaunchQuery.Kind.Command, "ipconfig /all")]
    [InlineData("github.com/ada", LaunchQuery.Kind.Url, "https://github.com/ada")]
    [InlineData("https://claude.ai", LaunchQuery.Kind.Url, "https://claude.ai")]
    [InlineData("www.trendyol.com", LaunchQuery.Kind.Url, "https://www.trendyol.com")]
    [InlineData("chrome", LaunchQuery.Kind.None, "chrome")]
    [InlineData("g", LaunchQuery.Kind.None, "g")]
    [InlineData("rapor.docx", LaunchQuery.Kind.None, "rapor.docx")]
    public void Parses(string input, LaunchQuery.Kind kind, string arg)
    {
        var (k, a) = LaunchQuery.Parse(input);
        Assert.Equal(kind, k);
        Assert.Equal(arg, a);
    }

    [Fact]
    public void BuildsSearchUrl() =>
        Assert.Equal("https://www.youtube.com/results?search_query=lofi%20hip%20hop", LaunchQuery.UrlFor(LaunchQuery.Kind.YouTube, "lofi hip hop"));
}

public class SearchScoreTests
{
    static readonly System.Globalization.CultureInfo Tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");

    [Fact]
    public void PrefixBeatsWordBeatsContains()
    {
        Assert.Equal(3, Ada.Services.AppIndex.Score("Visual Studio Code", "vis", Tr));
        Assert.Equal(2, Ada.Services.AppIndex.Score("Visual Studio Code", "cod", Tr));
        Assert.Equal(1, Ada.Services.AppIndex.Score("Visual Studio Code", "udi", Tr));
        Assert.Equal(0, Ada.Services.AppIndex.Score("Visual Studio Code", "xyz", Tr));
    }

    [Fact]
    public void IgnoresCaseAndTurkishAccents() => Assert.True(Ada.Services.AppIndex.Score("Görev Yöneticisi", "gorev", Tr) > 0);
}

public class ClipboardHistoryTests
{
    [Fact]
    public void NewestFirstAndDedupes()
    {
        var h = new ClipboardHistory();
        var t = DateTime.Now;
        Assert.True(h.Add("a", t));
        Assert.True(h.Add("b", t));
        Assert.False(h.Add("b", t));   // aynısı zaten en üstte
        Assert.True(h.Add("a", t));    // eskisi üste taşınır
        Assert.Equal(new[] { "a", "b" }, h.Items.Select(i => i.Text));
    }

    [Fact]
    public void CapsAtMaxAndIgnoresBlank()
    {
        var h = new ClipboardHistory();
        Assert.False(h.Add("   ", DateTime.Now));
        for (int i = 0; i < 40; i++) h.Add("x" + i, DateTime.Now);
        Assert.Equal(ClipboardHistory.Max, h.Items.Count);
        Assert.Equal("x39", h.Items[0].Text);
    }

    [Fact]
    public void PreviewIsSingleLine()
    {
        var item = new ClipItem("satır1\r\nsatır2", DateTime.Now);
        Assert.DoesNotContain("\n", item.Preview);
    }
}

public class SpringTests
{
    [Fact]
    public void ConvergesWithSmallOvershoot()
    {
        var s = new Spring(0) { Target = 100 };
        float max = 0;
        for (int i = 0; i < 120; i++) { s.Step(1 / 60f); max = MathF.Max(max, s.Value); }
        Assert.True(s.Settled);
        Assert.Equal(100, s.Value, 2);
        Assert.InRange(max, 100.5f, 112f); // hafif yaylanma var ama abartılı değil
    }

    [Fact]
    public void LargeDtStaysStable()
    {
        var s = new Spring(0) { Target = 1 };
        s.Step(0.5f);
        Assert.InRange(s.Value, -1f, 2f);
    }
}

public class StoreTests
{
    [Fact]
    public void RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "ada-test-" + Guid.NewGuid() + ".json");
        Store.Reset(new State());
        Store.Current.Todos.Add(new TodoItem { Text = "çay", Due = new DateTime(2026, 1, 2, 3, 4, 0) });
        Store.Current.Apps.Add(new PinnedApp { Name = "Not Defteri", Path = "notepad.exe" });
        Store.Save(path);
        Store.Reset(new State());
        var s = Store.Load(path);
        Assert.Equal("çay", s.Todos[0].Text);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 0), s.Todos[0].Due);
        Assert.Equal("Not Defteri", s.Apps[0].Name);
        File.Delete(path);
    }

    [Fact]
    public void NormalizeRepairsTabsAndRanges()
    {
        var s = new State { Version = 2, TabOrder = new() { 3, 3, 9, 1 }, HiddenTabs = new() { 0, 1, 2, 3, 4, 5, 6 }, Size = 7, Roundness = 0, Hotkey = -2 };
        s.Normalize();
        Assert.Equal(new[] { 3, 1, 0, 2, 4, 5, 6 }, s.TabOrder);
        Assert.Empty(s.HiddenTabs);           // hepsi gizlenemez
        Assert.Equal(2, s.Size);
        Assert.Equal(0.4f, s.Roundness);
        Assert.Equal(0, s.Hotkey);
    }

    [Fact]
    public void VisibleTabsFollowOrderAndHidden()
    {
        var s = new State { TabOrder = new() { 5, 0, 1, 2, 3, 4 }, HiddenTabs = new() { 1, 3 } };
        Assert.Equal(new[] { 5, 0, 2, 4 }, s.VisibleTabs());
    }

    [Fact]
    public void ResetSettingsKeepsUserData()
    {
        var s = new State { Notch = false, Size = 2, OpenOnHover = true };
        s.Todos.Add(new TodoItem { Text = "kalsın" });
        s.ResetSettings();
        Assert.True(s.Notch); Assert.Equal(1, s.Size); Assert.False(s.OpenOnHover);
        Assert.Single(s.Todos);
    }

    [Fact]
    public void OldFileWithoutNewSettingsGetsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "ada-old-" + Guid.NewGuid() + ".json");
        File.WriteAllText(path, "{\"Todos\":[{\"Text\":\"eski\"}],\"Apps\":[],\"Notch\":false,\"TabOrder\":[5,4,3,2,1,0]}");
        var s = Store.Load(path);
        // v1 dosyası Hitodama düzenine taşınır, kullanıcı verisi korunur
        Assert.True(s.Notch);
        Assert.Equal(2, s.Version);
        Assert.Equal("eski", s.Todos[0].Text);
        Assert.Equal(Slot.Clock, s.IdleCenter);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 }, s.TabOrder);
        File.Delete(path);
    }

    [Fact]
    public void CorruptFileFallsBackToDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "ada-bad-" + Guid.NewGuid() + ".json");
        File.WriteAllText(path, "{ bozuk json");
        var s = Store.Load(path);
        Assert.Empty(s.Todos);
        File.Delete(path); File.Delete(path + ".bozuk");
    }
}
