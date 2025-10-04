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
    private readonly FileCacheManager _fileDbManager;

    private readonly Dictionary<string, DateTime> _verifiedUploadedHashes = new(StringComparer.Ordinal);
    private CancellationTokenSource? _uploadCTS = new();

    public FileUploadManager(ILogger<FileUploadManager> logger, SundouleiaMediator mediator,
        MainConfig config, FileCacheManager fileDbManager) : base(logger, mediator)
    {
        _config = config;
        _fileDbManager = fileDbManager;

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

    // Removes all file upload links / uploaded files respective to our user from the server.
    public async Task DeleteAllFiles()
    {
        await Task.Delay(1).ConfigureAwait(false);
    }

    // Passes in a single ModFileInfo containing the hash and replacement data.
    // makes a request to upload said files contents.
    // this is a WIP as we still need to restructure file transfer. The end goal is that this returns the download link, or it doesnt?
    // point being this operation is just to upload the file conetents. If it does or doesnt return the download link changes if we use it or not when
    // we send the remaining files to sync in post from this method.
    public async Task<ModFileData> UploadFiles(ModFileInfo modFileInfo)
    {
        await Task.Delay(1).ConfigureAwait(false);
        return new ModFileData(modFileInfo.Hash, modFileInfo.GamePaths, modFileInfo.SwappedPath, "NULL");
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