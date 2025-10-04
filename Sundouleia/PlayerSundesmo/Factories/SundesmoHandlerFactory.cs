using Microsoft.Extensions.Hosting;
using Sundouleia.ModFiles;
using Sundouleia.Interop;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI.Files;
using Sundouleia.Services;

namespace Sundouleia.Pairs.Factories;

public class SundesmoHandlerFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly SundouleiaMediator _mediator;
    private readonly FileDownloader _downloader;
    private readonly FileCacheManager _fileCache;
    private readonly IpcManager _ipc;
    private readonly ServerConfigManager _configs;
    private readonly CharaObjectWatcher _watcher;

    public SundesmoHandlerFactory(ILoggerFactory loggerFactory, SundouleiaMediator mediator,
        IHostApplicationLifetime lifetime, FileCacheManager fileCache, FileDownloader downloads,
        IpcManager ipc, ServerConfigManager configs, CharaObjectWatcher watcher)
    {
        _loggerFactory = loggerFactory;
        _mediator = mediator;
        _lifetime = lifetime;
        _fileCache = fileCache;
        _downloader = downloads;
        _ipc = ipc;
        _configs = configs;
        _watcher = watcher;
    }

    /// <summary>
    ///     This create method in the pair handler factory will create a new pair handler object.
    /// </summary>
    public PlayerHandler Create(Sundesmo sundesmo)
        => new(sundesmo, _loggerFactory.CreateLogger<PlayerHandler>(), _mediator, _lifetime, _fileCache, _downloader, _ipc);

    /// <summary>
    ///     This create method in the pair handler factory will create a new owned object handler.
    /// </summary>
    public PlayerOwnedHandler Create(OwnedObject type, Sundesmo sundesmo)
        => new(type, sundesmo, _loggerFactory.CreateLogger<PlayerOwnedHandler>(), _mediator, _lifetime, _ipc);
}
