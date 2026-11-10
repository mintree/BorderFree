using System.Text;
using BorderFree.Core.Geometry;

namespace BorderFree.Platform;

public sealed record WindowDetails(
    nint Handle,
    int ProcessId,
    string Title,
    string ExecutablePath,
    string ExecutableName,
    PixelRect Bounds);

public static class WindowCatalog
{
    private static readonly HashSet<string> SkippedClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "DV2ControlHost",
        "MsgrIMEWindowClass",
        "SysShadow"
    };

    public static nint ForegroundHandle() => NativeMethods.GetForegroundWindow();

    public static IReadOnlyList<WindowDetails> ListVisibleWindows()
    {
        var found = new List<WindowDetails>();
        NativeMethods.EnumWindowsProc callback = (hwnd, _) =>
        {
            if (TryDescribe(hwnd, requireTitle: true, out var details))
                found.Add(details);
            return true;
        };

        if (!NativeMethods.EnumWindows(callback, 0))
            throw new InvalidOperationException("Windows could not list the open windows.");

        GC.KeepAlive(callback);
        found.Sort(static (a, b) =>
        {
            int byExe = string.Compare(a.ExecutableName, b.ExecutableName, StringComparison.OrdinalIgnoreCase);
            return byExe != 0
                ? byExe
                : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
        });
        return found;
    }

    /// <summary>
    /// The top-level window that should receive a hotkey, or null when the foreground
    /// window is BorderFree itself or not something we can place.
    /// </summary>
    public static WindowDetails? FromForeground(nint foreground, nint ourWindow)
    {
        if (foreground == 0)
            return null;

        var root = NativeMethods.GetAncestor(foreground, NativeMethods.GaRoot);
        if (root == 0)
            root = foreground;
        if (root == ourWindow)
            return null;

        NativeMethods.GetWindowThreadProcessId(root, out uint processId);
        if (processId == (uint)Environment.ProcessId)
            return null;

        return TryDescribe(root, requireTitle: false, out var details) ? details : null;
    }

    public static bool TryGetBounds(nint hwnd, out PixelRect bounds)
    {
        bounds = default;
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd) || !NativeMethods.GetWindowRect(hwnd, out var rect))
            return false;

        bounds = rect.ToPixelRect();
        return bounds.HasArea;
    }

    private static bool TryDescribe(nint hwnd, bool requireTitle, out WindowDetails details)
    {
        details = null!;
        if (hwnd == 0 || !NativeMethods.IsWindowVisible(hwnd) || IsCloaked(hwnd))
            return false;

        var className = ReadClassName(hwnd);
        if (SkippedClasses.Contains(className))
            return false;

        nint style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlStyle);
        nint exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle);
        if ((style & NativeMethods.WsChild) != 0)
            return false;
        if ((exStyle & NativeMethods.WsExToolWindow) != 0 && (exStyle & NativeMethods.WsExAppWindow) == 0)
            return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        if (processId == 0 || processId == (uint)Environment.ProcessId)
            return false;

        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            return false;
        var bounds = rect.ToPixelRect();
        if (!bounds.HasArea)
            return false;

        string title = ReadTitle(hwnd);
        if (requireTitle && string.IsNullOrWhiteSpace(title))
            return false;

        string path = QueryImagePath(processId);
        string exe = string.IsNullOrEmpty(path) ? string.Empty : Path.GetFileName(path);
        details = new WindowDetails(hwnd, (int)processId, title, path, exe, bounds);
        return true;
    }

    private static bool IsCloaked(nint hwnd)
    {
        int cloaked = 0;
        return NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCloaked, out cloaked, sizeof(int)) == 0
            && cloaked != 0;
    }

    private static string ReadTitle(nint hwnd)
    {
        int length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0)
            return string.Empty;

        var buffer = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string ReadClassName(nint hwnd)
    {
        var buffer = new StringBuilder(256);
        return NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : string.Empty;
    }

    private static string QueryImagePath(uint processId)
    {
        nint process = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (process == 0)
            return string.Empty;

        try
        {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            return NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref size)
                ? buffer.ToString()
                : string.Empty;
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }
}
