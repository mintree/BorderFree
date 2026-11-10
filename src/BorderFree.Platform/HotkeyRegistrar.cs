using System.Runtime.InteropServices;
using BorderFree.Core.Hotkeys;

namespace BorderFree.Platform;

public static class HotkeyRegistrar
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    public static bool TryRegister(nint hwnd, int id, HotkeyGesture gesture, out string? error)
    {
        error = null;
        if (hwnd == 0)
        {
            error = "BorderFree is not ready to listen for shortcuts yet.";
            return false;
        }

        if (!gesture.IsValid || !HotkeyKeys.TryGetVirtualKey(gesture.Key, out int virtualKey))
        {
            error = $"The shortcut {gesture.ToSettingString()} is not valid.";
            return false;
        }

        uint modifiers = ToNativeModifiers(gesture.Modifiers) | ModNoRepeat;
        if (NativeMethods.RegisterHotKey(hwnd, id, modifiers, (uint)virtualKey))
            return true;

        int code = Marshal.GetLastWin32Error();
        error = $"Couldn't register {gesture.ToSettingString()}. Another program may already be using it (Win32 error {code}).";
        return false;
    }

    public static void Unregister(nint hwnd, int id)
    {
        if (hwnd != 0)
            NativeMethods.UnregisterHotKey(hwnd, id);
    }

    public static uint ToNativeModifiers(HotkeyModifiers modifiers)
    {
        uint value = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Alt))
            value |= ModAlt;
        if (modifiers.HasFlag(HotkeyModifiers.Control))
            value |= ModControl;
        if (modifiers.HasFlag(HotkeyModifiers.Shift))
            value |= ModShift;
        if (modifiers.HasFlag(HotkeyModifiers.Windows))
            value |= ModWin;
        return value;
    }
}
