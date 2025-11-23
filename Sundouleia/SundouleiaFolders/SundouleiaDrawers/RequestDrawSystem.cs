using CkCommons.HybridSaver;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;

namespace Sundouleia.DrawSystem;

public sealed class RequestsDrawSystem : DynamicDrawSystem<RequestEntry>, IMediatorSubscriber, IDisposable, IHybridSavable
{
    private readonly ILogger<RadarDrawSystem> _logger;
    private readonly RequestsManager _manager;
    private readonly HybridSaveService _hybridSaver;

    public SundouleiaMediator Mediator { get; }

    public RequestsDrawSystem(ILogger<RadarDrawSystem> logger, SundouleiaMediator mediator,
        RequestsManager manager, HybridSaveService saver)
    {
        _logger = logger;
        Mediator = mediator;
        _manager = manager;
        _hybridSaver = saver;

        // Load the hierarchy and initialize the folders.
        LoadData();

        Mediator.Subscribe<FolderUpdateRequests>(this, _ => UpdateFolders());

        Changed += OnChange;
    }

    public void Dispose()
    {
        Mediator.UnsubscribeAll(this);
        Changed -= OnChange;
    }

    // Note that this will change very soon, as saves should only occur for certain changes.
    private void OnChange(DDSChangeType type, IDynamicNode<RequestEntry> obj, IDynamicCollection<RequestEntry>? prevParent, IDynamicCollection<RequestEntry>? newParent)
    {
        if (type != DDSChangeType.Reload)
        {
            _logger.LogInformation($"DDS Change [{type}] for node [{obj.Name} ({obj.FullPath})] occured. Saving Config.");
            _hybridSaver.Save(this);
        }
    }

    private void LoadData()
    {
        // If any changes occured, re-save the file.
        if (LoadFile(new FileInfo(_hybridSaver.FileNames.DDS_Requests)))
        {
            _logger.LogInformation("RequestsDrawSystem folder structure changed on load, saving updated structure.");
            _hybridSaver.Save(this);
        }
    }

    protected override bool EnsureAllFolders(Dictionary<string, string> folderMap)
    {
        return false;
    }

    // HybridSavable
    public int ConfigVersion => 0;
    public HybridSaveType SaveType => HybridSaveType.StreamWrite;
    public DateTime LastWriteTimeUTC { get; private set; } = DateTime.MinValue;
    public string GetFileName(ConfigFileProvider files, out bool isAccountUnique)
        => (isAccountUnique = false, files.DDS_Requests).Item2;

    public string JsonSerialize() 
        => throw new NotImplementedException();

    public void WriteToStream(StreamWriter writer) 
        => SaveToFile(writer);
}

