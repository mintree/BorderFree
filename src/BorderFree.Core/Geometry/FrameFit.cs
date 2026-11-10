namespace BorderFree.Core.Geometry;

/// <summary>
/// Invisible resize margins around a window. Win32's outer rectangle is often
/// larger than the pixels the user actually sees; borderless placement has to
/// expand the outer rectangle so the visible frame lands on the requested one.
/// </summary>
public readonly record struct FrameExtras(int Left, int Top, int Right, int Bottom)
{
    public bool IsZero => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;

    public bool IsPlausible =>
        Left >= 0 && Top >= 0 && Right >= 0 && Bottom >= 0
        && Left < 64 && Top < 64 && Right < 64 && Bottom < 64;

    public static FrameExtras FromOuterAndVisible(PixelRect outer, PixelRect visible) =>
        new(
            visible.X - outer.X,
            visible.Y - outer.Y,
            outer.Right - visible.Right,
            outer.Bottom - visible.Bottom);
}

public static class FrameFit
{
    /// <summary>
    /// Returns the outer rectangle that makes <paramref name="desiredVisible"/> the visible frame.
    /// Returns false when the measured extras are missing or not believable, and leaves the desired
    /// rectangle unchanged so a bad DWM reading cannot fling the window off-screen.
    /// </summary>
    public static bool TryOuterForVisible(
        PixelRect desiredVisible,
        PixelRect currentOuter,
        PixelRect currentVisible,
        out PixelRect outer)
    {
        var extras = FrameExtras.FromOuterAndVisible(currentOuter, currentVisible);
        if (!extras.IsPlausible || extras.IsZero)
        {
            outer = desiredVisible;
            return false;
        }

        outer = new PixelRect(
            desiredVisible.X - extras.Left,
            desiredVisible.Y - extras.Top,
            desiredVisible.Width + extras.Left + extras.Right,
            desiredVisible.Height + extras.Top + extras.Bottom);
        return outer.HasArea;
    }
}
