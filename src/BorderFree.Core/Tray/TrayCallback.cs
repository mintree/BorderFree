namespace BorderFree.Core.Tray;

public enum TrayAction
{
    None,
    Open,
    ShowMenu
}

/// <summary>
/// Turns a Shell_NotifyIcon callback into an action.
/// Version 0 puts the mouse message in lParam. NOTIFYICON_VERSION_4 puts it in the low word of lParam
/// (the high word is the icon id) and also sends NIN_SELECT, NIN_KEYSELECT, and WM_CONTEXTMENU.
/// Reading the low word works for both.
/// </summary>
public static class TrayCallback
{
    public const int WmContextMenu = 0x007B;
    public const int WmLButtonUp = 0x0202;
    public const int WmLButtonDblClk = 0x0203;
    public const int WmRButtonUp = 0x0205;
    public const int NinSelect = 0x0400;
    public const int NinKeySelect = 0x0401;

    public static int MouseMessage(long lParam) => unchecked((int)(lParam & 0xFFFF));

    public static TrayAction Interpret(long lParam, bool version4)
    {
        int message = MouseMessage(lParam);
        switch (message)
        {
            case WmLButtonUp:
            case WmLButtonDblClk:
            case NinSelect:
            case NinKeySelect:
                return TrayAction.Open;
            case WmContextMenu:
                return TrayAction.ShowMenu;
            case WmRButtonUp:
                // Version 4 follows a right click with WM_CONTEXTMENU, so handling both would open the menu twice.
                return version4 ? TrayAction.None : TrayAction.ShowMenu;
            default:
                return TrayAction.None;
        }
    }
}
