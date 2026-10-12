using System.Collections.Concurrent;
using Ada.Native;

namespace Ada.Core;

/// <summary>Arka plan iş parçacıklarından UI iş parçacığına iş taşır.</summary>
public static class Dispatcher
{
    public const uint WM_RUN = Win32.WM_APP + 1;
    static readonly ConcurrentQueue<Action> Queue = new();
    static IntPtr _hwnd;

    public static void Init(IntPtr hwnd) => _hwnd = hwnd;

    public static void Post(Action a)
    {
        Queue.Enqueue(a);
        Win32.PostMessageW(_hwnd, WM_RUN, 0, 0);
    }

    public static void Drain()
    {
        while (Queue.TryDequeue(out var a))
        {
            try { a(); } catch (Exception e) { Log.Error("dispatch", e); }
        }
    }
}

public static class Log
{
    static readonly string Path = System.IO.Path.Combine(Data.Store.Dir, "ada.log");
    public static void Error(string where, Exception e)
    {
        try
        {
            Directory.CreateDirectory(Data.Store.Dir);
            var fi = new FileInfo(Path);
            if (fi.Exists && fi.Length > 256 * 1024) fi.Delete();
            File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{where}] {e.GetType().Name}: {e.Message}\n");
        }
        catch { }
    }
    public static void Info(string msg)
    {
        try { Directory.CreateDirectory(Data.Store.Dir); File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}\n"); } catch { }
    }
}
