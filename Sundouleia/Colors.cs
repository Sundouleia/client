using CkCommons;
using Dalamud.Interface;
using Sundouleia.PlayerClient;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace Sundouleia;

// Everything in here is just a placeholder for right now.
public enum SundCol
{
    ShopKeeperColor,
    ShopKeeperText,

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
public static class SundColors
{
    public static readonly int Count = Enum.GetValues<SundCol>().Length;
    private static readonly Vector4[] _vec4 = new Vector4[Count];
    private static readonly uint[] _u32 = new uint[Count];

    // Static constructor runs once, ensures _vec4 and _u32 are populated immediately
    static SundColors()
    {
        foreach (var kvp in Defaults)
        {
            int index = (int)kvp.Key;
            _vec4[index] = kvp.Value;
            _u32[index] = kvp.Value.ToUint();
        }
    }

    public static Dictionary<SundCol, Vector4> AsVec4Dictionary()
    => Enumerable.Range(0, Count).ToDictionary(i => (SundCol)i, i => _vec4[i]);

    public static Dictionary<SundCol, uint> AsUintDictionary()
        => Enumerable.Range(0, Count).ToDictionary(i => (SundCol)i, i => _u32[i]);

    public static void SetColors(MainConfig config)
    {
        foreach (var kvp in config.SundColors)
            Set(kvp.Key, kvp.Value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Set(SundCol var, Vector4 col)
    {
        _vec4[(int)var] = col;
        _u32[(int)var] = col.ToUint();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Set(SundCol var, uint col)
    {
        _u32[(int)var] = col;
        _vec4[(int)var] = col.ToVec4();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RevertCol(SundCol col)
    {
        var defaultCol = Defaults[col];
        _vec4[(int)col] = defaultCol;
        _u32[(int)col] = defaultCol.ToUint();
    }

    public static void RevertAll()
    {
        foreach (var kvp in Defaults)
        {
            int index = (int)kvp.Key;
            _vec4[index] = kvp.Value;
            _u32[index] = kvp.Value.ToUint();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Uint(this SundCol col) => _u32[(int)col];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Vec4(this SundCol col) => _vec4[(int)col];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref Vector4 Vec4Ref(this SundCol col) => ref _vec4[(int)col];

    public static uint ToUint(this Vector4 color)
    {
        var r = (byte)(color.X * 255);
        var g = (byte)(color.Y * 255);
        var b = (byte)(color.Z * 255);
        var a = (byte)(color.W * 255);

        return (uint)((a << 24) | (b << 16) | (g << 8) | r);
    }

    public static Vector4 ToVec4(this uint color)
    {
        var r = (color & 0x000000FF) / 255f;
        var g = ((color & 0x0000FF00) >> 8) / 255f;
        var b = ((color & 0x00FF0000) >> 16) / 255f;
        var a = ((color & 0xFF000000) >> 24) / 255f;
        return new Vector4(r, g, b, a);
    }

    /// <summary>
    ///     Converts the colors to the config dictionary format.
    /// </summary>
    public static Dictionary<SundCol, uint> ToConfigDict()
    {
        var dict = new Dictionary<SundCol, uint>(Count);
        for (int i = 0; i < Count; i++)
            dict[(SundCol)i] = _u32[i];
        return dict;
    }

    // Default color mapping from CkCol (example, fill in your actual colors)
    public static readonly IReadOnlyDictionary<SundCol, Vector4> Defaults = new Dictionary<SundCol, Vector4>
    {
        { SundCol.ShopKeeperColor,  new Vector4(0.886f, 0.407f, 0.658f, 1.000f) },
        { SundCol.ShopKeeperText,   new Vector4(1.000f, 0.711f, 0.843f, 1.000f) },

        { SundCol.Gold,             new Vector4(0.957f, 0.682f, 0.294f, 1.000f) },
        { SundCol.GoldAlpha,        new Vector4(.957f, .682f, .294f, .7f) },
        { SundCol.Silver,           new Vector4(.778f, .778f, .778f, 1f) },
        { SundCol.Light,            new Vector4(.318f, .137f, .196f, 1f) },
        { SundCol.LightAlpha,       new Vector4(.318f, .137f, .196f, .5f) },
        { SundCol.Dark,             new Vector4(.282f, .118f, .173f, 1f) },
    };

    public static string ToName(this SundCol idx) => idx switch
    {
        SundCol.ShopKeeperColor => "Shopkeeper Color",
        SundCol.ShopKeeperText  => "Shopkeeper Text",
        SundCol.Gold            => "Gold",
        SundCol.GoldAlpha       => "Gold (Alpha)",
        SundCol.Silver          => "Silver",
        SundCol.Light           => "Light",
        SundCol.LightAlpha      => "Light (Alpha)",
        SundCol.Dark            => "Dark",
        _ => idx.ToString(),
    };

    public static void Vec4ToClipboard(Dictionary<SundCol, Vector4> cols)
    {
        if (cols is null || cols.Count is 0)
            return;

        var sb = new StringBuilder();
        sb.AppendLine($"public static readonly Dictionary<SundCol, Vector4> TEMPLATE = new Dictionary<SundCol, Vector4>");
        sb.AppendLine("{");

        var maxEnumLen = cols.Keys.Max(k => k.ToString().Length);
        foreach (var kvp in cols.OrderBy(k => (int)k.Key))
        {
            var name = kvp.Key.ToString().PadRight(maxEnumLen);
            var v = kvp.Value;
            sb.AppendLine($"    {{ SundCol.{name}, new Vector4({v.X:0.###}f, {v.Y:0.###}f, {v.Z:0.###}f, {v.W:0.###}f) }},");
        }
        sb.AppendLine("};");

        Clipboard.SetText(sb.ToString());
    }

    public static void UintToClipboard(Dictionary<SundCol, uint> cols)
    {
        if (cols is null || cols.Count is 0)
            return;

        var sb = new StringBuilder();
        sb.AppendLine($"public static readonly IReadOnlyDictionary<SundCol, uint> TEMPLATE = new Dictionary<SundCol, uint>");
        sb.AppendLine("{");

        var maxEnumLen = cols.Keys.Max(k => k.ToString().Length);
        foreach (var kvp in cols.OrderBy(k => (int)k.Key))
            sb.AppendLine($"    {{ SundCol.{kvp.Key.ToString().PadRight(maxEnumLen)}, 0x{kvp.Value:X8} }},");
        sb.AppendLine("};");

        Clipboard.SetText(sb.ToString());
    }
}

