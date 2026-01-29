using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;
using Sundouleia.WebAPI.Files;

namespace Sundouleia.Pairs.Factories;

public class SundesmoHandlerFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly SundouleiaMediator _mediator;
    private readonly AccountConfig _account;
    private readonly FileDownloader _downloader;
    private readonly FileCacheManager _fileCache;
    private readonly IpcManager _ipc;
    private readonly CharaObjectWatcher _watcher;

    public SundesmoHandlerFactory(ILoggerFactory loggerFactory, SundouleiaMediator mediator,
        AccountConfig account, FileCacheManager fileCache, FileDownloader downloads, 
        IpcManager ipc, CharaObjectWatcher watcher)
    {
        _loggerFactory = loggerFactory;
        _mediator = mediator;
        _account = account;
        _fileCache = fileCache;
        _downloader = downloads;
        _ipc = ipc;
        _watcher = watcher;
    }

    /// <summary>
    ///     This create method in the pair handler factory will create a new pair handler object.
    /// </summary>
    public PlayerHandler Create(Sundesmo sundesmo)
        => new(sundesmo, _loggerFactory.CreateLogger<PlayerHandler>(), _mediator, _account, _fileCache, _downloader, _watcher, _ipc);

    /// <summary>
    ///     This create method in the pair handler factory will create a new owned object handler.
    /// </summary>
    public PlayerOwnedHandler Create(OwnedObject type, Sundesmo sundesmo)
        => new(type, sundesmo, _loggerFactory.CreateLogger<PlayerOwnedHandler>(), _mediator, _account, _ipc, _watcher);
}
