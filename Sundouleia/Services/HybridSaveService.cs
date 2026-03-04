using CkCommons;
using CkCommons.HybridSaver;
using Microsoft.Extensions.Hosting;

namespace Sundouleia.Services.Configs;

/// <summary> 
///     Any file type that we want to let the HybridSaveService handle
/// </summary>
public interface IHybridSavable : IHybridConfig<ConfigFileProvider>
{ }

/// <summary> 
///     Handles the Saving of enqueued services in a threadsafe manner. <para />
///     All saves are performed via secure write. <para />
/// </summary>
public sealed class HybridSaveService : HybridSaveServiceBase<ConfigFileProvider>, IHostedService
{
    private readonly ILogger<HybridSaveService> _logger;

    private HashSet<IHybridSavable> _toSaveOnDispose = [];
    public HybridSaveService(ILogger<HybridSaveService> logger, ConfigFileProvider provider)
        : base(provider)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting HybridSaveService...");
        Init();
        _logger.LogInformation("HybridSaveService started.");
        return Task.CompletedTask;
    }

    public bool MarkForSaveOnDispose(IHybridSavable savable)
        => _toSaveOnDispose.Add(savable);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping HybridSaveService...");
        _logger.LogDebug($"Savables tracked for DisposalSave:" +
            $"\n──────────────────────\n - " +
            $"{string.Join("\n - ", _toSaveOnDispose.Select(s => s.GetType().Name))}" +
            $"\n──────────────────────");
        foreach (var savable in _toSaveOnDispose)
        {
            try
            {
                Save(savable);
                _logger.LogDebug($"Enqueued [{savable.GetType().Name}] for save on disposal.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to save [{savable.GetType().Name}] on disposal:\n{ex}");
            }
        }
        await Dispose();
        _logger.LogInformation("HybridSaveService stopped.");
    }
}
