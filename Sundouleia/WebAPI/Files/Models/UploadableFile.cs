using SundouleiaAPI.Data;

namespace Sundouleia.WebAPI.Files.Models;

public record UploadableFile(VerifiedModFile Verified, string LocalPath, long Size);