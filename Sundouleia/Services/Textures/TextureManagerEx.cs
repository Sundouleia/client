using CkCommons.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Sundouleia.Services.Configs;
using System.Diagnostics.CodeAnalysis;

namespace Sundouleia.Services.Textures;

/// <summary>
///     Friendly Reminder, all methods in this class must be called in the framework thread or they will fail.
/// </summary>
public static class TextureManagerEx
{    
    public static IDalamudTextureWrap GetProfilePicture(byte[] imageData)
        => Svc.Texture.CreateFromImageAsync(imageData).Result;
    
    //public static IDalamudTextureWrap? GetMetadataPath(ImageDataType folder, string path)
    //    => Svc.Texture.GetFromFile(Path.Combine(ConfigFileProvider.ThumbnailDirectory, folder.ToString(), path)).GetWrapOrDefault();

    //public static async Task<IDalamudTextureWrap?> RentMetadataPath(ImageDataType folder, string path)
    //    => await TextureManager.RentTextureAsync(Path.Combine(ConfigFileProvider.ThumbnailDirectory, folder.ToString(), path));

    public static bool TryRentAssetImage(string path, [NotNullWhen(true)] out IDalamudTextureWrap? fileTexture)
        => TextureManager.TryRentAssetDirectoryImage(path, out fileTexture);
}
