using LociApi.Enums;
using SundouleiaAPI.Data;

namespace Sundouleia;

// Help bridge the gap between LociAPI and SundouleiaAPI
public static class LociHelpers
{
    public static LociStatusStruct ToStruct(this LociStatusInfo info)
        => new()
        {
            Version = info.Version,
            GUID = info.GUID,
            IconID = info.IconID,
            Title = info.Title,
            Description = info.Description,
            CustomVFXPath = info.CustomVFXPath,
            ExpireTicks = info.ExpireTicks,
            Type = (byte)info.Type,
            Stacks = info.Stacks,
            StackSteps = info.StackSteps,
            StackToChain = info.StackToChain,
            Modifiers = info.Modifiers,
            ChainedGUID = info.ChainedGUID,
            ChainType = (byte)info.ChainType,
            ChainTrigger = (int)info.ChainTrigger,
            Applier = info.Applier,
            Dispeller = info.Dispeller
        };

    public static LociStatusInfo ToTuple(this LociStatusStruct statStruct)
        => (statStruct.Version,
            statStruct.GUID,
            statStruct.IconID,
            statStruct.Title,
            statStruct.Description,
            statStruct.CustomVFXPath,
            statStruct.ExpireTicks,
            (StatusType)statStruct.Type,
            statStruct.Stacks,
            statStruct.StackSteps,
            statStruct.StackToChain,
            statStruct.Modifiers,
            statStruct.ChainedGUID,
            (ChainType)statStruct.ChainType,
            (ChainTrigger)statStruct.ChainTrigger,
            statStruct.Applier,
            statStruct.Dispeller);

    public static LociPresetStruct ToStruct(this LociPresetInfo info)
        => new()
        {
            Version = info.Version,
            GUID = info.GUID,
            Statuses = info.Statuses,
            ApplicationType = info.ApplicationType,
            Title = info.Title,
            Description = info.Description
        };

    public static LociPresetInfo ToTuple(this LociPresetStruct presetStruct)
        => (presetStruct.Version,
            presetStruct.GUID,
            presetStruct.Statuses,
            presetStruct.ApplicationType,
            presetStruct.Title,
            presetStruct.Description);
}
