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
        MainConfig config, FileCacheManager fileDbManager, FileTransferService transferService)
        : base(logger, mediator)
    {
        _config = config;
        _fileDbManager = fileDbManager;
        _transferService = transferService;
    }

    public FileTransferProgress CurrentUploads { get; } = new(); // update as time does on.
    public bool IsUploading => CurrentUploads.TotalFiles > 0;

    /// <summary>
    ///     Uploads all necessary files via their authorized upload links to the server.
    /// </summary>
    public async Task<List<ModFile>> UploadFiles(List<VerifiedModFile> filesToUpload)
    {
        var toReturn = new List<ModFile>();
        foreach (var file in filesToUpload)
        {
            // If the file is not cached, we should not upload it. The file needs to be valid.
            if (_fileDbManager.GetFileCacheByHash(file.Hash) is not { } fileEntity)
            {
                Logger.LogWarning($"File {file.Hash} is not cached, skipping upload.");
                continue;
            }

            // If the upload is already being processed, skip over it.
            if (!CurrentUploads.TryAddFile(file.Hash, 0))
            {
                Logger.LogWarning($"File {file.Hash} is already being uploaded, skipping.");
                continue;
            }

            try
            {
                Logger.LogDebug($"Upload file {file.Hash} [{fileEntity.Size ?? 0}bytes]", LoggerType.FileUploads);
                // Attempt to upload the file using the authorized upload link.
                await UploadFile(file, fileEntity, CancellationToken.None).ConfigureAwait(false);
                Logger.LogDebug($"Successfully uploaded file {file.Hash}.", LoggerType.FileUploads);
                toReturn.Add(new ModFile(file.Hash, file.GamePaths, file.SwappedPath));
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error uploading file {file.Hash}. {ex}");
            }
            finally
            {
                CurrentUploads.RemoveFile(file.Hash);
            }
        }
        // Return the uploaded files to transfer.
        return toReturn;
    }

    /// <summary>
    ///     Inner file upload. Should contain the compressed data that we are doing to upload. WIP.
    /// </summary>
    /// <exception cref="FileNotFoundException"></exception>
    private async Task UploadFile(VerifiedModFile modFile, FileCacheEntity fileInfo, CancellationToken cancelToken)
    {
        // Construct a new FileTransferProgress tracker to monitor the upload progress.
        Progress<long>? progressTracker = new((transferredBytes) =>
        {
            try
            {
                CurrentUploads.AddFileProgress(modFile.Hash, transferredBytes);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[{modFile.Hash}] Could not set upload progress. {ex}");
            }
        });

        using var fileStream = File.OpenRead(fileInfo.ResolvedFilepath);
        using var content = new ProgressableStreamContent(fileStream, progressTracker);

        var response = await _transferService.SendRequestStreamAsync(HttpMethod.Put, new(modFile.Link), content, cancelToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}