using Sundouleia.ModFiles;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Data;

namespace Sundouleia.WebAPI.Files;

public sealed class FileUploader : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly FileCacheManager _fileDbManager;

    public FileUploader(ILogger<FileUploader> logger, SundouleiaMediator mediator,
        MainConfig config, FileCacheManager fileDbManager) : base(logger, mediator)
    {
        _config = config;
        _fileDbManager = fileDbManager;

    }

    public List<string> CurrentUploads { get; } = []; // update as time does on.
    public bool IsUploading => CurrentUploads.Count > 0;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }


    // Removes all file upload links / uploaded files respective to our user from the server.
    public async Task DeleteAllFiles()
    {
        await Task.Delay(1).ConfigureAwait(false);
    }

    // Uploads all necessary files via their authorized upload links to the server.
    public async Task UploadFiles(List<VerifiedModFile> filesToUpload) // Don't need to include the visible players here just yet.
    {
        await Task.Delay(1).ConfigureAwait(false);
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