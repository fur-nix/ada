using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ada.Data;

public sealed class TodoItem
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public DateTime? Due { get; set; }
    public bool Notified { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
}

public sealed class PinnedApp
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Args { get; set; }
}

/// <summary>Kapalı haptaki bir yuvada ne gösterileceği.</summary>
public enum Slot { None = 0, Clock = 1, ClockSeconds = 2, Date = 3, Battery = 4, CpuRam = 5, Dot = 6 }

public sealed class State
{
    public List<TodoItem> Todos { get; set; } = new();
    public List<PinnedApp> Apps { get; set; } = new();
    public int LastTab { get; set; }
    public bool FirstRunDone { get; set; }

    public const int TabCount = 7;
    /// <summary>Ayar dosyası düzen sürümü (2: Hitodama tarzı 7 sekme).</summary>
    public int Version { get; set; }
    public string? WallpaperFolder { get; set; }

    // ---- Görünüm ----
    /// <summary>true: üst kenara yapışık çentik (MacBook), false: yüzen ada.</summary>
    public bool Notch { get; set; } = true;
    /// <summary>0 küçük, 1 orta, 2 büyük (kapalı hap).</summary>
    public int Size { get; set; } = 1;
    /// <summary>Köşe yuvarlaklığı 0.4–1.</summary>
    public float Roundness { get; set; } = 1f;
    public string Accent { get; set; } = "A6E3A1";

    // ---- Boşta ----
    public Slot IdleLeft { get; set; } = Slot.Dot;
    public Slot IdleCenter { get; set; } = Slot.Clock;
    public Slot IdleRight { get; set; } = Slot.Battery;
    public bool MediaInIdle { get; set; } = true;

    // ---- Davranış & bildirimler ----
    /// <summary>0: her zaman görünür, 1: fare üst kenara gelince belirir, 2: sadece kısayol/bildirimle.</summary>
    public int Visibility { get; set; }
    /// <summary>0 yumuşak, 1 normal, 2 hızlı.</summary>
    public int AnimSpeed { get; set; } = 1;
    public bool Shadow { get; set; } = true;
    public bool OpenOnHover { get; set; }
    public int CollapseDelayMs { get; set; } = 380;
    public bool HideOnFullscreen { get; set; } = true;
    public bool NotifyClipboard { get; set; } = true;
    public bool NotifyTrack { get; set; } = true;
    public bool NotifyReminder { get; set; } = true;
    public bool ReminderSound { get; set; } = true;

    // ---- Dock (macOS tarzı alt çubuk) ----
    public bool DockEnabled { get; set; } = true;
    /// <summary>0 küçük, 1 orta, 2 büyük.</summary>
    public int DockSize { get; set; } = 1;
    public bool DockMagnify { get; set; } = true;
    public bool DockAutoHide { get; set; }
    /// <summary>Büyütülmüş pencereler Dock'un altına girmesin (AppBar ile yer ayırır).</summary>
    public bool DockReserve { get; set; } = true;
    /// <summary>Windows görev çubuğunu tamamen gizle (Ada kapanınca geri gelir).</summary>
    public bool HideTaskbar { get; set; }
    public bool DockShowRunning { get; set; } = true;

    // ---- Spotlight arama ----
    /// <summary>0 Ctrl+Space, 1 Alt+Space, 2 kapalı.</summary>
    public int SearchHotkey { get; set; }
    public bool SearchFiles { get; set; } = true;
    public bool SearchWeb { get; set; } = true;

    // ---- Sekmeler & genel ----
    public List<int> TabOrder { get; set; } = new() { 0, 1, 2, 3, 4, 5, 6 };
    public List<int> HiddenTabs { get; set; } = new();
    /// <summary>0 Ctrl+Alt+Space, 1 Ctrl+Shift+Space, 2 Alt+Shift+Space, 3 kapalı.</summary>
    public int Hotkey { get; set; }

    /// <summary>Eksik/bozuk değerleri onarır (eski sürümden gelen dosyalar dahil).</summary>
    public void Normalize()
    {
        if (Version < 2)
        {
            // v1 → v2: Hitodama tarzı düzen (çentik, yeşil vurgu, yeni 7 sekme)
            Notch = true; Accent = "A6E3A1";
            TabOrder = new() { 0, 1, 2, 3, 4, 5, 6 }; HiddenTabs = new(); LastTab = 0;
            Version = 2;
        }
        Size = Math.Clamp(Size, 0, 2);
        Roundness = Math.Clamp(Roundness, 0.4f, 1f);
        CollapseDelayMs = Math.Clamp(CollapseDelayMs, 150, 2000);
        Hotkey = Math.Clamp(Hotkey, 0, 3);
        Visibility = Math.Clamp(Visibility, 0, 2);
        AnimSpeed = Math.Clamp(AnimSpeed, 0, 2);
        DockSize = Math.Clamp(DockSize, 0, 2);
        SearchHotkey = Math.Clamp(SearchHotkey, 0, 2);
        var order = TabOrder.Where(t => t is >= 0 and < TabCount).Distinct().ToList();
        for (int i = 0; i < TabCount; i++) if (!order.Contains(i)) order.Add(i);
        TabOrder = order;
        HiddenTabs = HiddenTabs.Where(t => t is >= 0 and < TabCount).Distinct().ToList();
        if (HiddenTabs.Count >= TabCount) HiddenTabs.Clear();
        if (!Enum.IsDefined(IdleLeft)) IdleLeft = Slot.Dot;
        if (!Enum.IsDefined(IdleCenter)) IdleCenter = Slot.Clock;
        if (!Enum.IsDefined(IdleRight)) IdleRight = Slot.Battery;
    }

    /// <summary>Görünüm/davranış ayarlarını varsayılana döndürür; görevler ve uygulamalar korunur.</summary>
    public void ResetSettings()
    {
        var d = new State();
        Notch = d.Notch; Size = d.Size; Roundness = d.Roundness; Accent = d.Accent;
        IdleLeft = d.IdleLeft; IdleCenter = d.IdleCenter; IdleRight = d.IdleRight; MediaInIdle = d.MediaInIdle;
        Visibility = d.Visibility; AnimSpeed = d.AnimSpeed; Shadow = d.Shadow;
        DockEnabled = d.DockEnabled; DockSize = d.DockSize; DockMagnify = d.DockMagnify; DockAutoHide = d.DockAutoHide;
        DockReserve = d.DockReserve; HideTaskbar = d.HideTaskbar; DockShowRunning = d.DockShowRunning;
        SearchHotkey = d.SearchHotkey; SearchFiles = d.SearchFiles; SearchWeb = d.SearchWeb;
        OpenOnHover = d.OpenOnHover; CollapseDelayMs = d.CollapseDelayMs; HideOnFullscreen = d.HideOnFullscreen;
        NotifyClipboard = d.NotifyClipboard; NotifyTrack = d.NotifyTrack; NotifyReminder = d.NotifyReminder; ReminderSound = d.ReminderSound;
        TabOrder = d.TabOrder; HiddenTabs = d.HiddenTabs; Hotkey = d.Hotkey;
    }

    /// <summary>Görünür sekmeler, kullanıcının sırasıyla.</summary>
    public List<int> VisibleTabs() => TabOrder.Where(t => !HiddenTabs.Contains(t)).ToList();
}

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(State))]
internal partial class StateJson : JsonSerializerContext { }

public static class Store
{
    public static string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ada");
    static string FilePath => Path.Combine(Dir, "state.json");

    public static State Current { get; private set; } = new();

    public static State Load(string? path = null)
    {
        path ??= FilePath;
        try
        {
            if (File.Exists(path))
                Current = JsonSerializer.Deserialize(File.ReadAllText(path), StateJson.Default.State) ?? new State();
        }
        catch (Exception e)
        {
            Core.Log.Error("Store.Load", e);
            try { File.Copy(path, path + ".bozuk", true); } catch { }
            Current = new State();
        }
        Current.Normalize();
        return Current;
    }

    public static void Save(string? path = null)
    {
        path ??= FilePath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, StateJson.Default.State));
            File.Move(tmp, path, true);
        }
        catch (Exception e) { Core.Log.Error("Store.Save", e); }
    }

    internal static void Reset(State s) => Current = s;
}
