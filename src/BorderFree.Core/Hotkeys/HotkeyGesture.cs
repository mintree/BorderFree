namespace BorderFree.Core.Hotkeys;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

/// <summary>
/// A global shortcut. At least one modifier is required so a bare letter cannot swallow typing.
/// </summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    public bool IsValid =>
        Modifiers != HotkeyModifiers.None && HotkeyKeys.TryGetVirtualKey(Key, out _);

    public string ToSettingString()
    {
        if (!HotkeyKeys.TryCanonical(Key, out var key))
            return string.Empty;

        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
            parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
            parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
            parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Windows))
            parts.Add("Win");
        parts.Add(key);
        return string.Join("+", parts);
    }

    public override string ToString() => ToSettingString();

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var tokens = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
            return false;

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        var seen = new HashSet<HotkeyModifiers>();
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            if (!TryModifier(tokens[i], out var modifier) || !seen.Add(modifier))
                return false;
            modifiers |= modifier;
        }

        if (modifiers == HotkeyModifiers.None)
            return false;
        if (!HotkeyKeys.TryCanonical(tokens[^1], out var key))
            return false;

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    private static bool TryModifier(string token, out HotkeyModifiers modifier)
    {
        switch (token.ToLowerInvariant())
        {
            case "ctrl":
            case "control":
                modifier = HotkeyModifiers.Control;
                return true;
            case "alt":
                modifier = HotkeyModifiers.Alt;
                return true;
            case "shift":
                modifier = HotkeyModifiers.Shift;
                return true;
            case "win":
            case "windows":
                modifier = HotkeyModifiers.Windows;
                return true;
            default:
                modifier = HotkeyModifiers.None;
                return false;
        }
    }
}

public static class HotkeyKeys
{
    private static readonly Dictionary<string, int> Map = Create();

    public static bool TryCanonical(string? key, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        foreach (var name in Map.Keys)
        {
            if (name.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                canonical = name;
                return true;
            }
        }

        return false;
    }

    public static bool TryGetVirtualKey(string? key, out int virtualKey)
    {
        virtualKey = 0;
        return TryCanonical(key, out var canonical) && Map.TryGetValue(canonical, out virtualKey);
    }

    private static Dictionary<string, int> Create()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < 26; i++)
            map[((char)('A' + i)).ToString()] = 0x41 + i;
        for (int i = 0; i <= 9; i++)
            map[i.ToString()] = 0x30 + i;
        for (int i = 1; i <= 24; i++)
            map["F" + i] = 0x70 + i - 1;
        for (int i = 0; i <= 9; i++)
            map["Num" + i] = 0x60 + i;

        map["Left"] = 0x25;
        map["Up"] = 0x26;
        map["Right"] = 0x27;
        map["Down"] = 0x28;
        map["Tab"] = 0x09;
        map["Space"] = 0x20;
        map["Enter"] = 0x0D;
        map["PageUp"] = 0x21;
        map["PageDown"] = 0x22;
        map["End"] = 0x23;
        map["Home"] = 0x24;
        map["Insert"] = 0x2D;
        map["Delete"] = 0x2E;
        map["Escape"] = 0x1B;
        return map;
    }
}
