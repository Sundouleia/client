using CkCommons;
using Dalamud.Plugin.Ipc;
using MemoryPack;
using Sundouleia.Loci.Data;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Interop;

[Serializable]
[MemoryPackable]
public partial class MyStatus
{
    public Guid GUID;
    public int IconID;
    public string Title;
    public string Description;
    public string CustomFXPath;
    public long ExpiresAt;

    public StatusType Type; // Int
    public Modifiers Modifiers; // UInt

    public int Stacks;
    public int StackSteps;

    public Guid ChainedStatus;
    public ChainTrigger ChainTrigger; // Int

    public string Applier;
    public string Dispeller;
}

public static class LegacyMoodlesEx
{
    public static readonly MemoryPackSerializerOptions SerializerOptions = new()
    {
        StringEncoding = StringEncoding.Utf16,
    };

    public static MyStatus ToLegacyStatus(this LociStatus s)
        => new MyStatus
        {
            GUID = s.GUID,
            IconID = s.IconID,
            Title = s.Title,
            Description = s.Description,
            CustomFXPath = s.CustomFXPath,
            ExpiresAt = s.ExpiresAt,
            Type = s.Type,
            Modifiers = s.Modifiers,
            Stacks = s.Stacks,
            StackSteps = s.StackSteps,
            ChainedStatus = s.ChainedType is ChainType.Status ? s.ChainedGUID : Guid.Empty,
            ChainTrigger = s.ChainTrigger,
            Applier = s.Applier,
            Dispeller = s.Dispeller
        };

    public static LociStatus FromLegacyStatus(this MyStatus p)
        => new LociStatus
        {
            GUID = p.GUID,
            IconID = p.IconID,
            Title = p.Title,
            Description = p.Description,
            CustomFXPath = p.CustomFXPath,
            ExpiresAt = p.ExpiresAt,
            Type = p.Type,
            Modifiers = p.Modifiers,
            Stacks = p.Stacks,
            StackSteps = p.StackSteps,
            ChainedGUID = p.ChainedStatus,
            ChainedType = ChainType.Status,
            ChainTrigger = p.ChainTrigger,
            Applier = p.Applier,
            Dispeller = p.Dispeller
        };

    public static LociStatusInfo FromLegacyTuple(this MoodlesStatusInfo t)
        => new LociStatusInfo
        {
            GUID = t.GUID,
            IconID = t.IconID,
            Title = t.Title,
            Description = t.Description,
            CustomVFXPath = t.CustomVFXPath,
            ExpireTicks = t.ExpireTicks,
            Type = t.Type,
            Modifiers = t.Modifiers,
            Stacks = t.Stacks,
            StackSteps = t.StackSteps,
            ChainedGUID = t.ChainedStatus,
            ChainType = ChainType.Status,
            ChainTrigger = t.ChainTrigger
        };

    public static MoodlesStatusInfo ToLegacyTuple(this LociStatusInfo s)
        => new MoodlesStatusInfo
        {
            GUID = s.GUID,
            IconID = s.IconID,
            Title = s.Title,
            Description = s.Description,
            CustomVFXPath = s.CustomVFXPath,
            ExpireTicks = s.ExpireTicks,
            Type = s.Type,
            Modifiers = s.Modifiers,
            Stacks = s.Stacks,
            StackSteps = s.StackSteps,
            ChainedStatus = s.ChainedGUID,
            ChainTrigger = s.ChainTrigger
        };

    public static LociPresetInfo FromLegacyPreset(this MoodlePresetInfo t)
        => new LociPresetInfo
        {
            GUID = t.GUID,
            Statuses = t.Statuses,
            ApplicationType = t.ApplicationType,
            Title = t.Title,
            Description = string.Empty,
        };

    public static MoodlePresetInfo ToLegacyPreset(this LociPresetInfo s)
        => new MoodlePresetInfo
        {
            GUID = s.GUID,
            Statuses = s.Statuses,
            ApplicationType = s.ApplicationType,
            Title = s.Title,
        };
}
