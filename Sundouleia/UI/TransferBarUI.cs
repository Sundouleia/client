using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Sundouleia;
using Sundouleia.Pairs;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI.Files;
using Sundouleia.WebAPI.Files.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Numerics;
using Sundouleia.PlayerClient;
using Sundouleia.Utils;
using CkCommons.Gui;

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
    private readonly ConcurrentDictionary<PlayerHandler, Dictionary<string, FileDownloadStatus>> _downloads = new();

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
        Mediator.Subscribe<FileDownloadStarted>(this, (msg) => _downloads[msg.Player] = msg.Status);
        Mediator.Subscribe<FileDownloadComplete>(this, (msg) => _downloads.TryRemove(msg.Player, out _));
        // For uploads
        Mediator.Subscribe<FileUploading>(this, _ => _uploads[_.Player] = true);
        Mediator.Subscribe<FileUploaded>(this, (msg) => _uploads.TryRemove(msg.Player, out _));
        // For GPose handling.
        Mediator.Subscribe<GPoseStartMessage>(this, _ => IsOpen = false);
        Mediator.Subscribe<GPoseEndMessage>(this, _ => IsOpen = true);
    }

    protected override void DrawInternal()
    {
        if (_config.Current.TransferWindow)
        {
            try
            {
                DrawTransferWindow();
            }
            catch { }
        }
        if (_config.Current.TransferBars)
        {
            try
            {
                DrawTransferBars();
            }
            catch
            { }
        }
    }

    private void DrawTransferWindow()
    {
        if (_fileUploader.CurrentUploads.Any())
        {
            var currentUploads = _fileUploader.CurrentUploads.ToList();
            var totalUploads = currentUploads.Count;

            var doneUploads = currentUploads.Count(c => c.IsTransferred);
            var totalUploaded = currentUploads.Sum(c => c.Transferred);
            var totalToUpload = currentUploads.Sum(c => c.Total);

            CkGui.OutlinedFont($"▲", ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
            ImGui.SameLine();
            var xDistance = ImGui.GetCursorPosX();
            CkGui.OutlinedFont($"Compressing+Uploading {doneUploads}/{totalUploads}",
                ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
            ImGui.NewLine();
            ImGui.SameLine(xDistance);
            CkGui.OutlinedFont($"{SundouleiaEx.ByteToString(totalUploaded, false)}/{SundouleiaEx.ByteToString(totalToUpload)}", ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);

            if (_downloads.Any())
                ImGui.Separator();
        }

        foreach (var item in _downloads.ToList())
        {
            var dlSlot = item.Value.Count(c => c.Value.DownloadStatus == DownloadStatus.WaitingForSlot);
            var dlQueue = item.Value.Count(c => c.Value.DownloadStatus == DownloadStatus.WaitingForQueue);
            var dlProg = item.Value.Count(c => c.Value.DownloadStatus == DownloadStatus.Downloading);
            var dlDecomp = item.Value.Count(c => c.Value.DownloadStatus == DownloadStatus.Decompressing);
            var totalFiles = item.Value.Sum(c => c.Value.TotalFiles);
            var transferredFiles = item.Value.Sum(c => c.Value.TransferredFiles);
            var totalBytes = item.Value.Sum(c => c.Value.TotalBytes);
            var transferredBytes = item.Value.Sum(c => c.Value.TransferredBytes);

            CkGui.OutlinedFont($"▼", ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
            ImGui.SameLine();
            var xDistance = ImGui.GetCursorPosX();
            CkGui.OutlinedFont($"{item.Key.NameString} [W:{dlSlot}/Q:{dlQueue}/P:{dlProg}/D:{dlDecomp}]", ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
            ImGui.NewLine();
            ImGui.SameLine(xDistance);
            CkGui.OutlinedFont($"{transferredFiles}/{totalFiles} ({SundouleiaEx.ByteToString(transferredBytes, false)}/{SundouleiaEx.ByteToString(totalBytes)})", ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
        }
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
            var totalBytes = downloads.Sum(c => c.Value.TotalBytes);
            var transferredBytes = downloads.Sum(c => c.Value.TransferredBytes);

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
        if (!_downloads.Any() && !_fileUploader.CurrentUploads.Any() && !_uploads.Any()) return false;
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
        this.SetBoundaries(new(300, maxHeight), new (300, maxHeight));
    }
}