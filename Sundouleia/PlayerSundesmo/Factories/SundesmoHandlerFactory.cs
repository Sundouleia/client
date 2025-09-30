using Microsoft.Extensions.Hosting;
using Sundouleia.FileCache;
using Sundouleia.Interop;
using Sundouleia.Pairs.Handlers;
using Sundouleia.Services;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI.Files;
using SundouleiaAPI.Network;

namespace Sundouleia.Pairs.Factories;

public class SundesmoHandlerFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly SundouleiaMediator _mediator;
    private readonly FileCacheManager _fileCache;
    private readonly FileDownloadManager _personalDownloader;
    private readonly IpcManager _ipc;
    private readonly ServerConfigManager _configs;

    public SundesmoHandlerFactory(ILoggerFactory loggerFactory, SundouleiaMediator mediator,
        IHostApplicationLifetime lifetime, FileCacheManager fileCache, FileDownloadManager downloads,
        IpcManager ipc, ServerConfigManager configs)
    {
        _loggerFactory = loggerFactory;
        _mediator = mediator;
        _lifetime = lifetime;
        _fileCache = fileCache;
        _personalDownloader = downloads;
        _ipc = ipc;
        _configs = configs;
    }

    /// <summary> This create method in the pair handler factory will create a new pair handler object.</summary>
    /// <returns> A new PairHandler object </returns>
    public SundesmoHandler Create(Sundesmo sundesmo)
    {
        return new SundesmoHandler(_loggerFactory.CreateLogger<SundesmoHandler>(), _mediator, sundesmo,
            _lifetime, _fileCache, _personalDownloader, _ipc, _configs);
    }
}
