using BorderFree.Core.Geometry;

namespace BorderFree.Core.Layout;

/// <summary>
/// Turns a monitor rectangle into a window rectangle.
/// Centered presets keep the largest frame of that exact ratio inside the area.
/// Halves split the width and tile it, so on a true 32:9 panel each half is 16:9.
/// Full 32:9 uses the whole reference rectangle, which is the full 32:9 frame
/// when that display is the one selected.
/// </summary>
public static class PresetCalculator
{
    public static PixelRect Compute(LayoutPreset preset, PixelRect area)
    {
        if (!area.HasArea)
            throw new ArgumentOutOfRangeException(nameof(area), "The monitor area must have a positive width and height.");

        return preset switch
        {
            LayoutPreset.Centered16x9 => FitAspect(area, 16, 9),
            LayoutPreset.Centered21x9 => FitAspect(area, 21, 9),
            LayoutPreset.Full32x9 => area,
            LayoutPreset.LeftHalf => Half(area, left: true),
            LayoutPreset.RightHalf => Half(area, left: false),
            LayoutPreset.Custom => throw new ArgumentOutOfRangeException(nameof(preset), "A custom layout stores an explicit rectangle."),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown layout.")
        };
    }

    public static PixelRect FitAspect(PixelRect area, int aspectWidth, int aspectHeight)
    {
        if (!area.HasArea)
            throw new ArgumentOutOfRangeException(nameof(area), "The monitor area must have a positive width and height.");
        if (aspectWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(aspectWidth));
        if (aspectHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(aspectHeight));

        int width = RoundDiv(area.Height, aspectWidth, aspectHeight);
        int height = area.Height;
        if (width > area.Width)
        {
            width = area.Width;
            height = RoundDiv(area.Width, aspectHeight, aspectWidth);
        }

        if (width > area.Width)
            width = area.Width;
        if (height > area.Height)
            height = area.Height;
        if (width < 1)
            width = 1;
        if (height < 1)
            height = 1;

        int x = area.X + (area.Width - width) / 2;
        int y = area.Y + (area.Height - height) / 2;
        return new PixelRect(x, y, width, height);
    }

    public static PixelRect Half(PixelRect area, bool left)
    {
        if (!area.HasArea)
            throw new ArgumentOutOfRangeException(nameof(area), "The monitor area must have a positive width and height.");

        int leftWidth = area.Width / 2;
        int rightWidth = area.Width - leftWidth;
        if (leftWidth < 1 || rightWidth < 1)
            throw new ArgumentOutOfRangeException(nameof(area), "The area is too narrow to split in half.");

        return left
            ? new PixelRect(area.X, area.Y, leftWidth, area.Height)
            : new PixelRect(area.X + leftWidth, area.Y, rightWidth, area.Height);
    }

    /// <summary>
    /// Rounds <c>length * numerator / denominator</c> to the nearest integer, with halves away from zero.
    /// </summary>
    public static int RoundDiv(int length, int numerator, int denominator)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));
        if (numerator < 0)
            throw new ArgumentOutOfRangeException(nameof(numerator));
        if (denominator <= 0)
            throw new ArgumentOutOfRangeException(nameof(denominator));

        long product = (long)length * numerator;
        return (int)((product + (denominator / 2)) / denominator);
    }
}
