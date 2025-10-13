namespace Sundouleia.WebAPI.Files.Models;

/// <summary>
///     Tells us how much of a file is uploaded to/downloaded from Sundouleia's FileHost.
/// </summary>
public record FileTransferProgress(long Transferred, long TotalSize);