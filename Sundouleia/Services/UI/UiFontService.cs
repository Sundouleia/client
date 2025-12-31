using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Microsoft.Extensions.Hosting;

namespace Sundouleia.Services;

/// <summary>
///     Manages Custom fonts during plugin lifetime. <para />
///     Should probably look at chat2 to see how to handle pointers for fonts 
///     and how to get scaled fonts more efficiently because right now anything
///     over 50px seems to take over 3 seconds to load.
/// </summary>
public sealed class UiFontService : IHostedService
{
    public static IFontHandle IconFont => Svc.PluginInterface.UiBuilder.IconFontFixedWidthHandle;
    public static IFontHandle UidFont { get; private set; }
    public static IFontHandle Default150Percent { get; private set; }

    // Shortcut.
    private IFontAtlas FontAtlas => Svc.PluginInterface.UiBuilder.FontAtlas;

    public UiFontService()
    { }

    private async Task InitializeAllFonts()
    {
        // Initialize the necessary fonts.
        await InitNecessaryFonts().ConfigureAwait(false);
        // Build the fonts.
        await FontAtlas.BuildFontsAsync().ConfigureAwait(false);

        Svc.Logger.Information("UiFontService: Fonts initialized successfully.");
    }

    private async Task InitNecessaryFonts()
    {
        UidFont = FontAtlas.NewDelegateFontHandle(tk =>
        {
            tk.OnPreBuild(tk => tk.AddDalamudAssetFont(Dalamud.DalamudAsset.NotoSansJpMedium, new() { SizePx = 35 }));
        });

        Default150Percent = FontAtlas.NewDelegateFontHandle(tk =>
        {
            tk.OnPreBuild(tk => tk.AddDalamudDefaultFont(UiBuilder.DefaultFontSizePx * 1.5f));
        });

        // Wait for them to be valid.
        await UidFont.WaitAsync().ConfigureAwait(false);
        await Default150Percent.WaitAsync().ConfigureAwait(false);
        Svc.Logger.Information("UiFontService: Initialized Necessary fonts.");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Svc.Logger.Information("UiFontService Started.");
        _ = InitializeAllFonts();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Svc.Logger.Information("UiFontService Stopped.");
        UidFont?.Dispose();
        Default150Percent?.Dispose();
        return Task.CompletedTask;
    }
}
