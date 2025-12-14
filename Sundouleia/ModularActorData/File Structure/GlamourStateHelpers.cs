using System.Security.Cryptography;
using System.Text.Json;

namespace Sundouleia.ModularActor;

[Flags]
public enum SMAGlamourParts : byte
{
    None           = 0 << 0,
    Customizations = 1 << 0,
    Equipment      = 1 << 1,
    Metadata       = 1 << 2,
    AdvancedDyes   = 1 << 3,
}

[Flags]
public enum SMAFileSlotFilter : short
{
    MainHand = 0 << 0,
    OffHand  = 1 << 0,
    Head     = 1 << 1,
    Body     = 1 << 2,
    Hands    = 1 << 3,
    Legs     = 1 << 4,
    Feet     = 1 << 5,
    Ears     = 1 << 6,
    Neck     = 1 << 7,
    Wrists   = 1 << 8,
    RFinger  = 1 << 9,
    LFinger  = 1 << 10,
    Bonus    = 1 << 11,
}

[Flags]
public enum SMAFileMetaFilter : byte
{
    None      = 0 << 0,
    Hat       = 1 << 0,
    VieraEars = 1 << 1,
    Visor     = 1 << 2,
    Weapon    = 1 << 3,
}

public static class GlamourStateEx
{
    private static readonly Dictionary<string, SMAFileSlotFilter> SlotFilterMap = new()
    {
        ["MainHand"] = SMAFileSlotFilter.MainHand,
        ["OffHand"]  = SMAFileSlotFilter.OffHand,
        ["Head"]     = SMAFileSlotFilter.Head,
        ["Body"]     = SMAFileSlotFilter.Body,
        ["Hands"]    = SMAFileSlotFilter.Hands,
        ["Legs"]     = SMAFileSlotFilter.Legs,
        ["Feet"]     = SMAFileSlotFilter.Feet,
        ["Ears"]     = SMAFileSlotFilter.Ears,
        ["Neck"]     = SMAFileSlotFilter.Neck,
        ["Wrists"]   = SMAFileSlotFilter.Wrists,
        ["RFinger"]  = SMAFileSlotFilter.RFinger,
        ["LFinger"]  = SMAFileSlotFilter.LFinger,
    };

    private static readonly Dictionary<string, SMAFileMetaFilter> MetaFilterMap = new()
    {
        ["Hat"]       = SMAFileMetaFilter.Hat,
        ["VieraEars"] = SMAFileMetaFilter.VieraEars,
        ["Visor"]     = SMAFileMetaFilter.Visor,
        ["Weapon"]    = SMAFileMetaFilter.Weapon,
    };

    public static bool HasAny(this SMAGlamourParts flags, SMAGlamourParts check) => (flags & check) != 0;
    public static bool HasAny(this SMAFileSlotFilter flags, SMAFileSlotFilter check) => (flags & check) != 0;
    public static bool HasAny(this SMAFileMetaFilter flags, SMAFileMetaFilter check) => (flags & check) != 0;

    /// <summary>
    ///     Compiles a new JObject containing only the filtered data.
    /// </summary>
    public static void FilterEquipment(this JObject root, SMAFileSlotFilter slotFilter, SMAFileMetaFilter metaFilter)
    {
        var result = new JObject();

        if (root["Equipment"] is not JObject equipment)
            return;

        // Slots
        foreach (var (name, token) in equipment)
        {
            if (SlotFilterMap.TryGetValue(name, out var slot))
            {
                if (slotFilter.HasAny(slot))
                    result[name] = token!.DeepClone();
            }
            else if (MetaFilterMap.TryGetValue(name, out var meta))
            {
                if (metaFilter.HasAny(meta))
                    result[name] = token!.DeepClone();
            }
        }

        return;
    }



}