namespace Sundouleia.WebAPI.Files.Models;

/// <summary>
///     Tells us how much of a file is uploaded to/downloaded from Sundouleia's FileHost.
/// </summary>
public class FileTransferProgress
{
	private readonly Lock _lock = new();

	/// <summary>
	///     How many bytes have been transferred so far.
	/// </summary
	public long Transferred
	{
		get
		{
			lock (_lock)
			{
				return _fileTransfers.Values.Sum(t => t.Transferred);
			}
		}
	}

	/// <summary>
	///    The sum total size of all the files being transferred, in bytes.
	/// </summary>
	public long TotalSize
	{
		get
		{
			lock (_lock)
			{
				return _fileTransfers.Values.Sum(t => t.TotalSize);
			}
		}
	}

	/// <summary>
	///   The total number of files being transferred.
	/// </summary>
	public int TotalFiles
	{
		get
		{
			lock (_lock)
			{
				return _fileTransfers.Count;
			}
		}
	}

	public int FilesCompleted
	{
		get
		{
			lock (_lock)
			{
				return _fileTransfers.Values.Count(t => t.Completed);
			}
		}
	}

	private Dictionary<string, Transfer> _fileTransfers = [];

	/// <summary>
	/// Adds or updates a file being tracked by its hash, setting its total size.
	/// </summary>
	/// <param name="fileHash"></param>
	/// <param name="fileSize"></param>
	public void AddOrUpdateFile(string fileHash, long fileSize)
	{
		lock (_lock)
		{
			if (!_fileTransfers.ContainsKey(fileHash))
			{
				_fileTransfers[fileHash] = new Transfer(0, fileSize);
			}
			else
			{
				_fileTransfers[fileHash].TotalSize = fileSize;
			}
		}
	}

	/// <summary>
	///    Tries to add a new file to be tracked by its hash and size.
	/// </summary>
	/// <param name="fileHash"></param>
	/// <param name="fileSize"></param>
	/// <returns>True if the file was added, false if it was already being tracked.</returns>
	public bool TryAddFile(string fileHash, long fileSize)
	{
		lock (_lock)
		{
			if (_fileTransfers.ContainsKey(fileHash))
				return false;

			_fileTransfers[fileHash] = new Transfer(0, fileSize);
			return true;
		}
	}

	/// <summary>
	///     Adds progress to a specific file being tracked by its hash.
	/// </summary>
	public void AddFileProgress(string fileHash, long bytesTransferred)
	{
		lock (_lock)
		{
			if (_fileTransfers.ContainsKey(fileHash))
			{
				_fileTransfers[fileHash].Transferred += bytesTransferred;
			}
		}
	}

	/// <summary>
	///     Marks a specific file as completed by setting its transferred bytes to its total size.
	/// </summary>
	public void MarkFileCompleted(string fileHash)
	{
		lock (_lock)
		{
			if (_fileTransfers.ContainsKey(fileHash))
			{
				_fileTransfers[fileHash].Transferred = _fileTransfers[fileHash].TotalSize;
			}
		}
	}

	/// <summary>
	///     Removes a file from tracking by its hash.
	/// </summary>
	public void RemoveFile(string fileHash)
	{
		lock (_lock)
		{
			_fileTransfers.Remove(fileHash);
		}
	}

	/// <summary>
	///     Percentage of the file transfer completed, from 0.0 to 1.0.
	/// </summary>
	/// <remarks>
	///     Returns 0 if TotalSize is 0 to avoid division by zero.
	/// </remarks>
	public float Percentage => TotalSize > 0 ? (float)Transferred / TotalSize : 0;

	private class Transfer(long Transferred, long TotalSize)
	{
		public long Transferred { get; set; } = Transferred;
		public long TotalSize { get; set; } = TotalSize;
		public bool Completed => Transferred >= TotalSize;
	}
}