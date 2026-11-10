namespace BorderFree.Core.Geometry;

/// <summary>
/// An axis-aligned rectangle in physical pixels on the Windows virtual screen.
/// X and Y may be negative when a display sits left of or above the origin.
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool HasArea => Width > 0 && Height > 0;

    public override string ToString() => $"{Width}×{Height} at {X}, {Y}";
}
