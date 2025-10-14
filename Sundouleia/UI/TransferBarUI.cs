using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.WebAPI.Files;
using Sundouleia.WebAPI.Files.Models;

namespace Sundouleia.Gui;

public class TransferBarUI : WindowMediatorSubscriberBase
{
    private const int TRANSPARENCY = 100;
    private const int DL_BAR_BORDER = 3;
    private readonly MainConfig _config;
    private readonly FileUploader _fileUploader;

    // For correct display tracking of upload and download displays.
    // Change this later to have Sundouleias own flavor and taste, as the original was somewhat bland.
    // Additionally i dislike the way these dictionaries are structured and believe they deserve to be changed.
    private readonly ConcurrentDictionary<PlayerHandler, bool> _uploads = new();
    private readonly ConcurrentDictionary<PlayerHandler, FileTransferProgress> _downloads = new();

    public TransferBarUI(ILogger<TransferBarUI> logger, SundouleiaMediator mediator, MainConfig config,
        FileUploader fileUploader) : base(logger, mediator, "##SundouleiaDLs")
    {
        _config = config;
        _fileUploader = fileUploader;

        this.SetBoundaries(new(500, 90), new(500, 90));

        // Define flags to enforce the persistent window behavior.
        Flags |= ImGuiWindowFlags.NoMove;
        Flags |= ImGuiWindowFlags.NoBackground;
        Flags |= ImGuiWindowFlags.NoInputs;
        Flags |= ImGuiWindowFlags.NoNavFocus;
        Flags |= ImGuiWindowFlags.NoResize;
        Flags |= ImGuiWindowFlags.NoScrollbar;
        Flags |= ImGuiWindowFlags.NoTitleBar;
        Flags |= ImGuiWindowFlags.NoDecoration;
        Flags |= ImGuiWindowFlags.NoFocusOnAppearing;
        // Along with it's attributes.
        DisableWindowSounds = true;
        ForceMainWindow = true;
        IsOpen = true;

        // Temporarily do this, hopefully we dont always need to.
        Mediator.Subscribe<FileDownloadStarted>(this, (msg) =>
        {
            _logger.LogWarning($"Starting download tracking for {msg.Player.NameString}({msg.Player.Sundesmo.GetNickAliasOrUid()})");
            _downloads[msg.Player] = msg.Status;
        });
        Mediator.Subscribe<FileDownloadComplete>(this, (msg) =>
        {
            _logger.LogWarning($"Ending download tracking for {msg.Player.NameString}");
            _downloads.TryRemove(msg.Player, out _);
        });
        // For uploads (can configure later as there is not much reason with our new system)
        Mediator.Subscribe<FileUploading>(this, _ =>
        {
            _logger.LogWarning($"Starting upload tracking for {_.Player.NameString}");
            _uploads[_.Player] = true; 
        });
        Mediator.Subscribe<FileUploaded>(this, (msg) =>
        {
            _logger.LogWarning($"Ending upload tracking for {msg.Player.NameString}");
            _uploads.TryRemove(msg.Player, out _);
        });
        // For GPose handling.
        Mediator.Subscribe<GPoseStartMessage>(this, _ => IsOpen = false);
        Mediator.Subscribe<GPoseEndMessage>(this, _ => IsOpen = true);
    }

    protected override void PreDrawInternal()
    { }

    protected override void DrawInternal()
    {
        try
        {
            if (_config.Current.TransferWindow)
                DrawTransferWindow();

            if (_config.Current.TransferBars)
                DrawTransferBars();

            if (_config.Current.ShowUploadingText)
                DrawUploadingText();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to draw TransferUI: {ex}", LoggerType.UIManagement);
        }
    }

    protected override void PostDrawInternal()
    { }

    private void DrawTransferWindow()
    {
        var currentUploads = _fileUploader.CurrentUploads;
        var totalUploads = currentUploads.TotalFiles;

        var totalUploaded = currentUploads.Transferred;
        var totalToUpload = currentUploads.TotalSize;

        CkGui.OutlinedFont($"▲", ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
        ImGui.SameLine();
        var xDistance = ImGui.GetCursorPosX();
        CkGui.OutlinedFont($"Uploading {totalUploads} files.",
            ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
        ImGui.NewLine();
        ImGui.SameLine(xDistance);
        CkGui.OutlinedFont($"{SundouleiaEx.ByteToString(totalUploaded, false)}/{SundouleiaEx.ByteToString(totalToUpload)}", ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);

        if (_downloads.Any())
            ImGui.Separator();
    }

    private void DrawTransferBars()
    {
        foreach (var (player, downloads) in _downloads.ToList())
        {
            // do now show if not rendered.
            if (!player.IsRendered)
                continue;
            // Grab their position.
            var pos = player.DataState.Position;
            var screenPos = Svc.GameGui.WorldToScreen(pos, out var sPos) ? sPos : Vector2.Zero;
            // Do not display invalid translations.
            if (screenPos == Vector2.Zero)
                continue;

            // Obtain the transfer in bytes.
            var totalBytes = downloads.TotalSize;
            var transferredBytes = downloads.Transferred;

            var maxDlText = $"{SundouleiaEx.ByteToString(totalBytes, addSuffix: false)}/{SundouleiaEx.ByteToString(totalBytes)}";
            var textSize = _config.Current.TransferBarText ? ImGui.CalcTextSize(maxDlText) : new Vector2(10, 10);

            int dlBarHeight = _config.Current.TransferBarHeight > ((int)textSize.Y + 5) ? _config.Current.TransferBarHeight : (int)textSize.Y + 5;
            int dlBarWidth = _config.Current.TransferBarWidth > ((int)textSize.X + 10) ? _config.Current.TransferBarWidth : (int)textSize.X + 10;

            // Can probably grab the CkCommons progress bar method from here over using this, as it is far more customizable.
            var dlBarStart = new Vector2(screenPos.X - dlBarWidth / 2f, screenPos.Y - dlBarHeight / 2f);
            var dlBarEnd = new Vector2(screenPos.X + dlBarWidth / 2f, screenPos.Y + dlBarHeight / 2f);
            var bdl = ImGui.GetBackgroundDrawList();
            bdl.AddRectFilled(
                dlBarStart with { X = dlBarStart.X - DL_BAR_BORDER - 1, Y = dlBarStart.Y - DL_BAR_BORDER - 1 },
                dlBarEnd with { X = dlBarEnd.X + DL_BAR_BORDER + 1, Y = dlBarEnd.Y + DL_BAR_BORDER + 1 },
                CkGui.Color(0, 0, 0, TRANSPARENCY), 1);
            bdl.AddRectFilled(dlBarStart with { X = dlBarStart.X - DL_BAR_BORDER, Y = dlBarStart.Y - DL_BAR_BORDER },
                dlBarEnd with { X = dlBarEnd.X + DL_BAR_BORDER, Y = dlBarEnd.Y + DL_BAR_BORDER },
                CkGui.Color(220, 220, 220, TRANSPARENCY), 1);
            bdl.AddRectFilled(dlBarStart, dlBarEnd,
                CkGui.Color(0, 0, 0, TRANSPARENCY), 1);
            var dlProgressPercent = transferredBytes / (double)totalBytes;
            bdl.AddRectFilled(dlBarStart,
                dlBarEnd with { X = dlBarStart.X + (float)(dlProgressPercent * dlBarWidth) },
                CkGui.Color(50, 205, 50, TRANSPARENCY), 1);

            if (_config.Current.TransferBarText)
            {
                var downloadText = $"{SundouleiaEx.ByteToString(transferredBytes, addSuffix: false)}/{SundouleiaEx.ByteToString(totalBytes)}";
                bdl.OutlinedFont(downloadText,
                    screenPos with { X = screenPos.X - textSize.X / 2f - 1, Y = screenPos.Y - textSize.Y / 2f - 1 },
                    CkGui.Color(255, 255, 255, TRANSPARENCY),
                    CkGui.Color(0, 0, 0, TRANSPARENCY), 1);
            }
        }
    }

    private void DrawUploadingText()
    {
        foreach (var player in _uploads.Select(p => p.Key).ToList())
        {
            // do now show if not rendered.
            if (!player.IsRendered) continue;
            // Grab their position.
            var pos = player.DataState.Position;
            var screenPos = Svc.GameGui.WorldToScreen(pos, out var sPos) ? sPos : Vector2.Zero;
            // Do not display invalid translations.
            if (screenPos == Vector2.Zero) continue;

            try
            {
                using var _ = UiFontService.UidFont.Push();
                var uploadText = "Uploading";

                var textSize = ImGui.CalcTextSize(uploadText);

                var bdl = ImGui.GetBackgroundDrawList();
                bdl.OutlinedFont(uploadText,
                    screenPos with { X = screenPos.X - textSize.X / 2f - 1, Y = screenPos.Y - textSize.Y / 2f - 1 },
                    CkGui.Color(255, 255, 0, TRANSPARENCY),
                    CkGui.Color(0, 0, 0, TRANSPARENCY), 2);
            }
            catch
            { }
        }
    }

    // Capable of preventing the window from being drawn.
    public override bool DrawConditions()
    {
        if (!_config.Current.TransferBars) return false;
        if (!_downloads.Any() && _fileUploader.CurrentUploads.TotalFiles == 0 && !_uploads.Any()) return false;
        if (!IsOpen) return false;
        return true;
    }

    public override void PreDraw()
    {
        base.PreDraw();

        Flags |= ImGuiWindowFlags.NoMove;
        Flags |= ImGuiWindowFlags.NoBackground;
        Flags |= ImGuiWindowFlags.NoInputs;
        Flags |= ImGuiWindowFlags.NoResize;

        var maxHeight = ImGui.GetTextLineHeight() * (_config.Current.MaxParallelDownloads + 3);
        this.SetBoundaries(new(300, maxHeight), new(300, maxHeight));
    }
}