using CkCommons.RichText;
using CkCommons.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Data;
using Microsoft.Extensions.Hosting;
using System.Diagnostics.CodeAnalysis;

namespace Sundouleia.Services.Textures;

/// <summary>
///     Friendly Reminder, all methods in this class must be called
///     in the framework thread or they will fail. <para />
///     Everything in here is effectively static, but it is still 
///     created as a singleton for the constructor.
/// </summary>
public class CosmeticService : IHostedService, IDisposable
{
    private readonly ILogger<CosmeticService> _logger;
    public CosmeticService(ILogger<CosmeticService> logger, SundouleiaMediator mediator)
    {
        _logger = logger;
        CoreTextures = TextureManager.CreateEnumTextureCache(CosmeticLabels.NecessaryImages);
        EmoteTextures = TextureManager.CreateEnumTextureCache(CosmeticLabels.EmoteTextures);
        CkRichText.DefineEmoteResolver(TryResolveEmote);

        LoadAllCosmetics();
    }
    
    public static EnumTextureCache<CoreTexture> CoreTextures;
    public static EnumTextureCache<EmoteTexture>EmoteTextures;

    // Processed by the string name equivalent of the above EnumTextureCaches.
    private static ConcurrentDictionary<string, IDalamudTextureWrap> InternalCosmeticCache = [];

    public void Dispose()
    {
        _logger.LogInformation("CosmeticCache Disposing.");
        foreach (var texture in InternalCosmeticCache.Values)
            texture?.Dispose();

        InternalCosmeticCache.Clear();
    }

    /// <summary>
    ///     Initializes all plugin textures desired into the cosmetic service 
    ///     for use throughout the plugin lifetime. <para />
    ///     Called from the constructor, which occurs on the framework thread.
    /// </summary>
    private void LoadAllCosmetics()
    {
        foreach (var label in CosmeticLabels.CosmeticTextures)
        {
            var key = label.Key;
            var path = label.Value;
            if (string.IsNullOrEmpty(path))
            {
                _logger.LogError($"No Texture for [{key}] (Path is empty or was not provided)");
                continue;
            }

            _logger.LogDebug($"Renting image for cosmetic cache key [{key}]. (Path: {path})", LoggerType.Textures);
            if (TextureManager.TryRentAssetDirectoryImage(path, out var texture))
                InternalCosmeticCache[key] = texture;
        }
        _logger.LogInformation("LoadAllCosmetics completed initial load of all textures.", LoggerType.Textures);
    }

    /// <summary>
    ///     Retrieve an emote texture given its name via the resolver. <para /> Returns null if not found.
    /// </summary>
    private IDalamudTextureWrap? TryResolveEmote(string name)
        => Enum.TryParse<EmoteTexture>(name, out var key) ? EmoteTextures.Cache.GetValueOrDefault(key) : null;

    /// <summary>
    ///     Grabs the BG texture from Sundouleia Cosmetic Cache Service, if it exists. <para />
    ///     texture passed out will be null when returning false.
    /// </summary>
    public static bool TryGetPlateBg(PlateElement section, PlateBG style, [NotNullWhen(true)] out IDalamudTextureWrap value)
    {
        var res = InternalCosmeticCache.TryGetValue(section.ToString() + "_Background_" + style.ToString(), out var texture);
        value = res ? texture! : null!;
        return res;
    }

    /// <summary>
    ///     Grabs the Border texture from Sundouleia Cosmetic Cache Service, if it exists. <para />
    ///     texture passed out will be null when returning false.
    /// </summary>
    public static bool TryGetPlateBorder(PlateElement section, PlateBorder style, [NotNullWhen(true)] out IDalamudTextureWrap value)
    {
        var res = InternalCosmeticCache.TryGetValue(section.ToString() + "_Border_" + style.ToString(), out var texture);
        value = res ? texture! : null!;
        return res;
    }

    /// <summary>
    ///     Grabs the Overlay texture from Sundouleia Cosmetic Cache Service, if it exists. <para />
    ///     texture passed out will be null when returning false.
    /// </summary>
    public static bool TryGetPlateOverlay(PlateElement section, PlateOverlay style, [NotNullWhen(true)] out IDalamudTextureWrap value)
    {
        var res = InternalCosmeticCache.TryGetValue(section.ToString() + "_Overlay_" + style.ToString(), out var texture);
        value = res ? texture! : null!;
        return res;
    }

    /// <summary>
    ///     Helpful for getting the supporter icon texture and tooltip for the
    ///     <paramref name="userData"/> wherever it is drawn in the UI.
    /// </summary>
    public static (IDalamudTextureWrap? SupporterWrap, string Tooltip) GetSupporterInfo(UserData user)
        => user.Tier switch
        {
            CkVanityTier.ShopKeeper => (CoreTextures.Cache[CoreTexture.Tier4Icon], "Plugin Author of Sundouleia."),
            CkVanityTier.DistinguishedConnoisseur => (CoreTextures.Cache[CoreTexture.Tier3Icon], $"{user.AliasOrUID} is supporting Sundouleia as a Distinguished Connoisseur"),
            CkVanityTier.EsteemedPatron => (CoreTextures.Cache[CoreTexture.Tier2Icon], $"{user.AliasOrUID} is supporting Sundouleia as a Esteemed Patron"),
            CkVanityTier.IllustriousSupporter => (CoreTextures.Cache[CoreTexture.Tier1Icon], $"{user.AliasOrUID} is supporting Sundouleia as a Illustrious Supporter"),
            CkVanityTier.ServerBooster => (CoreTextures.Cache[CoreTexture.TierBoosterIcon], $"{user.AliasOrUID} is server boosting the Sundouleia Discord!"),
            _ => (null, string.Empty),
        };

    // Hosted Service things.
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cosmetic Service Started.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cosmetic Service Stopped.");
        return Task.CompletedTask;
    }

}
