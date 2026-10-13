using System.Runtime.InteropServices;
using Ada.Core;
using Ada.Data;
using Ada.Native;
using Microsoft.Win32;
using Vortice.Direct2D1;

namespace Ada.Services;

public sealed class AppsService
{
    readonly Gfx _g;
    readonly Dictionary<string, ID2D1Bitmap?> _icons = new(StringComparer.OrdinalIgnoreCase);

    public AppsService(Gfx g) => _g = g;

    public static void EnsureDefaults(State s)
    {
        if (s.FirstRunDone) return;
        s.FirstRunDone = true;
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        (string name, string[] paths)[] candidates =
        {
            ("Dosyalar", new[] { Path.Combine(win, "explorer.exe") }),
            ("Chrome", new[] { Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"), Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"), Path.Combine(local, @"Google\Chrome\Application\chrome.exe") }),
            ("Edge", new[] { Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"), Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe") }),
            ("Spotify", new[] { Path.Combine(appData, @"Spotify\Spotify.exe") }),
            ("VS Code", new[] { Path.Combine(local, @"Programs\Microsoft VS Code\Code.exe") }),
            ("Discord", new[] { Path.Combine(local, @"Discord\Update.exe") }),
            ("Terminal", new[] { Path.Combine(local, @"Microsoft\WindowsApps\wt.exe") }),
            ("Not Defteri", new[] { Path.Combine(win, @"System32\notepad.exe") }),
            ("Hesap Makinesi", new[] { Path.Combine(win, @"System32\calc.exe") }),
            ("Görev Yöneticisi", new[] { Path.Combine(win, @"System32\Taskmgr.exe") }),
        };
        foreach (var (name, paths) in candidates)
        {
            var p = paths.FirstOrDefault(File.Exists);
            if (p == null) continue;
            s.Apps.Add(new PinnedApp { Name = name, Path = p, Args = name == "Discord" ? "--processStart Discord.exe" : null });
            if (s.Apps.Count >= 10) break;
        }
        s.Apps.Add(new PinnedApp { Name = "Ayarlar", Path = "ms-settings:" });
    }

    public ID2D1Bitmap? Icon(string path)
    {
        if (_icons.TryGetValue(path, out var bmp)) return bmp;
        bmp = null;
        if (!path.Contains(':') || File.Exists(path) || Directory.Exists(path))
        {
            try
            {
                var iid = typeof(IShellItemImageFactory).GUID;
                if (Win32.SHCreateItemFromParsingName(path, IntPtr.Zero, iid, out var p) == 0)
                {
                    var f = ComUtil.Wrap<IShellItemImageFactory>(p);
                    int px = (int)(48 * _g.Scale);
                    // SIIGBF_ICONONLY = 4 (küçük resim yerine ikon), BIGGERSIZEOK = 1
                    if (f != null && f.GetImage(new NativeSize { cx = px, cy = px }, 0x4 | 0x1, out var hbm) == 0)
                    {
                        bmp = _g.FromHBitmap(hbm, px);
                        Win32.DeleteObject(hbm);
                    }
                }
            }
            catch (Exception e) { Log.Error("Icon", e); }
        }
        _icons[path] = bmp;
        return bmp;
    }

    public void ClearIconCache() { foreach (var b in _icons.Values) b?.Dispose(); _icons.Clear(); }

    public static void Launch(PinnedApp app)
    {
        var r = Win32.ShellExecuteW(IntPtr.Zero, "open", app.Path, app.Args, null, Win32.SW_SHOWNORMAL);
        if ((long)r <= 32) Log.Info($"Başlatılamadı: {app.Path} ({(long)r})");
    }

    public static PinnedApp FromPath(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        try
        {
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var desc = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(desc) && desc.Length <= 24) name = desc.Trim();
            }
        }
        catch { }
        return new PinnedApp { Name = name, Path = path };
    }

    /// <summary>Dosya seçme penceresi (exe / kısayol / herhangi bir dosya).</summary>
    public static unsafe string? PickFile(IntPtr owner, string title = "Ada'ya uygulama ekle", string? filter = null)
    {
        filter ??= "Uygulamalar ve kısayollar\0*.exe;*.lnk;*.url;*.bat;*.cmd\0Tüm dosyalar\0*.*\0\0";
        var fileBuf = Marshal.AllocHGlobal(2048 * 2);
        var filterPtr = Marshal.StringToHGlobalUni(filter);
        var titlePtr = Marshal.StringToHGlobalUni(title);
        var startDir = Marshal.StringToHGlobalUni(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) + @"\Programs");
        try
        {
            *(char*)fileBuf = '\0';
            var ofn = new OPENFILENAME
            {
                lStructSize = Marshal.SizeOf<OPENFILENAME>(), hwndOwner = owner, lpstrFilter = filterPtr, nFilterIndex = 1,
                lpstrFile = fileBuf, nMaxFile = 2048, lpstrTitle = titlePtr, lpstrInitialDir = startDir,
                // OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NODEREFERENCELINKS | OFN_EXPLORER
                Flags = 0x1000 | 0x800 | 0x100000 | 0x80000
            };
            return Win32.GetOpenFileNameW(ref ofn) ? Marshal.PtrToStringUni(fileBuf) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(fileBuf); Marshal.FreeHGlobal(filterPtr); Marshal.FreeHGlobal(titlePtr); Marshal.FreeHGlobal(startDir);
        }
    }

    // ---------------- Windows ile başlat ----------------
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool StartupEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue("Ada") is string s && s.Contains(Environment.ProcessPath ?? "§", StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue("Ada", $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue("Ada", false);
        }
    }
}

public sealed class ClipboardService
{
    public readonly ClipboardHistory History = new();
    bool _selfSet;
    readonly IntPtr _hwnd;

    public ClipboardService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        Win32.AddClipboardFormatListener(hwnd);
    }

    /// <summary>WM_CLIPBOARDUPDATE → yeni metin eklendiyse true.</summary>
    public bool OnUpdate(out string? text)
    {
        text = null;
        if (_selfSet) { _selfSet = false; return false; }
        text = ReadText(_hwnd);
        return History.Add(text, DateTime.Now);
    }

    public static unsafe string? ReadText(IntPtr hwnd)
    {
        if (!Win32.IsClipboardFormatAvailable(Win32.CF_UNICODETEXT)) return null;
        for (int i = 0; i < 5; i++)
        {
            if (Win32.OpenClipboard(hwnd)) break;
            if (i == 4) return null;
            Thread.Sleep(15);
        }
        try
        {
            var h = Win32.GetClipboardData(Win32.CF_UNICODETEXT);
            if (h == IntPtr.Zero) return null;
            var p = Win32.GlobalLock(h);
            try { return p == IntPtr.Zero ? null : new string((char*)p); }
            finally { Win32.GlobalUnlock(h); }
        }
        finally { Win32.CloseClipboard(); }
    }

    public unsafe bool SetText(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            if (Win32.OpenClipboard(_hwnd)) break;
            if (i == 4) return false;
            Thread.Sleep(15);
        }
        try
        {
            Win32.EmptyClipboard();
            var bytes = (text.Length + 1) * 2;
            var h = Win32.GlobalAlloc(Win32.GMEM_MOVEABLE, (nuint)bytes);
            var p = (char*)Win32.GlobalLock(h);
            fixed (char* src = text) Buffer.MemoryCopy(src, p, bytes, text.Length * 2);
            p[text.Length] = '\0';
            Win32.GlobalUnlock(h);
            if (Win32.SetClipboardData(Win32.CF_UNICODETEXT, h) == IntPtr.Zero) { Win32.GlobalFree(h); return false; }
            _selfSet = true;
            return true;
        }
        finally { Win32.CloseClipboard(); }
    }
}
