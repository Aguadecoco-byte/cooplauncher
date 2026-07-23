using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RemotePlayLauncher;

public static class WindowDiscoveryService
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const int DwmaCloaked = 14;

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr64(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern nint GetWindowLong32(nint hwnd, int index);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static IReadOnlyList<RunningWindowInfo> Discover()
    {
        var ownPid = Environment.ProcessId;
        var results = new List<RunningWindowInfo>();

        EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!IsWindowVisible(hwnd) || GetWindowTextLength(hwnd) <= 0)
                    return true;
                if (((long)GetWindowLongPtr(hwnd, GwlExStyle) & WsExToolWindow) != 0)
                    return true;
                if (DwmGetWindowAttribute(hwnd, DwmaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;
                if (!GetWindowRect(hwnd, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                    return true;

                GetWindowThreadProcessId(hwnd, out var pidValue);
                var pid = unchecked((int)pidValue);
                if (pid <= 0 || pid == ownPid)
                    return true;

                var titleBuffer = new StringBuilder(GetWindowTextLength(hwnd) + 1);
                GetWindowText(hwnd, titleBuffer, titleBuffer.Capacity);
                var title = titleBuffer.ToString().Trim();
                if (title.Length == 0) return true;

                using var process = Process.GetProcessById(pid);
                string path;
                try { path = process.MainModule?.FileName ?? string.Empty; }
                catch { path = string.Empty; }

                results.Add(new RunningWindowInfo
                {
                    Handle = hwnd,
                    ProcessId = pid,
                    Title = title,
                    ProcessName = process.ProcessName,
                    ExecutablePath = path
                });
            }
            catch (Exception ex)
            {
                AppLog.Write("A window could not be inspected.", ex);
            }
            return true;
        }, 0);

        return results
            .GroupBy(item => item.Handle)
            .Select(group => group.First())
            .OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static nint GetWindowLongPtr(nint hwnd, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : GetWindowLong32(hwnd, index);
}
