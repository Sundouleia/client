using CkCommons;
using CkCommons.HybridSaver;
using Dalamud.Bindings.ImGui;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;

namespace Sundouleia.DrawSystem;
public sealed class WhitelistFolder : DynamicFolder<Sundesmo>
{
    private Func<IReadOnlyList<Sundesmo>> _generator;
    public WhitelistFolder(DynamicFolderGroup<Sundesmo> parent, uint id, FAI icon, string name,
        uint iconColor, Func<IReadOnlyList<Sundesmo>> generator)
        : base(parent, icon, name, id)
    {
        // Can set stylizations here.
        NameColor = uint.MaxValue;
        IconColor = iconColor;
        BgColor = uint.MinValue;
        BorderColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        _generator = generator;
    }

    public WhitelistFolder(DynamicFolderGroup<Sundesmo> parent, uint id, FAI icon, string name,
        uint iconColor, Func<IReadOnlyList<Sundesmo>> generator, IReadOnlyList<ISortMethod<DynamicLeaf<Sundesmo>>> sortSteps)
        : base(parent, icon, name, id, new(sortSteps))
    {
        // Can set stylizations here.
        NameColor = uint.MaxValue;
        IconColor = iconColor;
        BgColor = uint.MinValue;
        BorderColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        _generator = generator;
    }

    public int Rendered => Children.Count(s => s.Data.IsRendered);
    public int Online => Children.Count(s => s.Data.IsOnline);
    protected override IReadOnlyList<Sundesmo> GetAllItems() => _generator();
    protected override DynamicLeaf<Sundesmo> ToLeaf(Sundesmo item) => new(this, item.UserData.UID, item);

    // Maybe replace with something better later. Would be nice to not depend on multiple generators but idk.
    public string BracketText => Name switch
    {
        Constants.FolderTagAll => $"[{TotalChildren}]",
        Constants.FolderTagVisible => $"[{Rendered}]",
        Constants.FolderTagOnline => $"[{Online}]",
        Constants.FolderTagOffline => $"[{TotalChildren}]",
        _ => string.Empty,
    };

    public string BracketTooltip => Name switch
    {
        Constants.FolderTagAll => $"{TotalChildren} total",
        Constants.FolderTagVisible => $"{Rendered} visible",
        Constants.FolderTagOnline => $"{Online} online",
        Constants.FolderTagOffline => $"{TotalChildren} offline",
        _ => string.Empty,
    };
}

public class WhitelistDrawSystem : DynamicDrawSystem<Sundesmo>, IMediatorSubscriber, IDisposable, IHybridSavable
{
    private readonly ILogger<WhitelistDrawSystem> _logger;
    private readonly FolderConfig _folderConfig;
    private readonly SundesmoManager _sundesmos;
    private readonly HybridSaveService _hybridSaver;

    public SundouleiaMediator Mediator { get; init; }

    public WhitelistDrawSystem(ILogger<WhitelistDrawSystem> logger, SundouleiaMediator mediator,
        FolderConfig config, SundesmoManager sundesmos, HybridSaveService saver)
    {
        _logger = logger;
        Mediator = mediator;
        _folderConfig = config;
        _sundesmos = sundesmos;
        _hybridSaver = saver;

        // Load the hierarchy and initialize the folders.
        LoadData();

        Mediator.Subscribe<FolderUpdateSundesmos>(this, _ => UpdateFolders());

        // On change notifications, we should save the config.
        // Note that this will change very soon, as saves should only occur for certain changes.
        Changed += OnChange;
    }

    public void Dispose()
    {
        Mediator.UnsubscribeAll(this);
        Changed -= OnChange;
    }

    // Note that this will change very soon, as saves should only occur for certain changes.
    private void OnChange(DDSChangeType type, IDynamicNode<Sundesmo> obj, IDynamicCollection<Sundesmo>? prevParent, IDynamicCollection<Sundesmo>? newParent)
    {
        if (type != DDSChangeType.Reload)
        {
            _logger.LogInformation($"DDS Change [{type}] for node [{obj.Name} ({obj.FullPath})] occured. Saving Config.");
            _hybridSaver.Save(this);
        }
    }

    private void LoadData()
    {
        // Before we load anything, inverse the sort direction of root.
        SetSortDirection(root, true);
        // If any changes occured, re-save the file.
        if (LoadFile(new FileInfo(_hybridSaver.FileNames.DDS_Whitelist)))
        {
            _logger.LogInformation("WhitelistDrawSystem folder structure changed on load, saving updated structure.");
            _hybridSaver.Save(this);
        }
    }

    protected override bool EnsureAllFolders(Dictionary<string, string> _)
    {
        // Load in the folders, they are all descendants of root.
        bool anyChanged = false;
        anyChanged |= UpdateVisibleFolderState(_folderConfig.Current.VisibleFolder);
        anyChanged |= UpdateOfflineFolderState(_folderConfig.Current.OfflineFolder);
        _logger.LogInformation($"Ensured all folders, total now {FolderMap.Count} folders.");
        return anyChanged;
    }

    // Update the FolderSystem folders based on if it should be included or not.
    public bool UpdateVisibleFolderState(bool showFolder)
    {
        // If we want to show the folder and it already exists then change nothing.
        if (showFolder)
        {
            if (FolderMap.ContainsKey(Constants.FolderTagVisible))
                return false;
            // Try to add it.
            return TryAdd(FAI.Eye, Constants.FolderTagVisible, CkColor.TriStateCheck.Uint(), () => [.. _sundesmos.DirectPairs.Where(u => u.IsRendered && u.IsOnline)]);
        }
        // Otherwise attempt to remove it.
        return Delete(Constants.FolderTagVisible);
    }

    // Not too worried about additional work here since it only happens on recalculations.
    public bool UpdateOfflineFolderState(bool showFolder)
    {
        // Assume no changes.
        bool anyChanges = false;
        // If we wanted to show offline/online..
        if (showFolder)
        {
            anyChanges |= Delete(Constants.FolderTagAll);
            anyChanges |= TryAdd(FAI.Link, Constants.FolderTagOnline, CkColor.TriStateCheck.Uint(), () => [.. _sundesmos.DirectPairs.Where(s => s.IsOnline)]);
            anyChanges |= TryAdd(FAI.Link, Constants.FolderTagOffline, CkColor.TriStateCross.Uint(), () => [.. _sundesmos.DirectPairs.Where(s => !s.IsOnline)]);
        }
        // Otherwise we wanted to only show ALL.
        else
        {
            anyChanges |= Delete(Constants.FolderTagOnline);
            anyChanges |= Delete(Constants.FolderTagOffline);
            anyChanges |= AddFolder(new WhitelistFolder(root, idCounter + 1u, FAI.Globe, Constants.FolderTagAll, 
                                        uint.MaxValue, () => _sundesmos.DirectPairs, DynamicSorterEx.AllFolderSorter));
        }
        // Return if anything was modified.
        return anyChanges;
    }

    private bool TryAdd(FAI icon, string name, uint iconColor, Func<IReadOnlyList<Sundesmo>> generator)
        => AddFolder(new WhitelistFolder(root, idCounter + 1u, icon, name, iconColor, generator, [DynamicSorterEx.ByPairName]));

    // HybridSavable
    public int ConfigVersion => 0;
    public HybridSaveType SaveType => HybridSaveType.StreamWrite;
    public DateTime LastWriteTimeUTC { get; private set; } = DateTime.MinValue;
    public string GetFileName(ConfigFileProvider files, out bool isAccountUnique)
        => (isAccountUnique = false, files.DDS_Whitelist).Item2;

    public string JsonSerialize() 
        => throw new NotImplementedException();

    public void WriteToStream(StreamWriter writer) 
        => SaveToFile(writer);
}

public static class DynamicSorterEx
{
    public static ISortMethod<IDynamicCollection<T>> ByFolderName<T>() where T : class
        => new FolderName<T>();
    public static ISortMethod<IDynamicCollection<T>> ByTotalChildren<T>() where T : class
        => new TotalChildren<T>();

    // Used here as Sundesmo is shared commonly across multiple draw systems.
    public static readonly ISortMethod<DynamicLeaf<Sundesmo>> ByRendered = new Rendered();
    public static readonly ISortMethod<DynamicLeaf<Sundesmo>> ByOnline = new Online();
    public static readonly ISortMethod<DynamicLeaf<Sundesmo>> ByFavorite = new Favorite();
    public static readonly ISortMethod<DynamicLeaf<Sundesmo>> ByPairName = new PairName();
    public static readonly ISortMethod<DynamicLeaf<Sundesmo>> ByTemporary = new Temporary();
    public static readonly ISortMethod<DynamicLeaf<Sundesmo>> ByDateAdded = new DateAdded();

    // Converters
    public static ISortMethod<DynamicLeaf<Sundesmo>> ToSortMethod(this FolderSortFilter filter)
        => filter switch
        {
            FolderSortFilter.Rendered => ByRendered,
            FolderSortFilter.Online => ByOnline,
            FolderSortFilter.Favorite => ByFavorite,
            FolderSortFilter.Alphabetical => ByPairName,
            FolderSortFilter.Temporary => ByTemporary,
            FolderSortFilter.DateAdded => ByDateAdded,
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null)
        };

    /// <summary>
    ///     Preset for the AllFolder, to sort by name -> visible -> online -> favorite.
    /// </summary>
    public static readonly IReadOnlyList<ISortMethod<DynamicLeaf<Sundesmo>>> AllFolderSorter
        = [ ByRendered, ByOnline, ByFavorite, ByPairName ];

    // Sort Helpers
    public struct TotalChildren<T> : ISortMethod<IDynamicCollection<T>> where T : class
    {
        public string Name => "Total Count";
        public FAI Icon => FAI.SortNumericDown; // Maybe change.
        public string Tooltip => "Sort by number of items in the folder.";
        public Func<IDynamicCollection<T>, IComparable?> KeySelector => c => c.TotalChildren;
    }

    public struct FolderName<T> : ISortMethod<IDynamicCollection<T>> where T : class
    {
        public string Name => "Name";
        public FAI Icon => FAI.SortAlphaDown; // Maybe change.
        public string Tooltip => "Sort by name.";
        public Func<IDynamicCollection<T>, IComparable?> KeySelector => c => c.Name;
    }

    public struct Rendered : ISortMethod<DynamicLeaf<Sundesmo>>
    {
        public string Name => "Rendered";
        public FAI Icon => FAI.Eye; // Maybe change.
        public string Tooltip => "Sort by rendered status.";
        public Func<DynamicLeaf<Sundesmo>, IComparable?> KeySelector => l => l.Data.IsRendered ? 0 : 1;
    }

    public struct Online : ISortMethod<DynamicLeaf<Sundesmo>>
    {
        public string Name => "Online";
        public FAI Icon => FAI.Wifi; // Maybe change.
        public string Tooltip => "Sort by online status.";
        public Func<DynamicLeaf<Sundesmo>, IComparable?> KeySelector => l => l.Data.IsOnline ? 0 : 1;
    }

    public struct Favorite : ISortMethod<DynamicLeaf<Sundesmo>>
    {
        public string Name => "Favorite";
        public FAI Icon => FAI.Star; // Maybe change.
        public string Tooltip => "Sort by favorite status.";
        public Func<DynamicLeaf<Sundesmo>, IComparable?> KeySelector => l => l.Data.IsFavorite ? 0 : 1;
    }

    public struct Temporary : ISortMethod<DynamicLeaf<Sundesmo>>
    {
        public string Name => "Temporary";
        public FAI Icon => FAI.Clock; // Maybe change.
        public string Tooltip => "Sort temporary pairs.";
        public Func<DynamicLeaf<Sundesmo>, IComparable?> KeySelector => l => l.Data.IsTemporary ? 0 : 1;
    }

    public struct DateAdded : ISortMethod<DynamicLeaf<Sundesmo>>
    {
        public string Name => "Date Added";
        public FAI Icon => FAI.Calendar; // Maybe change.
        public string Tooltip => "Sort by date added.";
        public Func<DynamicLeaf<Sundesmo>, IComparable?> KeySelector => l => l.Data.UserPair.CreatedAt;
    }

    public struct PairName : ISortMethod<DynamicLeaf<Sundesmo>>
    {
        public string Name => "Name";
        public FAI Icon => FAI.SortAlphaDown; // Maybe change.
        public string Tooltip => "Sort by name.";
        public Func<DynamicLeaf<Sundesmo>, IComparable?> KeySelector => l => l.Data.AlphabeticalSortKey();
    }

    public struct RadarName : ISortMethod<DynamicLeaf<RadarUser>>
    {
        public string Name => "Name";
        public FAI Icon => FAI.SortAlphaDown; // Maybe change.
        public string Tooltip => "Sort by name.";
        public Func<DynamicLeaf<RadarUser>, IComparable?> KeySelector => l => l.Data.PlayerName;
    }

    public struct ByRequestTime : ISortMethod<DynamicLeaf<RequestEntry>>
    {
        public string Name => "Request Time";
        public FAI Icon => FAI.Stopwatch;
        public string Tooltip => "Sort by request time.";
        public Func<DynamicLeaf<RequestEntry>, IComparable?> KeySelector => l => l.Data.ExpireTime;
    }
}

