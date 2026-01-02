using Sundouleia.ModFiles;
using SundouleiaAPI.Data;
using TerraFX.Interop.Windows;

namespace Sundouleia.PlayerClient;

/// <summary>
///     Holds a cache of the client's current data for comparison purposes. <para />
///     This is not used over the network and designed for efficient lookups & updates.
/// </summary>
public class ClientDataCache
{
    // Mod Replacements.
    public Dictionary<string, ModdedFile> ModdedFiles   { get; set; } = new(); // Keyed by hash.
    public Dictionary<string, ModdedFile> SwappedFiles  { get; set; } = new(); // Keyed by resolvedpath.

    // Can be accessed by multiple tasks concurrently.
    public ConcurrentDictionary<OwnedObject, string> GlamourerState { get; set; } = [];
    public ConcurrentDictionary<OwnedObject, string> CPlusState { get; set; } = [];

    public string ModManips     { get; set; } = string.Empty;
    public string HeelsOffset   { get; set; } = string.Empty;
    public string Moodles       { get; set; } = string.Empty;
    public string TitleData     { get; set; } = string.Empty;
    public string PetNames      { get; set; } = string.Empty;

    public ClientDataCache()
    {
        GlamourerState = new ConcurrentDictionary<OwnedObject, string>
        {
            [OwnedObject.Player] = string.Empty,
            [OwnedObject.MinionOrMount] = string.Empty,
            [OwnedObject.Pet] = string.Empty,
            [OwnedObject.Companion] = string.Empty
        };
        CPlusState = new ConcurrentDictionary<OwnedObject, string>
        {
            [OwnedObject.Player] = string.Empty,
            [OwnedObject.MinionOrMount] = string.Empty,
            [OwnedObject.Companion] = string.Empty,
            [OwnedObject.Pet] = string.Empty
        };
    }

    public ClientDataCache(ClientDataCache other)
    {
        // Deep-copy the HashSet<ModdedFile> values.
        GlamourerState = new ConcurrentDictionary<OwnedObject, string>(other.GlamourerState);
        CPlusState = new ConcurrentDictionary<OwnedObject, string>(other.CPlusState);
        ModManips = other.ModManips;
        HeelsOffset = other.HeelsOffset;
        Moodles = other.Moodles;
        TitleData = other.TitleData;
        PetNames = other.PetNames;
    }

    public ModUpdates ToModUpdates()
        => new ModUpdates(
            ModdedFiles.Values.Select(m => m.ToFileHashDto()).ToList(),
            SwappedFiles.Values.Select(m => m.ToFileSwapDto()).ToList()
        );

    public VisualUpdate ToVisualUpdate()
    {
        return new VisualUpdate()
        {
            PlayerChanges = new IpcDataPlayerUpdate(IpcKind.Glamourer | IpcKind.Heels | IpcKind.CPlus | IpcKind.Honorific | IpcKind.Moodles | IpcKind.ModManips | IpcKind.PetNames)
            {
                GlamourState = GlamourerState[OwnedObject.Player],
                CPlusState = CPlusState[OwnedObject.Player],
                ModManips = ModManips,
                HeelsOffset = HeelsOffset,
                TitleData = TitleData,
                Moodles = Moodles,
                PetNicks = PetNames
            },
            MinionMountChanges = new IpcDataUpdate(IpcKind.Glamourer | IpcKind.CPlus)
            {
                GlamourState = GlamourerState[OwnedObject.MinionOrMount],
                CPlusState = CPlusState[OwnedObject.MinionOrMount]
            },
            PetChanges = new IpcDataUpdate(IpcKind.Glamourer | IpcKind.CPlus)
            {
                GlamourState = GlamourerState[OwnedObject.Pet],
                CPlusState = CPlusState[OwnedObject.Pet]
            },
            CompanionChanges = new IpcDataUpdate(IpcKind.Glamourer | IpcKind.CPlus)
            {
                GlamourState = GlamourerState[OwnedObject.Companion],
                CPlusState = CPlusState[OwnedObject.Companion]
            }
        };
    }

    public ModUpdates ApplyNewModState(ModdedState latestState)
    {
        var updates = ModUpdates.Empty;

        var curSwapKeys = new HashSet<string>(SwappedFiles.Keys);
        var curModKeys = new HashSet<string>(ModdedFiles.Keys);

        foreach (var file in latestState.AllFiles)
        {
            // If a file swap.
            if (file.IsFileSwap)
            {
                // Get the resolved file and remove it from the remaining keys.
                var key = file.ResolvedPath;
                curSwapKeys.Remove(key); // Removes with O(1)

                // Locate with O(1) and compare sets by O(n) over the gamepaths (always very small)
                if (SwappedFiles.TryGetValue(key, out var existing) && existing.GamePaths.SetEquals(file.GamePaths))
                    continue; // No change if both are valid.

                // Otherwise, add/update it.
                SwappedFiles[key] = file;
                updates.NewSwaps.Add(new FileSwapData(key, [.. file.GamePaths]));
            }
            // Otherwise it is a mod replacement, so handle by hash.
            else
            {
                var key = file.Hash;
                curModKeys.Remove(key); // Removes with O(1)

                // Locate with O(1) and compare sets by O(n) over the gamepaths (always very small)
                if (ModdedFiles.TryGetValue(key, out var existing) && existing.GamePaths.SetEquals(file.GamePaths))
                    continue; // No change if both are valid.

                // Otherwise, add/update it.
                ModdedFiles[key] = file;
                updates.NewReplacements.Add(new FileHashData(key, [..file.GamePaths]));
            }
        }

        // Any remaining keys in cur*Keys are to be removed.
        foreach (var hash in curModKeys)
        {
            ModdedFiles.Remove(hash, out _);
            updates.HashesToRemove.Add(hash);
        }

        foreach (var path in curSwapKeys)
        {
            SwappedFiles.Remove(path, out _);
            updates.SwapsToRemove.Add(path);
        }

        return updates;
    }

    public bool ApplySingleIpc(OwnedObject obj, IpcKind kind, string data)
    {
        if (kind.HasAny(IpcKind.Mods))
            return false;

        if (!IsDifferent(obj, kind, data))
            return false;

        // Apply the change based on the kind.
        switch (kind)
        {
            case IpcKind.Glamourer: GlamourerState[obj] = data; break;
            case IpcKind.CPlus:     CPlusState[obj] = data;     break;
            case IpcKind.ModManips: ModManips = data;           break;
            case IpcKind.Heels:     HeelsOffset = data;         break;
            case IpcKind.Honorific: TitleData = data;           break;
            case IpcKind.Moodles:   Moodles = data;             break;
            case IpcKind.PetNames:  PetNames = data;            break;
        }
        return true;
    }

    private bool IsDifferent(OwnedObject obj, IpcKind kind, string data)
        => kind switch
        {
            IpcKind.Glamourer => GlamourerState[obj] != data,
            IpcKind.CPlus => CPlusState[obj] != data,
            IpcKind.ModManips => ModManips != data,
            IpcKind.Heels => HeelsOffset != data,
            IpcKind.Honorific => TitleData != data,
            IpcKind.Moodles => Moodles != data,
            IpcKind.PetNames => PetNames != data,
            _ => false
        };

    /// <summary>
    ///     Applies all data from another cache onto this one, returning the full visual update. <para />
    ///     <b> If Manips are included, </b>
    /// </summary>
    /// <returns> The visual update reflecting all IPC-related changes for all differences. </returns>
    public VisualUpdate ApplyAllIpc(ClientDataCache other, bool forceManips)
    {
        var playerBuilder = new IpcUpdateBuilder();
        var minionMountBuilder = new IpcUpdateBuilder();
        var petBuilder = new IpcUpdateBuilder();
        var companionBuilder = new IpcUpdateBuilder();

        if ((ModManips != other.ModManips) || forceManips)
        {
            ModManips = other.ModManips;
            playerBuilder.WithManips(ModManips);
        }
        if (HeelsOffset != other.HeelsOffset)
        {
            HeelsOffset = other.HeelsOffset;
            playerBuilder.WithHeels(HeelsOffset);
        }
        if (TitleData != other.TitleData)
        {
            TitleData = other.TitleData;
            playerBuilder.WithTitle(TitleData);
        }
        if (Moodles != other.Moodles)
        {
            Moodles = other.Moodles;
            playerBuilder.WithMoodles(Moodles);
        }
        if (PetNames != other.PetNames)
        {
            PetNames = other.PetNames;
            playerBuilder.WithPetNicks(PetNames);
        }
        // Handle shared changes.
        foreach (OwnedObject obj in Enum.GetValues<OwnedObject>())
        {
            if (GlamourerState[obj] != other.GlamourerState[obj])
            {
                GlamourerState[obj] = other.GlamourerState[obj];
                switch (obj)
                {
                    case OwnedObject.Player: playerBuilder.WithGlamour(GlamourerState[obj]); break;
                    case OwnedObject.MinionOrMount: minionMountBuilder.WithGlamour(GlamourerState[obj]); break;
                    case OwnedObject.Pet: petBuilder.WithGlamour(GlamourerState[obj]); break;
                    case OwnedObject.Companion: companionBuilder.WithGlamour(GlamourerState[obj]); break;
                }
            }
            if (CPlusState[obj] != other.CPlusState[obj])
            {
                CPlusState[obj] = other.CPlusState[obj];
                switch (obj)
                {
                    case OwnedObject.Player: playerBuilder.WithCPlus(CPlusState[obj]); break;
                    case OwnedObject.MinionOrMount: minionMountBuilder.WithCPlus(CPlusState[obj]); break;
                    case OwnedObject.Pet: petBuilder.WithCPlus(CPlusState[obj]); break;
                    case OwnedObject.Companion: companionBuilder.WithCPlus(CPlusState[obj]); break;
                }
            }
        }

        return new VisualUpdate()
        {
            PlayerChanges = playerBuilder.BuildPlayer(),
            MinionMountChanges = minionMountBuilder.BuildNonPlayer(),
            PetChanges = petBuilder.BuildNonPlayer(),
            CompanionChanges = companionBuilder.BuildNonPlayer()
        };
    }
}