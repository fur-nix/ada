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

public sealed class AiProfile
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    /// <summary>Yalnızca "custom" ve "ollama" için; diğerlerinde sağlayıcının varsayılan adresi kullanılır.</summary>
    public string BaseUrl { get; set; } = "";
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

    // ---- Dört ana özellik: bağımsız aç/kapat ----
    /// <summary>Üst ada (dynamic island) etkin mi.</summary>
    public bool IslandEnabled { get; set; } = true;
    /// <summary>İmleç menüsü (daire) etkin mi.</summary>
    public bool RadialEnabled { get; set; } = true;
    /// <summary>Yapay zeka asistanı etkin mi (sekme + pencere + kısayol).</summary>
    public bool AiEnabled { get; set; } = true;
    /// <summary>Yapay zeka penceresi kısayolu: 0 Ctrl+Shift+Space, 1 Ctrl+Alt+A, 2 kapalı.</summary>
    public int AiHotkey { get; set; }

    public const int TabCount = 8;
    /// <summary>Ayar dosyası düzen sürümü (2: Hitodama tarzı 7 sekme; 3: Yapay Zeka sekmesi).</summary>
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
    /// <summary>Ön plandaki pencere ekranın üstünü kaplarken (tarayıcı sekmeleri vb.): 0 normal, 1 çok küçük çizgi, 2 gizle (fare gelince).</summary>
    public int AppOverlapMode { get; set; } = 1;
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
    /// <summary>Simge boyutu (DIP, 32–96). 0 → eski DockSize'dan taşınır.</summary>
    public float DockIconSize { get; set; }
    /// <summary>Büyütme oranı 1.0–2.4.</summary>
    public float DockMagnification { get; set; } = 1.65f;
    public float DockSpacing { get; set; } = 8f;
    public float DockRadius { get; set; } = 22f;
    public float DockOpacity { get; set; } = 0.78f;
    /// <summary>0 buzlu cam (duvar kağıdından), 1 koyu, 2 şeffaf.</summary>
    public int DockStyle { get; set; }
    public bool DockLabels { get; set; } = true;
    public bool DockBounce { get; set; } = true;
    public bool DockPreviews { get; set; } = true;
    public bool DockIndicators { get; set; } = true;

    // ---- Güç (ASUS) ----
    /// <summary>Son seçilen ASUS fan/performans profili: 0 dengeli, 1 turbo, 2 sessiz, -1 dokunulmadı.</summary>
    public int AsusFanMode { get; set; } = -1;
    /// <summary>Pil şarj sınırı (60/80/100); 0 = Ada yönetmesin. ASUS bunu yeniden başlatınca unutur → açılışta uygulanır.</summary>
    public int ChargeLimit { get; set; }

    // ---- Dairesel menü ve sabitleme ----
    /// <summary>Dairesel menünün kendi listesi (Dock'tan bağımsız; kullanıcı ekler).</summary>
    public List<PinnedApp> RadialApps { get; set; } = new();
    /// <summary>Sağ tuşu basılı tutunca dairesel menü açılsın.</summary>
    public bool RadialRightHold { get; set; } = true;
    /// <summary>Sağ tuş bırakılınca: 0 üzerindeki öğeyi aç / boşsa kapan, 1 menü açık kalsın (tıkla seç, boşluğa tıkla kapan).</summary>
    public int RadialReleaseMode { get; set; }
    /// <summary>Dairesel menü kısayolu: 0 Alt+Q, 1 Ctrl+Alt+Q, 2 kapalı.</summary>
    public int RadialHotkey { get; set; }
    /// <summary>Gezgin sağ tık menüsüne "Ada Dock'a sabitle" eklensin.</summary>
    public bool ShellPinMenu { get; set; } = true;

    // ---- Yapay zeka asistanı (çok sağlayıcılı) ----
    /// <summary>Seçili sağlayıcı anahtarı: anthropic | gemini | openai | openrouter | groq | ollama | custom.</summary>
    public string AiProvider { get; set; } = "gemini";
    /// <summary>Sağlayıcı başına ayar (anahtar, model, özel adres). Anahtarlar yalnızca bu bilgisayarda state.json'da saklanır.</summary>
    public Dictionary<string, AiProfile> AiProfiles { get; set; } = new();
    /// <summary>Asistanın dosya/komut işlemleri için izinli kök klasör (boşsa masaüstü).</summary>
    public string AiWorkDir { get; set; } = "";
    /// <summary>true: her dosya silme/komut önce onay ister (önerilen).</summary>
    public bool AiConfirm { get; set; } = true;
    public bool AiWebSearch { get; set; } = true;
    /// <summary>Asistan ekranı görüp fare/klavye ile bilgisayarı kullanabilsin (görüntü destekli model gerekir). Varsayılan kapalı.</summary>
    public bool AiComputerUse { get; set; }

    public AiProfile Ai(string? provider = null)
    {
        provider ??= AiProvider;
        if (!AiProfiles.TryGetValue(provider, out var p)) { p = new AiProfile(); AiProfiles[provider] = p; }
        return p;
    }

    /// <summary>Spotlight: neyi kaç kez açtın (Launchy gibi sık kullanılanı öne alır).</summary>
    public Dictionary<string, int> LaunchCounts { get; set; } = new();

    // ---- Spotlight arama ----
    /// <summary>0 Ctrl+Space, 1 Alt+Space, 2 kapalı.</summary>
    public int SearchHotkey { get; set; }
    public bool SearchFiles { get; set; } = true;
    public bool SearchWeb { get; set; } = true;

    // ---- Sekmeler & genel ----
    public List<int> TabOrder { get; set; } = new() { 0, 1, 7, 2, 3, 4, 5, 6 };
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
        if (Version < 3)
        {
            // v2 → v3: Yapay Zeka sekmesini (7) görünür sıraya ekle
            if (!TabOrder.Contains(7)) TabOrder.Insert(Math.Min(2, TabOrder.Count), 7);
            HiddenTabs.Remove(7);
            Version = 3;
        }
        Size = Math.Clamp(Size, 0, 2);
        Roundness = Math.Clamp(Roundness, 0.4f, 1f);
        CollapseDelayMs = Math.Clamp(CollapseDelayMs, 150, 2000);
        Hotkey = Math.Clamp(Hotkey, 0, 3);
        Visibility = Math.Clamp(Visibility, 0, 2);
        AnimSpeed = Math.Clamp(AnimSpeed, 0, 2);
        DockSize = Math.Clamp(DockSize, 0, 2);
        if (DockIconSize <= 0) DockIconSize = new[] { 40f, 50f, 62f }[DockSize];
        DockIconSize = Math.Clamp(DockIconSize, 32, 96);
        DockMagnification = Math.Clamp(DockMagnification, 1f, 2.4f);
        DockSpacing = Math.Clamp(DockSpacing, 2, 24);
        DockRadius = Math.Clamp(DockRadius, 6, 40);
        DockOpacity = Math.Clamp(DockOpacity, 0.2f, 1f);
        DockStyle = Math.Clamp(DockStyle, 0, 2);
        LaunchCounts ??= new();
        RadialApps ??= new();
        if (RadialApps.Count > 12) RadialApps = RadialApps.Take(12).ToList();
        if (LaunchCounts.Count > 400) // en az kullanılanları at
            LaunchCounts = LaunchCounts.OrderByDescending(kv => kv.Value).Take(300).ToDictionary(kv => kv.Key, kv => kv.Value);
        SearchHotkey = Math.Clamp(SearchHotkey, 0, 2);
        RadialHotkey = Math.Clamp(RadialHotkey, 0, 2);
        AppOverlapMode = Math.Clamp(AppOverlapMode, 0, 2);
        AiHotkey = Math.Clamp(AiHotkey, 0, 2);
        AiProfiles ??= new();
        if (ChargeLimit is not (0 or 60 or 80 or 100)) ChargeLimit = 0;
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
        Visibility = d.Visibility; AnimSpeed = d.AnimSpeed; Shadow = d.Shadow; AppOverlapMode = d.AppOverlapMode;
        DockEnabled = d.DockEnabled; DockSize = d.DockSize; DockMagnify = d.DockMagnify; DockAutoHide = d.DockAutoHide;
        DockReserve = d.DockReserve; HideTaskbar = d.HideTaskbar; DockShowRunning = d.DockShowRunning;
        DockIconSize = 50; DockMagnification = d.DockMagnification; DockSpacing = d.DockSpacing; DockRadius = d.DockRadius;
        DockOpacity = d.DockOpacity; DockStyle = d.DockStyle; DockLabels = d.DockLabels; DockBounce = d.DockBounce;
        DockPreviews = d.DockPreviews; DockIndicators = d.DockIndicators;
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
