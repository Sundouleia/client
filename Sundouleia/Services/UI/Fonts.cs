using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Microsoft.Extensions.Hosting;

namespace Sundouleia.Services;

/// <summary>
///     Manages Custom fonts during plugin lifetime. <para />
/// </summary>
public static class Fonts
{
    public static IFontHandle IconFont => Svc.PluginInterface.UiBuilder.IconFontFixedWidthHandle;
    public static IFontHandle UidFont { get; private set; }
    public static IFontHandle Default150Percent { get; private set; }

    /// <summary>
    ///     Helper task to initialize GagSpeak's fonts.
    /// </summary>
    public static async Task InitializeFonts()
    {
        UidFont = Svc.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(tk =>
        {
            tk.OnPreBuild(tk => tk.AddDalamudAssetFont(Dalamud.DalamudAsset.NotoSansJpMedium, new() { SizePx = 35 }));
        });

        Default150Percent = Svc.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(tk =>
        {
            tk.OnPreBuild(tk => tk.AddDalamudDefaultFont(UiBuilder.DefaultFontSizePx * 1.5f));
        });

        // Wait for them to be valid.
        await UidFont.WaitAsync().ConfigureAwait(false);
        await Default150Percent.WaitAsync().ConfigureAwait(false);
        await Svc.PluginInterface.UiBuilder.FontAtlas.BuildFontsAsync().ConfigureAwait(false);
        Svc.Logger.Information("Fonts: Initialized Necessary fonts.");
    }

    public static void Dispose()
    {
        Svc.Logger.Information("Disposing Fonts.");
        UidFont?.Dispose();
        Default150Percent?.Dispose();
    }
}
