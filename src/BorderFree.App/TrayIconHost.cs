using System.Runtime.InteropServices;
using System.Text;

namespace BorderFree;

/// <summary>
/// Notification-area icon via Shell_NotifyIcon. Open and Exit stay available while the main window is hidden.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    public const int CallbackMessage = 0x8001;

    private const int NimAdd = 0x0;
    private const int NimDelete = 0x2;
    private const int NifMessage = 0x1;
    private const int NifIcon = 0x2;
    private const int NifTip = 0x4;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmRButtonUp = 0x0205;
    private const int WmContextMenu = 0x007B;
    private const uint MfString = 0x0;
    private const uint TpmRightAlign = 0x0008;
    private const uint TpmBottomAlign = 0x0020;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmNonotify = 0x0080;
    private const uint IdOpen = 1;
    private const uint IdReapply = 2;
    private const uint IdExit = 3;

    private readonly nint _hwnd;
    private readonly Action _open;
    private readonly Action _reapply;
    private readonly Action _exit;
    private readonly nint _icon;
    private readonly bool _destroyIcon;
    private bool _added;

    public TrayIconHost(nint hwnd, Action open, Action reapply, Action exit)
    {
        _hwnd = hwnd;
        _open = open;
        _reapply = reapply;
        _exit = exit;
        (_icon, _destroyIcon) = LoadIcon();
        var data = Data();
        _added = ShellNotifyIcon(NimAdd, ref data);
    }

    public bool HandleMessage(int message, nint lParam)
    {
        if (message != CallbackMessage)
            return false;

        int mouse = unchecked((int)(lParam.ToInt64() & 0xFFFF));
        if (mouse == WmLButtonDblClk)
            _open();
        else if (mouse is WmRButtonUp or WmContextMenu)
            ShowMenu();
        return true;
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = Data();
            ShellNotifyIcon(NimDelete, ref data);
            _added = false;
        }

        if (_destroyIcon && _icon != 0)
            DestroyIcon(_icon);
    }

    private NotifyIconData Data()
    {
        return new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
            szTip = "BorderFree"
        };
    }

    private void ShowMenu()
    {
        nint menu = CreatePopupMenu();
        if (menu == 0)
            return;

        try
        {
            AppendMenu(menu, MfString, IdOpen, "Open");
            AppendMenu(menu, MfString, IdReapply, "Reapply layout");
            AppendMenu(menu, MfString, IdExit, "Exit");
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
            PostMessage(_hwnd, 0, 0, 0);
            switch (command)
            {
                case IdOpen:
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
        }
    }

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
                nint extracted = ExtractAssociatedIcon(0, buffer, ref index);
                if (extracted != 0)
                    return (extracted, true);
            }
        }
        catch (Exception)
        {
            // Fall through to the shared application icon.
        }

        return (LoadIcon(0, 32512), false);
    }

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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool ShellNotifyIcon(int message, ref NotifyIconData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint ExtractAssociatedIcon(nint instance, StringBuilder iconPath, ref ushort iconIndex);

    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    private static extern nint LoadIcon(nint instance, nint iconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint unused);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
