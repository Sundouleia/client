using CkCommons;
using CkCommons.HybridSaver;
using Dalamud.Bindings.ImGui;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
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
        if (LoadFile(new FileInfo(_hybridSaver.FileNames.DDS_Whitelist), out _, out List<string> openedCollections))
        {
            _logger.LogDebug("Loaded WhitelistDrawSystem from file.");
            // Load in the folders (we dont care about the parent state, they are all root here)
            VisibleFolderStateUpdate(_folderConfig.Current.VisibleFolder);
            OfflineFolderStateUpdate(_folderConfig.Current.OfflineFolder);
            // Now process OpenedState via the OpenedCollections. (could do this in the above method or not, idk. Maybe best to do seperate)
        }
        else
            _logger.LogDebug("No saved WhitelistDrawSystem file found, starting fresh.");
    }

    // Update the FolderSystem folders based on if it should be included or not.
    public void VisibleFolderStateUpdate(bool showFolder)
    {
        if (showFolder)
            CreateFolder(FAI.Eye, Constants.FolderTagVisible, CkColor.TriStateCheck.Uint(), () => [.. _sundesmos.DirectPairs.Where(u => u.IsRendered && u.IsOnline)]);
        else
            Delete(Constants.FolderTagVisible);
    }

    public void OfflineFolderStateUpdate(bool showFolder)
    {
        if (showFolder)
        {
            // Remove the AllFolder, if it was present.
            Delete(Constants.FolderTagAll);
            // Then add online and offline.
            CreateFolder(FAI.Link, Constants.FolderTagOnline, CkColor.TriStateCheck.Uint(), () => [.. _sundesmos.DirectPairs.Where(s => s.IsOnline)]);
            CreateFolder(FAI.Link, Constants.FolderTagOffline, CkColor.TriStateCross.Uint(), () => [.. _sundesmos.DirectPairs.Where(s => !s.IsOnline)]);
        }
        else
        {
            // Delete Online/Offline, then add All.
            Delete(Constants.FolderTagOnline);
            Delete(Constants.FolderTagOffline);
            CreateFolder(FAI.Globe, Constants.FolderTagAll, uint.MaxValue, () => _sundesmos.DirectPairs);
        }
    }

    private void CreateFolder(FAI icon, string name, uint iconColor, Func<IReadOnlyList<Sundesmo>> generator)
        => AddFolder(new WhitelistFolder(root, idCounter + 1u, icon, name, iconColor, generator));

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

