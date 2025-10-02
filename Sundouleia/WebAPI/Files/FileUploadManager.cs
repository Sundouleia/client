using CkCommons;
using Sundouleia.ModFiles;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI.Files.Models;
using SundouleiaAPI.Data;

namespace Sundouleia.WebAPI.Files;

// Appears to manage the uploading of files to the connected server using the FileTransferOrchestrator.
// will be fastely simplified later.
public sealed class FileUploadManager : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly ServerConfigManager _serverConfigs;
    private readonly FileCacheManager _fileDbManager;
    private readonly FileTransferOrchestrator _orchestrator;

    private readonly Dictionary<string, DateTime> _verifiedUploadedHashes = new(StringComparer.Ordinal);
    private CancellationTokenSource? _uploadCTS = new();

    public FileUploadManager(ILogger<FileUploadManager> logger, SundouleiaMediator mediator,
        MainConfig config, ServerConfigManager serverConfigs, FileCacheManager fileDbManager,
        FileTransferOrchestrator orchestrator) : base(logger, mediator)
    {
        _config = config;
        _serverConfigs = serverConfigs;
        _fileDbManager = fileDbManager;
        _orchestrator = orchestrator;

        Mediator.Subscribe<DisconnectedMessage>(this, _ => Reset());
    }

    public List<string> CurrentUploads { get; } = []; // update as time does on.
    public bool IsUploading => CurrentUploads.Count > 0;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Reset();
    }

    // Resets the upload manager along with all of its valid upload hashes and such.
    private void Reset()
    {
        _uploadCTS.SafeCancelDispose();
        CurrentUploads.Clear();
        _verifiedUploadedHashes.Clear();
    }

    // Cancels the current upload file task.
    public bool CancelUpload()
    {
        if (CurrentUploads.Any())
        {
            Logger.LogDebug("Cancelling current upload");
            _uploadCTS.SafeCancelDispose();
            CurrentUploads.Clear();
            return true;
        }

        return false;
    }

    // Removes all file upload links from the server.
    public async Task DeleteAllFiles()
    {
        await Task.Delay(1).ConfigureAwait(false);
    }

    // Uploads remaining files that had were new and had to be uploaded after pushing a ModDataUpdate.
    // Expected to have the authorized upload links here, will return the subset of download links associated with their data hash.
    // Structure pending soon.
    public async Task<ModDataUpdate> UploadFiles(ModDataUpdate data)
    {
        await Task.Delay(1).ConfigureAwait(false);
        return data;
    }

    // Inner file upload. Should contain the compressed data that we are doing to upload. WIP.
    private async Task UploadFile(byte[] compressedFile, string fileHash, bool postProgress, CancellationToken uploadToken)
    {
        await Task.Delay(1).ConfigureAwait(false);
    }

    // Uploads the file-stream of the actual mod data to the FTP server for transfer.
    // munged might be helpful, but can be added if independent sends fail only.
    private async Task UploadFileStream(byte[] compressedFile, string fileHash, bool munged, bool postProgress, CancellationToken uploadToken)
    {
        await Task.Delay(1).ConfigureAwait(false);
    }
}