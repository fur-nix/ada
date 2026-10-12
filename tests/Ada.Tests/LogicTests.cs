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
    public void CorruptFileFallsBackToDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "ada-bad-" + Guid.NewGuid() + ".json");
        File.WriteAllText(path, "{ bozuk json");
        var s = Store.Load(path);
        Assert.Empty(s.Todos);
        File.Delete(path); File.Delete(path + ".bozuk");
    }
}
