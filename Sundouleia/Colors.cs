using Dalamud.Interface;
using Sundouleia.PlayerClient;

namespace Sundouleia;

// Everything in here is just a placeholder for right now.
public enum SundColor
{
    Gold,
    GoldAlpha, // Maybe remove, idk
    Silver,
    Light,
    LightAlpha, // Maybe remove.
    Dark,
}

// Placeholder idk if i'll even keep
public struct SundColorData
{
    public string Name;
    public string Description;
    public uint Uint;
    public Vector4 Vec4;
}

// Controls both colors and styles. Allows for some customization and stuff i guess.
public static class ColorStyle
{
    public static Vector4 Vec4(this SundColor color)
        => color switch
        {
            SundColor.Gold => new Vector4(.957f, .682f, .294f, 1f),
            SundColor.GoldAlpha => new Vector4(.957f, .682f, .294f, .7f),
            SundColor.Silver => new Vector4(.778f, .778f, .778f, 1f),
            SundColor.Light => new Vector4(.318f, .137f, .196f, 1f),
            SundColor.LightAlpha => new Vector4(.318f, .137f, .196f, .5f),
            SundColor.Dark => new Vector4(.282f, .118f, .173f, 1f),
            _ => Vector4.Zero,
        };

    public static uint Uint(this SundColor color)
        => color switch
        {
            SundColor.Gold => SundColor.Gold.Vec4().ToUint(),
            SundColor.GoldAlpha => SundColor.GoldAlpha.Vec4().ToUint(),
            SundColor.Silver => SundColor.Silver.Vec4().ToUint(),
            SundColor.Light => SundColor.Light.Vec4().ToUint(),
            SundColor.LightAlpha => SundColor.LightAlpha.Vec4().ToUint(),
            SundColor.Dark => SundColor.Dark.Vec4().ToUint(),
            _ => 0x00000000,
        };

    // Helper functions for when we add new colors
    public static uint ToUint(this Vector4 color)
        => ColorHelpers.RgbaVector4ToUint(color);

    // Helper functions for when we add new colors
    public static Vector4 ToVector4(this uint color)
        => ColorHelpers.RgbaUintToVector4(color);

    // Later, idk, dont worry about it today, im so stressed im going to pass out.
    // private static IReadOnlyDictionary<ColorId, uint> _colors = new Dictionary<ColorId, uint>();


    /// <summary> Set the configurable colors dictionary to a value. </summary>
    public static void SetColors(MainConfig config)
    {
        // Update the colors here eventually.
    }
}

