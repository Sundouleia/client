using Sundouleia.ModFiles;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI.Files.Models;
using SundouleiaAPI.Data;
using ZstdSharp;

namespace Sundouleia.WebAPI.Files;

public sealed class FileUploader : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly FileCacheManager _fileDbManager;
    private readonly FileTransferService _transferService;
    private readonly Compressor  _compressor = new();

    public FileUploader(ILogger<FileUploader> logger, SundouleiaMediator mediator,
        MainConfig config, FileCacheManager fileDbManager, FileTransferService transferService)
        : base(logger, mediator)
    {
        _config = config;
        _fileDbManager = fileDbManager;
        _transferService = transferService;
    }
    
    protected override void Dispose(bool disposing)
    {
        _compressor.Dispose();
        base.Dispose(disposing);
    }

    public FileTransferProgress CurrentUploads { get; } = new(); // update as time does on.
    public bool IsUploading => CurrentUploads.TotalFiles > 0;

    /// <summary>
    ///     Uploads all necessary files via their authorized upload links to the server.
    /// </summary>
    public async Task<List<FileHashData>> UploadFiles(List<ValidFileHash> filesToUpload)
    {
        var toReturn = new List<FileHashData>();
        foreach (var file in filesToUpload)
        {
            // If the file is not cached, we should not upload it. The file needs to be valid.
            if (_fileDbManager.GetFileCacheByHash(file.Hash) is not { } fileEntity)
            {
                Logger.LogWarning($"File {file.Hash} is not cached, skipping upload.");
                continue;
            }

            var fileSize = fileEntity.Size ?? 0;
            // If the upload is already being processed, skip over it.
            if (!CurrentUploads.TryAddFile(file.Hash, fileSize))
            {
                Logger.LogWarning($"File {file.Hash} is already being uploaded, skipping.");
                continue;
            }

            try
            {
                Logger.LogDebug($"Upload file {file.Hash} [{fileSize}bytes]", LoggerType.FileUploads);
                // Attempt to upload the file using the authorized upload link.
                await UploadFile(file, fileEntity, CancellationToken.None).ConfigureAwait(false);
                Logger.LogDebug($"Successfully uploaded file {file.Hash}.", LoggerType.FileUploads);
                toReturn.Add(new FileHashData(file.Hash, file.GamePaths));
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
    private async Task UploadFile(ValidFileHash modFile, FileCacheEntity fileInfo, CancellationToken cancelToken)
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

        HttpResponseMessage response;
        if (fileInfo.Size > 300_000)
        {
            var fileBytes = await File.ReadAllBytesAsync(fileInfo.ResolvedFilepath, cancelToken);
            Span<byte> compressedBytes = _compressor.Wrap(fileBytes);
            using var compressedStream = new MemoryStream(compressedBytes.ToArray());
            compressedStream.Position = 0;
            using var content = new ProgressableStreamContent(compressedStream, progressTracker);
            
            Logger.LogDebug($"Compressed uploaded file {fileInfo.Hash} from {fileBytes.Length / 1024} KB to {compressedBytes.Length / 1024} KB.", LoggerType.FileUploads);

            response = await _transferService
                .SendRequestCompressedStreamAsync(HttpMethod.Put, new Uri(modFile.Link), content, fileBytes.LongLength,
                    cancelToken);
        }
        else
        {
            Logger.LogDebug($"File {modFile.Hash} is too small to be compressed.",  LoggerType.FileUploads);
            await using var fileStream = File.OpenRead(fileInfo.ResolvedFilepath);
            using var content = new ProgressableStreamContent(fileStream, progressTracker);

            response =
                await _transferService.SendRequestStreamAsync(HttpMethod.Put, new Uri(modFile.Link), content,
                    cancelToken);
        }
        
        var responseText = await response.Content.ReadAsStringAsync(cancelToken);
        Logger.LogDebug($"{fileInfo.Hash}: {responseText}", LoggerType.FileUploads);
        response.EnsureSuccessStatusCode();

        CurrentUploads.MarkFileCompleted(modFile.Hash);
    }
}