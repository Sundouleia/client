using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI.Files.Models;

namespace Sundouleia.WebAPI.Files;

// Can probably blend this or something idk.
public class FileTransferService : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;

    // Concurrent dictionary to handle asynchronous file downloading between multiple characters at the same time (most likely).
    private readonly ConcurrentDictionary<Guid, bool> _downloadReady = new();
    // Client being used to establish contact with the static FTP server.
    private readonly HttpClient _httpClient;

    // Download semaphore attributes.
    private readonly object _semaphoreModificationLock = new();
    private int _availableDownloadSlots;
    private SemaphoreSlim _downloadSemaphore;

    // How many downloads are in use (how many characters are we downloading from at once)
    // [**Think how players loaded in at venues.. lol]
    private int CurrentlyUsedDownloadSlots => _availableDownloadSlots - _downloadSemaphore.CurrentCount;

    public FileTransferService(ILogger<FileTransferService> logger, SundouleiaMediator mediator,
        MainConfig config, HttpClient httpClient)
        : base(logger, mediator)
    {
        _config = config;
        _httpClient = httpClient;

        // (if there is a version miss-match with the server, this would fail).
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Sundeouleia", $"{ver!.Major}.{ver!.Minor}.{ver!.Build}"));

        // Mark the maximum download slots for the configured parallel download slots.
        _availableDownloadSlots = config.Current.MaxParallelDownloads;
        // mark the download semaphore to have the number of concurrent requests equal to the _availableDownloadSlots.
        _downloadSemaphore = new(_availableDownloadSlots, _availableDownloadSlots);
    }

    public void ReleaseDownloadSlot()
    {
        try
        {
            _downloadSemaphore.Release();
            Mediator.Publish(new DownloadLimitChangedMessage());
        }
        catch (SemaphoreFullException) { /* Bagagwa */ }
    }

    /// <summary>
    ///     Send off a request with no content, but an optional token, with a completion option built in.
    /// </summary>
    public async Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, Uri uri,
        CancellationToken? ct = null, HttpCompletionOption endOption = HttpCompletionOption.ResponseContentRead)
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        return await SendRequestInternalAsync(requestMessage, ct, endOption).ConfigureAwait(false);
    }

    /// <summary>
    ///     process a request stream using progressable stream content.
    /// </summary>
    public async Task<HttpResponseMessage> SendRequestStreamAsync(HttpMethod method, Uri uri, ProgressableStreamContent content, CancellationToken ct)
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        requestMessage.Content = content;
        return await SendRequestInternalAsync(requestMessage, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     process a request stream using progressable stream content.
    /// </summary>
    public async Task<HttpResponseMessage> SendRequestCompressedStreamAsync(HttpMethod method, Uri uri, ProgressableStreamContent content, long? uncompressedSize, CancellationToken ct)
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        requestMessage.Headers.Add("X-File-Compression", "zstd");
        if (uncompressedSize != null)
            requestMessage.Headers.Add("X-File-Size", uncompressedSize.ToString());
        requestMessage.Content = content;
        return await SendRequestInternalAsync(requestMessage, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     awaits for a download slot to become available for us to start processing the download task.
    /// </summary>
    public async Task WaitForDownloadSlotAsync(CancellationToken ct)
    {
        // the parallel download slots have changed in the config and the available download slots are the same as the current semaphore count.
        // This is likely to help recalculate the download semaphore every time a download slot is waited on, but feels like a really wierd way to handle it.
        lock (_semaphoreModificationLock)
        {
            if (_availableDownloadSlots != _config.Current.MaxParallelDownloads && _availableDownloadSlots == _downloadSemaphore.CurrentCount)
            {
                _availableDownloadSlots = _config.Current.MaxParallelDownloads;
                _downloadSemaphore = new(_availableDownloadSlots, _availableDownloadSlots);
            }
        }

        // await for the semaphore to have an available slot, with a token for cancellation.
        await _downloadSemaphore.WaitAsync(ct).ConfigureAwait(false);
        Mediator.Publish(new DownloadLimitChangedMessage());
    }

    /// <summary>
    ///     Obtain the current download limit per slot, based on the configured max slots and download speed limit.
    /// </summary>
    public long DownloadLimitPerSlot()
    {
        var limit = _config.Current.DownloadLimitBytes;
        if (limit <= 0) return 0;
        limit = _config.Current.DownloadSpeedType switch
        {
            DownloadSpeeds.Bps => limit,
            DownloadSpeeds.KBps => limit * 1024,
            DownloadSpeeds.MBps => limit * 1024 * 1024,
            _ => limit,
        };
        var currentUsedDlSlots = CurrentlyUsedDownloadSlots;
        var available = _availableDownloadSlots;
        var currentCount = _downloadSemaphore.CurrentCount;
        var dividedLimit = limit / (currentUsedDlSlots == 0 ? 1 : currentUsedDlSlots);
        if (dividedLimit < 0)
        {
            Logger.LogWarning($"Calculated Bandwidth Limit is negative, returning Infinity: {dividedLimit}, " +
                $"CurrentlyUsedDownloadSlots is {currentUsedDlSlots}, DownloadSpeedLimit is {limit}, available slots: {available}, " +
                $"current count: {currentCount}");
            return long.MaxValue;
        }
        return Math.Clamp(dividedLimit, 1, long.MaxValue);
    }

    /// <summary>
    ///     Internally send a request off to the FTP servers
    /// </summary>
    private async Task<HttpResponseMessage> SendRequestInternalAsync(HttpRequestMessage reqMsg, CancellationToken? ct = null, HttpCompletionOption endOption = HttpCompletionOption.ResponseContentRead)
    {
        // if the request message content has content already, and it is not stream content, or byte array content, convert it to JsonContent we read as a string.
        if (reqMsg.Content != null && reqMsg.Content is not StreamContent && reqMsg.Content is not ByteArrayContent)
        {
            // read the content, converted to JsonContent, as a string. Await the result for the response.
            var content = await ((JsonContent)reqMsg.Content).ReadAsStringAsync().ConfigureAwait(false);
            Logger.LogDebug($"Sending {reqMsg.Method} to {reqMsg.RequestUri} (Content: {content})", LoggerType.FileService);
        }
        else
        {
            // at least log what method we are sending off to the URI thingy.
            Logger.LogDebug($"Sending {reqMsg.Method} to {reqMsg.RequestUri}", LoggerType.FileService);
        }

        try
        {
            // if we have a cancel token for this request, attach its value to the http request.
            if (ct != null)
                return await _httpClient.SendAsync(reqMsg, endOption, ct.Value).ConfigureAwait(false);
            // if the token is not valid, send off the request to the http client with no 
            return await _httpClient.SendAsync(reqMsg, endOption).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            // big bad oopsie, throw can crash game, but it should?
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Error during SendRequestInternal for {reqMsg.RequestUri}: {ex}");
            throw;
        }
        finally
        {
            Logger.LogDebug($"Request end for {reqMsg.Method} to {reqMsg.RequestUri}", LoggerType.FileService);
        }
    }
}