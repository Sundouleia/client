using Sundouleia.ModFiles;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI.Files.Models;
using SundouleiaAPI.Data;

namespace Sundouleia.WebAPI.Files;

public sealed class FileUploader : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly FileCacheManager _fileDbManager;
    private readonly FileTransferService _transferService;

    public FileUploader(ILogger<FileUploader> logger, SundouleiaMediator mediator,
        MainConfig config, FileCacheManager fileDbManager, FileTransferService fileTransferService) : base(logger, mediator)
    {
        _config = config;
        _fileDbManager = fileDbManager;
        _transferService = fileTransferService;
    }

    public ConcurrentDictionary<string, FileTransferProgress> CurrentUploads { get; } = []; // update as time does on.
    public bool IsUploading => CurrentUploads.Count > 0;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }

    // Uploads all necessary files via their authorized upload links to the server.
    public async Task UploadFiles(IEnumerable<UploadableFile> filesToUpload) // Don't need to include the visible players here just yet.
    {
        foreach (var file in filesToUpload)
        {
            if (CurrentUploads.GetOrAdd(file.Verified.Hash, new FileTransferProgress(0, file.Size)) is not null)
            {
                Logger.LogWarning("File {FileName} is already being uploaded, skipping.", file.Verified.Hash);
                continue;
            }

            try
            {
                await UploadFile(file, CancellationToken.None).ConfigureAwait(false);

                Logger.LogDebug("Successfully uploaded file {FileName}.", file.Verified.Hash);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error uploading file {FileName}.", file.Verified.Hash);
            }
            finally
            {
                CurrentUploads.Remove(file.Verified.Hash, out _);
            }
        }
    }

    // Inner file upload. Should contain the compressed data that we are doing to upload. WIP.
    private async Task UploadFile(UploadableFile file, CancellationToken cancelToken)
    {
        if (!File.Exists(file.LocalPath))
            throw new FileNotFoundException("File to upload does not exist.", file.LocalPath);

        Progress<FileTransferProgress>? progressTracker = new((prog) =>
        {
            try
            {
                CurrentUploads[file.Verified.Hash] = prog;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[{hash}] Could not set upload progress", file.Verified.Hash);
            }
        });

        using var fileStream = File.OpenRead(file.LocalPath);
        using var content = new ProgressableStreamContent(fileStream, progressTracker);

        var response = await _transferService.SendRequestStreamAsync(HttpMethod.Put, new(file.Verified.Link), content, cancelToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}