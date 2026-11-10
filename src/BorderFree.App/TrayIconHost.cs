using System.Runtime.InteropServices;
using System.Text;
using BorderFree.Core.Tray;

namespace BorderFree;

/// <summary>
/// Notification-area icon via Shell_NotifyIconW. Open and Exit stay available while the main window is hidden.
/// <see cref="IsAdded"/> says whether the icon is actually in the tray; callers must not hide the window when it is false.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    public const int CallbackMessage = 0x8001; // WM_APP + 1

    private const int NimAdd = 0x0;
    private const int NimModify = 0x1;
    private const int NimDelete = 0x2;
    private const int NimSetVersion = 0x4;
    private const uint NotifyIconVersion4 = 4;
    private const uint NifMessage = 0x1;
    private const uint NifIcon = 0x2;
    private const uint NifTip = 0x4;
    private const uint NifInfo = 0x10;
    private const uint NifShowTip = 0x80;
    private const uint NiifInfo = 0x1;
    private const uint IconId = 1;
    private const uint MfString = 0x0;
    private const uint TpmRightAlign = 0x0008;
    private const uint TpmBottomAlign = 0x0020;
    private const uint TpmNonotify = 0x0080;
    private const uint TpmReturnCmd = 0x0100;
    private const uint WmNull = 0x0;
    private const uint MsgfltAllow = 1;
    private const uint IdOpen = 1;
    private const uint IdReapply = 2;
    private const uint IdExit = 3;
    private const int RetryDelayMs = 750;

    /// <summary>Expected Marshal.SizeOf(NOTIFYICONDATAW) on x64.</summary>
    public const int ExpectedStructSizeX64 = 976;

    private static readonly TimeSpan OpenDebounce = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan MenuDebounce = TimeSpan.FromMilliseconds(300);

    private readonly nint _hwnd;
    private readonly Action _open;
    private readonly Action _reapply;
    private readonly Action _exit;
    private readonly nint _icon;
    private readonly bool _destroyIcon;
    private readonly uint _taskbarCreated;
    private bool _version4;
    private bool _inMenu;
    private DateTime _lastOpenUtc = DateTime.MinValue;
    private DateTime _menuClosedUtc = DateTime.MinValue;
    private bool _disposed;

    public TrayIconHost(nint hwnd, Action open, Action reapply, Action exit)
    {
        _hwnd = hwnd;
        _open = open;
        _reapply = reapply;
        _exit = exit;
        (_icon, _destroyIcon) = LoadIcon();

        int size = Marshal.SizeOf<NotifyIconData>();
        AppLog.Write($"Tray: NOTIFYICONDATAW size {size} (expected {ExpectedStructSizeX64} on x64), icon handle {(_icon != 0 ? "ok" : "missing")}.");

        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        if (_taskbarCreated != 0)
        {
            // An elevated process doesn't get Explorer's broadcast unless it opts in.
            ChangeWindowMessageFilterEx(_hwnd, _taskbarCreated, MsgfltAllow, 0);
        }

        IsAdded = TryAdd(allowRetry: true);
    }

    /// <summary>True when the icon is currently in the notification area.</summary>
    public bool IsAdded { get; private set; }

    /// <summary>Raised after the icon is re-added because Explorer restarted.</summary>
    public event Action<bool>? Readded;

    public bool HandleMessage(int message, nint lParam)
    {
        if (_taskbarCreated != 0 && unchecked((uint)message) == _taskbarCreated)
        {
            AppLog.Write("Tray: TaskbarCreated received (Explorer started or restarted). Re-adding the icon.");
            IsAdded = TryAdd(allowRetry: false);
            Readded?.Invoke(IsAdded);
            return false; // Let others see the broadcast too.
        }

        if (message != CallbackMessage)
            return false;

        switch (TrayCallback.Interpret(lParam.ToInt64(), _version4))
        {
            case TrayAction.Open:
                var now = DateTime.UtcNow;
                if (now - _lastOpenUtc >= OpenDebounce)
                {
                    _lastOpenUtc = now;
                    _open();
                }

                break;
            case TrayAction.ShowMenu:
                if (!_inMenu && DateTime.UtcNow - _menuClosedUtc >= MenuDebounce)
                    ShowMenu();
                break;
        }

        return true;
    }

    /// <summary>Shows a notification balloon (a toast on Windows 10/11) from the tray icon.</summary>
    public bool ShowBalloon(string title, string text)
    {
        if (!IsAdded)
            return false;

        var data = BaseData();
        data.uFlags = NifInfo | NifTip | NifShowTip;
        data.szTip = "BorderFree";
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(text, 255);
        data.dwInfoFlags = NiifInfo;
        bool ok = Shell_NotifyIconW(NimModify, ref data);
        if (!ok)
            AppLog.Write($"Tray: balloon failed, Win32 error {Marshal.GetLastWin32Error()}.");
        return ok;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (IsAdded)
        {
            var data = BaseData();
            Shell_NotifyIconW(NimDelete, ref data);
            IsAdded = false;
        }

        if (_destroyIcon && _icon != 0)
            DestroyIcon(_icon);
    }

    private bool TryAdd(bool allowRetry)
    {
        var data = FullData();
        if (Shell_NotifyIconW(NimAdd, ref data))
        {
            AppLog.Write("Tray: NIM_ADD succeeded.");
            SetVersion();
            return true;
        }

        int error = Marshal.GetLastWin32Error();
        AppLog.Write($"Tray: NIM_ADD failed, Win32 error {error}.");
        if (!allowRetry)
            return TryModifyExisting();

        // At login Explorer may not be ready yet, and a timed-out add can still have gone through.
        Thread.Sleep(RetryDelayMs);
        if (TryModifyExisting())
            return true;

        data = FullData();
        if (Shell_NotifyIconW(NimAdd, ref data))
        {
            AppLog.Write("Tray: NIM_ADD retry succeeded.");
            SetVersion();
            return true;
        }

        AppLog.Write($"Tray: NIM_ADD retry failed, Win32 error {Marshal.GetLastWin32Error()}. Running without a tray icon.");
        return false;
    }

    private bool TryModifyExisting()
    {
        // If the icon is already there (same window and id), NIM_MODIFY succeeds.
        var data = FullData();
        if (!Shell_NotifyIconW(NimModify, ref data))
            return false;

        AppLog.Write("Tray: icon already present (NIM_MODIFY succeeded).");
        SetVersion();
        return true;
    }

    private void SetVersion()
    {
        var data = BaseData();
        data.uTimeoutOrVersion = NotifyIconVersion4;
        _version4 = Shell_NotifyIconW(NimSetVersion, ref data);
        AppLog.Write(_version4
            ? "Tray: NOTIFYICON_VERSION_4 set."
            : $"Tray: NIM_SETVERSION failed (Win32 error {Marshal.GetLastWin32Error()}), using version 0 callbacks.");
    }

    private NotifyIconData BaseData()
    {
        return new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _hwnd,
            uID = IconId,
            szTip = string.Empty,
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
    }

    private NotifyIconData FullData()
    {
        var data = BaseData();
        data.uFlags = NifMessage | NifIcon | NifTip | NifShowTip;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        data.szTip = "BorderFree";
        return data;
    }

    private void ShowMenu()
    {
        nint menu = CreatePopupMenu();
        if (menu == 0)
            return;

        _inMenu = true;
        try
        {
            AppendMenuW(menu, MfString, IdOpen, "Open");
            AppendMenuW(menu, MfString, IdReapply, "Reapply layout");
            AppendMenuW(menu, MfString, IdExit, "Exit");
            GetCursorPos(out var point);
            SetForegroundWindow(_hwnd);
            uint command = TrackPopupMenu(
                menu,
                TpmRightAlign | TpmBottomAlign | TpmReturnCmd | TpmNonotify,
                point.X,
                point.Y,
                0,
                _hwnd,
                0);
            PostMessageW(_hwnd, WmNull, 0, 0);
            switch (command)
            {
                case IdOpen:
                    _lastOpenUtc = DateTime.UtcNow;
                    _open();
                    break;
                case IdReapply:
                    _reapply();
                    break;
                case IdExit:
                    _exit();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
            _inMenu = false;
            _menuClosedUtc = DateTime.UtcNow;
        }
    }

    private static string Truncate(string text, int max) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Length <= max ? text : text[..max];

    private static (nint Icon, bool Owned) LoadIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                var buffer = new StringBuilder(Math.Max(path.Length + 1, 260));
                buffer.Append(path);
                ushort index = 0;
                nint extracted = ExtractAssociatedIconW(0, buffer, ref index);
                if (extracted != 0)
                    return (extracted, true);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"Tray: ExtractAssociatedIconW failed: {ex.Message}");
        }

        return (LoadIconW(0, 32512), false); // IDI_APPLICATION
    }

    /// <summary>NOTIFYICONDATAW. 976 bytes on x64 with the default sequential layout.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(int message, ref NotifyIconData data);

    [DllImport("shell32.dll", EntryPoint = "ExtractAssociatedIconW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint ExtractAssociatedIconW(nint instance, StringBuilder iconPath, ref ushort iconIndex);

    [DllImport("user32.dll", EntryPoint = "LoadIconW", ExactSpelling = true)]
    private static extern nint LoadIconW(nint instance, nint iconName);

    [DllImport("user32.dll", EntryPoint = "DestroyIcon", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll", EntryPoint = "CreatePopupMenu", ExactSpelling = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(nint menu, uint flags, nuint id, string text);

    [DllImport("user32.dll", EntryPoint = "TrackPopupMenu", ExactSpelling = true)]
    private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint unused);

    [DllImport("user32.dll", EntryPoint = "DestroyMenu", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", EntryPoint = "GetCursorPos", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll", EntryPoint = "ChangeWindowMessageFilterEx", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(nint window, uint message, uint action, nint changeFilterStruct);
}
