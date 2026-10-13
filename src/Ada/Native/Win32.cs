using System.Runtime.InteropServices;

namespace Ada.Native;

[StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
[StructLayout(LayoutKind.Sequential)] public struct SIZE { public int CX, CY; public SIZE(int cx, int cy) { CX = cx; CY = cy; } }
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }

[StructLayout(LayoutKind.Sequential)]
public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public POINT pt; public uint lPrivate; }

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct WNDCLASSEX
{
    public uint cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra; public int cbWndExtra;
    public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground;
    public IntPtr lpszMenuName; public IntPtr lpszClassName; public IntPtr hIconSm;
}

[StructLayout(LayoutKind.Sequential)]
public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

[StructLayout(LayoutKind.Sequential)]
public struct BITMAPINFOHEADER
{
    public uint biSize; public int biWidth; public int biHeight; public ushort biPlanes; public ushort biBitCount;
    public uint biCompression; public uint biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter;
    public uint biClrUsed; public uint biClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
public struct MONITORINFO { public uint cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

[StructLayout(LayoutKind.Sequential)]
public struct TRACKMOUSEEVENT { public uint cbSize; public uint dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }

[StructLayout(LayoutKind.Sequential)]
public struct MEMORYSTATUSEX
{
    public uint dwLength; public uint dwMemoryLoad; public ulong ullTotalPhys; public ulong ullAvailPhys;
    public ulong ullTotalPageFile; public ulong ullAvailPageFile; public ulong ullTotalVirtual; public ulong ullAvailVirtual;
    public ulong ullAvailExtendedVirtual;
}

[StructLayout(LayoutKind.Sequential)]
public struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }

[StructLayout(LayoutKind.Sequential)]
public struct INPUT { public uint type; public KEYBDINPUT ki; public ulong pad; }

[StructLayout(LayoutKind.Sequential)]
public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct OPENFILENAME
{
    public int lStructSize; public IntPtr hwndOwner; public IntPtr hInstance; public IntPtr lpstrFilter; public IntPtr lpstrCustomFilter;
    public int nMaxCustFilter; public int nFilterIndex; public IntPtr lpstrFile; public int nMaxFile; public IntPtr lpstrFileTitle;
    public int nMaxFileTitle; public IntPtr lpstrInitialDir; public IntPtr lpstrTitle; public int Flags; public short nFileOffset;
    public short nFileExtension; public IntPtr lpstrDefExt; public IntPtr lCustData; public IntPtr lpfnHook; public IntPtr lpTemplateName;
    public IntPtr pvReserved; public int dwReserved; public int FlagsEx;
}

[StructLayout(LayoutKind.Sequential)]
public struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }

[StructLayout(LayoutKind.Sequential)]
public struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

[StructLayout(LayoutKind.Sequential)]
public struct APPBARDATA { public uint cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }

[StructLayout(LayoutKind.Sequential)]
public struct PROCESS_MEMORY_COUNTERS
{
    public uint cb, PageFaultCount; public nuint PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
        QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
}

[StructLayout(LayoutKind.Sequential)]
public struct PHYSICAL_MONITOR { public IntPtr hPhysicalMonitor; public unsafe fixed char szPhysicalMonitorDescription[128]; }

public static unsafe class Win32
{
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8, WS_EX_NOACTIVATE = 0x8000000, WS_EX_ACCEPTFILES = 0x10;
    public const int GWL_EXSTYLE = -20;

    public const uint WM_DESTROY = 0x2, WM_ACTIVATE = 0x6, WM_SETTINGCHANGE = 0x1A, WM_DISPLAYCHANGE = 0x7E, WM_TIMER = 0x113,
        WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_RBUTTONUP = 0x205, WM_MOUSEWHEEL = 0x20A,
        WM_MOUSELEAVE = 0x2A3, WM_KEYDOWN = 0x100, WM_CHAR = 0x102, WM_SYSKEYDOWN = 0x104, WM_HOTKEY = 0x312,
        WM_CLIPBOARDUPDATE = 0x31D, WM_DPICHANGED = 0x2E0, WM_DROPFILES = 0x233, WM_MOUSEACTIVATE = 0x21, WM_APP = 0x8000,
        WM_COMMAND = 0x111, WM_NCHITTEST = 0x84;

    public const int MA_NOACTIVATE = 3;
    public const uint PM_REMOVE = 1;
    public const uint ULW_ALPHA = 2;
    public const uint SWP_NOACTIVATE = 0x10, SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_SHOWWINDOW = 0x40;
    public const uint TME_LEAVE = 2;
    public const int SW_SHOWNOACTIVATE = 4, SW_HIDE = 0, SW_SHOWNORMAL = 1;
    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000;
    public const uint MF_STRING = 0, MF_CHECKED = 8, MF_SEPARATOR = 0x800;
    public const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 2;
    public const uint CF_UNICODETEXT = 13;
    public const uint GMEM_MOVEABLE = 2;

    public static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern ushort RegisterClassExW(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(int exStyle, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] public static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetMessageW(out MSG m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] public static extern bool PeekMessageW(out MSG m, IntPtr h, uint min, uint max, uint remove);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessageW(ref MSG m);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern int GetWindowLongW(IntPtr h, int idx);
    [DllImport("user32.dll")] public static extern int SetWindowLongW(IntPtr h, int idx, int val);
    [DllImport("user32.dll")]
    public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr hdcDst, ref POINT dst, ref SIZE size, IntPtr hdcSrc, ref POINT src, uint key, ref BLENDFUNCTION blend, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] public static extern IntPtr SetTimer(IntPtr h, IntPtr id, uint ms, IntPtr fn);
    [DllImport("user32.dll")] public static extern bool KillTimer(IntPtr h, IntPtr id);
    [DllImport("user32.dll")] public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT t);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, char* buf, int max);
    [DllImport("user32.dll")] public static extern IntPtr SetCapture(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfoW(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern IntPtr LoadCursorW(IntPtr inst, IntPtr name);
    [DllImport("user32.dll")] public static extern IntPtr SetCursor(IntPtr c);
    [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenuW(IntPtr menu, uint flags, IntPtr id, string? text);
    [DllImport("user32.dll")] public static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr h, IntPtr rect);
    [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr h);
    [DllImport("user32.dll")] public static extern bool OpenClipboard(IntPtr h);
    [DllImport("user32.dll")] public static extern bool CloseClipboard();
    [DllImport("user32.dll")] public static extern bool EmptyClipboard();
    [DllImport("user32.dll")] public static extern IntPtr GetClipboardData(uint fmt);
    [DllImport("user32.dll")] public static extern IntPtr SetClipboardData(uint fmt, IntPtr mem);
    [DllImport("user32.dll")] public static extern bool IsClipboardFormatAvailable(uint fmt);
    [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT* inputs, int size);
    [DllImport("user32.dll")] public static extern bool LockWorkStation();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageTimeoutW(IntPtr h, uint msg, IntPtr w, string l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string? cls, string? name);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref POINT p);
    [DllImport("user32.dll")]
    public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr hdcDst, IntPtr dst, ref SIZE size, IntPtr hdcSrc, ref POINT src, uint key, ref BLENDFUNCTION blend, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern uint ExtractIconExW(string file, int index, out IntPtr large, out IntPtr small, uint n);
    [DllImport("user32.dll")] public static extern bool EnumWindows(delegate* unmanaged<IntPtr, IntPtr, int> cb, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, char* buf, int max);
    [DllImport("user32.dll")] public static extern int GetWindowTextLengthW(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT2* inputs, int size);
    [DllImport("user32.dll")] public static extern short VkKeyScanW(char ch);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);

    public const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT2 { public uint type; public INPUTUNION u; }
    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT2 mi; [FieldOffset(0)] public KEYBDINPUT2 ki; }
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT2 { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT2 { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [DllImport("shell32.dll")] public static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, char* buf, ref int size);

    public static string WindowText(IntPtr h)
    {
        int n = GetWindowTextLengthW(h);
        if (n <= 0) return "";
        n = Math.Min(n, 255);
        char* buf = stackalloc char[n + 1];
        int got = GetWindowTextW(h, buf, n + 1);
        return new string(buf, 0, got);
    }

    public static string? ProcessPath(int pid)
    {
        var h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return null;
        try
        {
            char* buf = stackalloc char[520]; int n = 520;
            return QueryFullProcessImageNameW(h, 0, buf, ref n) ? new string(buf, 0, n) : null;
        }
        finally { CloseHandle(h); }
    }

    [DllImport("kernel32.dll")] public static extern bool K32GetProcessMemoryInfo(IntPtr proc, out PROCESS_MEMORY_COUNTERS c, int size);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);

    [DllImport("gdi32.dll")] public static extern int GetObjectW(IntPtr h, int size, out BITMAP bm);
    [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte* bits, ref BITMAPINFOHEADER bmi, uint usage);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandleW(IntPtr name);
    [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr proc, nint min, nint max);
    [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);
    [DllImport("kernel32.dll")] public static extern IntPtr GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] public static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(IntPtr h);
    [DllImport("kernel32.dll")] public static extern IntPtr GlobalFree(IntPtr h);

    [DllImport("dwmapi.dll")] public static extern int DwmFlush();
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr mon, int type, out uint dpiX, out uint dpiY);
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
    [DllImport("shell32.dll")] public static extern void DragAcceptFiles(IntPtr h, bool accept);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern uint DragQueryFileW(IntPtr drop, uint i, char* buf, uint len);
    [DllImport("shell32.dll")] public static extern void DragFinish(IntPtr drop);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr ShellExecuteW(IntPtr h, string? verb, string file, string? args, string? dir, int show);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHCreateItemFromParsingName(string path, IntPtr bc, in Guid riid, out IntPtr ppv);
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)] public static extern bool GetOpenFileNameW(ref OPENFILENAME ofn);
    [DllImport("powrprof.dll")] public static extern bool SetSuspendState(bool hibernate, bool force, bool disableWake);
    [DllImport("ole32.dll")] public static extern int CoCreateInstance(in Guid clsid, IntPtr outer, int ctx, in Guid iid, out IntPtr obj);
    [DllImport("ole32.dll")] public static extern int OleInitialize(IntPtr reserved);

    [DllImport("dxva2.dll")] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr mon, out uint count);
    [DllImport("dxva2.dll")] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr mon, uint count, PHYSICAL_MONITOR* arr);
    [DllImport("dxva2.dll")] public static extern bool GetMonitorBrightness(IntPtr mon, out uint min, out uint cur, out uint max);
    [DllImport("dxva2.dll")] public static extern bool SetMonitorBrightness(IntPtr mon, uint value);
    [DllImport("dxva2.dll")] public static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR* arr);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern int PdhOpenQueryW(string? src, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern int PdhAddEnglishCounterW(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] public static extern int PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern int PdhGetFormattedCounterArrayW(IntPtr counter, uint fmt, ref uint bufSize, out uint count, IntPtr buffer);
    [DllImport("pdh.dll")] public static extern int PdhCloseQuery(IntPtr query);

    public static int LoWord(IntPtr v) => (short)((long)v & 0xFFFF);
    public static int HiWord(IntPtr v) => (short)(((long)v >> 16) & 0xFFFF);

    public static string ClassName(IntPtr h)
    {
        char* buf = stackalloc char[128];
        int n = GetClassNameW(h, buf, 128);
        return new string(buf, 0, n);
    }
}
