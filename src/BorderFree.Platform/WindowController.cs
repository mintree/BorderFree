using System.Runtime.InteropServices;
using BorderFree.Core.Geometry;

namespace BorderFree.Platform;

public readonly record struct ApplyOutcome(bool Success, string Message)
{
    public static ApplyOutcome Ok(string message) => new(true, message);

    public static ApplyOutcome Fail(string message) => new(false, message);
}

/// <summary>
/// Strips the caption and frame from a top-level window and moves it.
/// The original style and placement are remembered for the rest of this session so Restore can put them back.
/// </summary>
public sealed class WindowController
{
    private readonly Dictionary<nint, WindowSnapshot> _snapshots = new();

    public bool CanRestore(nint hwnd) => hwnd != 0 && _snapshots.ContainsKey(hwnd);

    public ApplyOutcome ApplyBorderless(nint hwnd, PixelRect rect, bool activate)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd))
            return ApplyOutcome.Fail("That window is no longer open.");
        if (!rect.HasArea)
            return ApplyOutcome.Fail("The layout has no area.");

        bool firstTouch = !_snapshots.ContainsKey(hwnd);
        if (firstTouch)
        {
            if (!TryCapture(hwnd, out var snapshot, out var captureError))
                return ApplyOutcome.Fail(captureError);
            _snapshots[hwnd] = snapshot;
        }

        nint style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlStyle);
        if ((style & NativeMethods.WsChild) != 0)
        {
            if (firstTouch)
                _snapshots.Remove(hwnd);
            return ApplyOutcome.Fail("That window is part of another window, so it can't be placed on its own.");
        }

        if (NativeMethods.IsIconic(hwnd) || NativeMethods.IsZoomed(hwnd))
            NativeMethods.ShowWindow(hwnd, activate ? NativeMethods.SwRestore : NativeMethods.SwShowNoActivate);

        if (!TryClearFrameStyles(hwnd, out var styleError))
            return FailAndMaybeRollback(hwnd, firstTouch, styleError);

        uint flags = NativeMethods.SwpFrameChanged | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoOwnerZOrder;
        flags |= activate ? NativeMethods.SwpShowWindow : NativeMethods.SwpNoActivate;

        if (!NativeMethods.SetWindowPos(hwnd, 0, rect.X, rect.Y, rect.Width, rect.Height, flags))
            return FailAndMaybeRollback(hwnd, firstTouch, DescribeFailure("Windows refused to move that window."));

        if (TryMeasureFrame(hwnd, out var outer, out var visible)
            && FrameFit.TryOuterForVisible(rect, outer, visible, out var corrected)
            && !NativeMethods.SetWindowPos(hwnd, 0, corrected.X, corrected.Y, corrected.Width, corrected.Height, flags))
        {
            return FailAndMaybeRollback(hwnd, firstTouch, DescribeFailure("Windows refused to move that window."));
        }

        TryDisableRoundedCorners(hwnd);
        return ApplyOutcome.Ok($"Placed at {rect.Width}×{rect.Height}, {rect.X}, {rect.Y}.");
    }

    public ApplyOutcome Restore(nint hwnd)
    {
        if (!_snapshots.TryGetValue(hwnd, out var snapshot))
            return ApplyOutcome.Fail("BorderFree has not changed this window during this run.");
        if (!NativeMethods.IsWindow(hwnd))
        {
            _snapshots.Remove(hwnd);
            return ApplyOutcome.Fail("That window is no longer open.");
        }

        var error = RestoreSnapshot(hwnd, snapshot);
        if (error is not null)
            return ApplyOutcome.Fail(error);

        _snapshots.Remove(hwnd);
        return ApplyOutcome.Ok("Restored the original window frame.");
    }

    public void ForgetClosed()
    {
        foreach (var hwnd in _snapshots.Keys.ToList())
        {
            if (!NativeMethods.IsWindow(hwnd))
                _snapshots.Remove(hwnd);
        }
    }

    public static void TryUseDarkTitleBar(nint hwnd)
    {
        if (hwnd == 0)
            return;

        int enabled = 1;
        if (NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
    }

    private ApplyOutcome FailAndMaybeRollback(nint hwnd, bool firstTouch, string message)
    {
        if (firstTouch && _snapshots.TryGetValue(hwnd, out var snapshot))
        {
            RestoreSnapshot(hwnd, snapshot);
            _snapshots.Remove(hwnd);
        }

        return ApplyOutcome.Fail(message);
    }

    private static string? RestoreSnapshot(nint hwnd, WindowSnapshot snapshot)
    {
        if (!TrySetLong(hwnd, NativeMethods.GwlStyle, snapshot.Style, out var styleError))
            return styleError;
        if (!TrySetLong(hwnd, NativeMethods.GwlExStyle, snapshot.ExStyle, out var exError))
            return exError;

        var placement = snapshot.Placement;
        placement.length = Marshal.SizeOf<WINDOWPLACEMENT>();
        if (!NativeMethods.SetWindowPlacement(hwnd, ref placement))
            return DescribeFailure("Windows refused to restore that window.");

        uint flags = NativeMethods.SwpFrameChanged | NativeMethods.SwpNoMove | NativeMethods.SwpNoSize
            | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoOwnerZOrder | NativeMethods.SwpNoActivate;
        if (!NativeMethods.SetWindowPos(hwnd, 0, 0, 0, 0, 0, flags))
            return DescribeFailure("Windows refused to restore that window.");

        return null;
    }

    private static bool TryCapture(nint hwnd, out WindowSnapshot snapshot, out string error)
    {
        snapshot = default;
        error = string.Empty;
        NativeMethods.SetLastError(0);
        nint style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlStyle);
        int styleError = Marshal.GetLastWin32Error();
        if (style == 0 && styleError != 0)
        {
            error = DescribeFailure("Windows refused to read that window.");
            return false;
        }

        NativeMethods.SetLastError(0);
        nint exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle);
        int exError = Marshal.GetLastWin32Error();
        if (exStyle == 0 && exError != 0)
        {
            error = DescribeFailure("Windows refused to read that window.");
            return false;
        }

        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!NativeMethods.GetWindowPlacement(hwnd, ref placement))
        {
            error = DescribeFailure("Windows refused to read that window.");
            return false;
        }

        snapshot = new WindowSnapshot(style, exStyle, placement);
        return true;
    }

    private static bool TryClearFrameStyles(nint hwnd, out string error)
    {
        nint style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlStyle);
        // The mask is a 32-bit window style. Invert it in 32 bits, then sign-extend,
        // so the high half of a 64-bit LONG_PTR is preserved.
        nint updated = style & unchecked((nint)(int)~NativeMethods.BorderlessStyleMask);
        updated |= NativeMethods.WsVisible;
        if (!TrySetLong(hwnd, NativeMethods.GwlStyle, updated, out error))
            return false;

        nint exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle);
        nint updatedEx = exStyle & unchecked((nint)(int)~NativeMethods.BorderlessExStyleMask);
        return TrySetLong(hwnd, NativeMethods.GwlExStyle, updatedEx, out error);
    }

    private static bool TrySetLong(nint hwnd, int index, nint value, out string error)
    {
        error = string.Empty;
        NativeMethods.SetLastError(0);
        NativeMethods.SetWindowLongPtr(hwnd, index, value);
        int code = Marshal.GetLastWin32Error();
        if (code == 0)
            return true;

        error = DescribeCode("Windows refused to change that window.", code);
        return false;
    }

    private static bool TryMeasureFrame(nint hwnd, out PixelRect outer, out PixelRect visible)
    {
        outer = default;
        visible = default;
        if (!NativeMethods.GetWindowRect(hwnd, out var outerRect))
            return false;
        if (NativeMethods.DwmGetWindowAttribute(
                hwnd,
                NativeMethods.DwmwaExtendedFrameBounds,
                out RECT visibleRect,
                Marshal.SizeOf<RECT>()) != 0)
            return false;

        outer = outerRect.ToPixelRect();
        visible = visibleRect.ToPixelRect();
        return outer.HasArea && visible.HasArea;
    }

    private static void TryDisableRoundedCorners(nint hwnd)
    {
        int preference = NativeMethods.DwmwcpDoNotRound;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }

    private static string DescribeFailure(string action) =>
        DescribeCode(action, Marshal.GetLastWin32Error());

    private static string DescribeCode(string action, int code)
    {
        var message = $"{action} Win32 error {code}.";
        if (code == 5)
            message += " If that program is running as administrator, restart BorderFree as administrator.";
        return message;
    }

    private readonly record struct WindowSnapshot(nint Style, nint ExStyle, WINDOWPLACEMENT Placement);
}
