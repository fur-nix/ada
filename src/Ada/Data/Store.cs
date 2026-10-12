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

public sealed class State
{
    public List<TodoItem> Todos { get; set; } = new();
    public List<PinnedApp> Apps { get; set; } = new();
    public string Accent { get; set; } = "8E9BFF";
    public int LastTab { get; set; }
    /// <summary>true: üst kenara yapışık çentik, false: yüzen ada.</summary>
    public bool Notch { get; set; }
    public bool FirstRunDone { get; set; }
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
