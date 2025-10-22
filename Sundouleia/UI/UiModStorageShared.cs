using CkCommons;
using CkCommons.Gui;
using CkCommons.Helpers;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.ModFiles.Cache;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.WebAPI.Files;
using System.Text.RegularExpressions;

namespace Sundouleia.Gui;

public partial class UiFileCacheShared
{
    private readonly ILogger<UiFileCacheShared> _logger;
    private readonly MainConfig _config;
    private readonly FileUploader _uploader;
    private readonly FileCompactor _compactor;
    private readonly CacheMonitor _monitor;
    private readonly SundouleiaWatcher _mainWatcher;
    private readonly PenumbraWatcher _penumbraWatcher;
    private readonly UiFileDialogService _dialogService;

    // Cache location validators.
    private FilePathValidation _pathValidation = FilePathValidation.InvalidPath;
    private readonly string _rootPath;
    private readonly bool _isLinux;

    private bool _isMonitoring => _mainWatcher.Watcher is not null;


    public UiFileCacheShared(ILogger<UiFileCacheShared> logger, MainConfig config, FileUploader uploader, 
        FileCompactor compactor, CacheMonitor monitor, SundouleiaWatcher mainWatcher, PenumbraWatcher penumbraWatcher, 
        UiFileDialogService dialogService)
    {
        _logger = logger;
        _config = config;
        _uploader = uploader;
        _compactor = compactor;
        _monitor = monitor;
        _mainWatcher = mainWatcher;
        _penumbraWatcher = penumbraWatcher;
        _dialogService = dialogService;

        if (_config.HasValidCacheFolderSetup())
            _pathValidation = FilePathValidation.Valid;

        // Path Rooting & OS Detection.
        _isLinux = Util.IsWine();
        _rootPath = _isLinux ? @"Z:\" : @"C:\";
    }

    public bool IsCachePathValid => IsValidPath(_config.Current.CacheFolder);

    /// <summary>
    ///     Draws out the FileCache Storage component of the mod storage UI. <para />
    ///     Allows the client to configure their current file cache location.
    /// </summary>
    public void DrawFileCacheStorageBox()
    {
        var curCacheDir = _config.Current.CacheFolder;
        var penumbraGenWidth = CkGui.IconTextButtonSize(FAI.FolderPlus, "In Penumbras Parent Folder");
        var rootGenWidth = CkGui.IconTextButtonSize(FAI.FolderPlus, "At Drive Root");
        var rightWidth = penumbraGenWidth + rootGenWidth + ImUtf8.ItemInnerSpacing.X * 2;
        var height = CkStyle.TwoRowHeight() + CkGui.GetSeparatorHeight() + CkGui.CalcFontTextSize("A", UiFontService.UidFont).Y;
        // extra line for error text if any.
        if (!IsCachePathValid)
            height += ImUtf8.FrameHeightSpacing;

        // Framed child.
        using var _ = CkRaii.FramedChildPaddedW("Storage", ImGui.GetContentRegionAvail().X, height, 0, CkColor.VibrantPink.Uint(), CkStyle.ChildRoundingLarge());
        var topLeftPos = ImGui.GetCursorScreenPos();

        CkGui.FontTextCentered("FileCache Storage", UiFontService.UidFont);
        CkGui.Separator(CkColor.VibrantPink.Uint());

        // Draw out the folder icon first, prior to drawing out the file storage.
        if (CkGui.IconButton(FAI.Folder, disabled: _isMonitoring))
            OpenDialogBox();
        CkGui.AttachToolTip(_isMonitoring ? "Must stop monitoring the cache folder before changing it." : "Open file dialog to choose a directory.");

        ImUtf8.SameLineInner();
        // Display the directory in a readonly input text so that we can open the file dailog for selection.
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - rightWidth);
        ImGui.InputTextWithHint("##cacheDirectory", $"{_rootPath}... (Place folder close to root)", ref curCacheDir, 255, ImGuiInputTextFlags.ReadOnly);

        ImUtf8.SameLineInner();
        if (CkGui.IconTextButton(FAI.FolderPlus, "In Penumbras Parent Folder", disabled: _isMonitoring || CachePresetPenumbraParentDirExists()))
        {
            var newPath = CreatePenumbraParentDirCachePreset();
            if (!string.IsNullOrEmpty(newPath))
            {
                _config.Current.CacheFolder = newPath;
                _config.Save();
                _mainWatcher.StartWatcher(newPath);
                _monitor.InvokeScan();
            }
            else
                Svc.Toasts.ShowError($"Failed to Auto-Generate Cache Directory");
        }
        CkGui.AttachToolTip("Automatically creates a SundouleiaCache folder in the same directory that your PenumbraCache is in.");

        ImUtf8.SameLineInner();
        if (CkGui.IconTextButton(FAI.FolderPlus, "At Drive Root", disabled: _isMonitoring || CachePresetRootDirExists()))
        {
            var newPath = CreateRootDirCachePreset();
            if (!string.IsNullOrEmpty(newPath))
            {
                _config.Current.CacheFolder = newPath;
                _config.Save();
                _mainWatcher.StartWatcher(newPath);
                _monitor.InvokeScan();
            }
            else
                Svc.Toasts.ShowError($"Failed to Auto-Generate Cache Directory");
        }
        CkGui.AttachToolTip($"Automatically creates a SundouleiaCache folder at your drive root {{{_rootPath}}}");

        // Slider for storage size.
        using (ImRaii.Disabled(!_config.HasValidCacheFolderSetup()))
        {
            float maxCacheSize = (float)_config.Current.MaxCacheInGiB;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.SliderFloat("##maxcachesize", ref maxCacheSize, 1f, 200f, "%.2f GiB (Max Storage Size)"))
            {
                _config.Current.MaxCacheInGiB = maxCacheSize;
                _config.Save();
            }
        }
        CkGui.AttachToolTip("Storage is governed by Sundouleia, and clears itself upon reaching capacity, " +
            "or after a file has not been accessed for 6 weeks.");

        // Below, if the path validation is not success, display the error text.
        if (_pathValidation is not FilePathValidation.Valid)
            CkGui.CenterColorTextAligned(_pathValidation switch
            {
                FilePathValidation.IsOneDrive => "Path cannot be in OneDrive! Try putting it closer to a root. (C:\\) (C:\\FFXIVModding\\)!",
                FilePathValidation.IsPenumbraDir => "Path cannot be the Penumbra directory!",
                FilePathValidation.NotWritable => "Path must be writable!",
                FilePathValidation.HasOtherFiles => "Directory has files or other sub-folders not beloning to Sundouleia!",
                FilePathValidation.InvalidPath => "Folder must contain only (A-Z), underscores (_), dashes (-) and arabic numbers (0-9).",
                _ => string.Empty,
            }, ImGuiColors.DalamudRed);

        // Go to top right to draw out help text.
        ImGui.SetCursorScreenPos(topLeftPos + new Vector2(_.InnerRegion.X - ImUtf8.FrameHeight, 0));
        CkGui.FramedHoverIconText(FAI.QuestionCircle, ImGuiColors.TankBlue.ToUint(), ImGui.GetColorU32(ImGuiCol.TextDisabled));
        CkGui.AttachToolTip("Sundouleia's Cache is --COL--self-regulated--COL-- for downloaded mod files." +
            "--NL--It helps improve performance when loading mods and reduces download requirements." +
            "--SEP--Cleans are ran regularily to remove any files unused for 6+ weeks to keep things tidy!", CkColor.VibrantPink.Vec4());
    }

    public void DrawCacheMonitoring(bool showRescan, bool showPenumbraControls, bool showSundouleiaControls)
    {
        CkGui.FontText("Cache Monitoring", UiFontService.UidFont);

        CkGui.FramedIconText(FAI.BarsProgress);
        CkGui.TextFrameAlignedInline("Scanner Status:");
        if (!_monitor.IsScanRunning)
        {
            CkGui.ColorTextFrameAlignedInline("Idle", _config.Current.InitialScanComplete ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey, false);
            CkGui.ColorTextFrameAlignedInline($"(Scanned {_monitor.ScannedCacheEntities} files in {_monitor.LastScanReadStr}, created entries in {_monitor.LastScanWriteStr})", ImGuiColors.DalamudGrey2);
            if (_config.Current.InitialScanComplete && showRescan)
            {
                ImGui.SameLine(ImGui.GetContentRegionAvail().X - CkGui.IconTextButtonSize(FAI.Play, "Rescan"));
                if (CkGui.IconTextButton(FAI.Play, "Rescan"))
                    _monitor.InvokeScan();
            }
        }
        else
        {
            CkGui.ColorTextFrameAlignedInline("Progress:", ImGuiColors.DalamudGrey, true);
            var scanText = _monitor.TotalFiles is 1 ? "Collecting Files..." : $"Processing {_monitor.ScanProgressString} from storage ({_monitor.TotalFiles} scanned in)";
            CkGui.ColorTextFrameAlignedInline(scanText, ImGuiColors.DalamudYellow, false);
        }

        var allWatchersValid = _mainWatcher.Watcher is not null && _penumbraWatcher.Watcher is not null;
        var watcherControlWidth = CkGui.IconTextButtonSize(FAI.Play, "Restart") + ImUtf8.ItemInnerSpacing.X + CkGui.IconTextButtonSize(FAI.StopCircle, "Stop");

        var monitoringPenumbra = string.IsNullOrEmpty(_monitor.PenumbraPath);
        var penumbraPathCol = monitoringPenumbra ? ImGuiColors.DalamudGrey3 : ImGuiColors.ParsedGold.Darken(.5f);
        CkGui.FramedIconText(FAI.GlobeAsia);
        CkGui.TextFrameAlignedInline("Penumbra Monitor:");
        ImGui.SameLine();
        CkGui.TagLabelTextFrameAligned(_monitor.PenumbraPath ?? "Not Monitoring", penumbraPathCol, 3 * ImGuiHelpers.GlobalScale);

        if (showPenumbraControls)
            DrawPenumbraWatcherControls(watcherControlWidth, allWatchersValid);

        // Sundouliea Cache Monitor.
        var monitoringSundouleia = !string.IsNullOrEmpty(_monitor.SundeouleiaPath);
        var mainCacheCol = monitoringSundouleia ? ImGuiColors.ParsedGold.Darken(.5f) : ImGuiColors.DalamudGrey3;
        CkGui.FramedIconText(FAI.Key);
        CkGui.TextFrameAlignedInline("Sundouleia Monitor:");
        ImGui.SameLine();
        CkGui.TagLabelTextFrameAligned(_monitor.SundeouleiaPath ?? "Not Monitoring", mainCacheCol, 3 * ImGuiHelpers.GlobalScale);

        if (showSundouleiaControls)
            DrawSundouleiaWatcherControls(watcherControlWidth, allWatchersValid);
    }

    private void DrawPenumbraWatcherControls(float offsetX, bool allWatchersValid)
    {
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - offsetX);
        if (CkGui.IconTextButton(FAI.Play, "Restart", disabled: _penumbraWatcher.Watcher is not null, id: "penMonitor"))
        {
            _penumbraWatcher.StartWatcher(IpcCallerPenumbra.ModDirectory);
            if (allWatchersValid)
                _monitor.InvokeScan();
        }
        CkGui.AttachToolTip("Restarts the penumbra watcher and invokes a full scan over both directories if both are valid." +
            "--NL--If the button remains present after clicking it, consult /xllog for errors");

        ImUtf8.SameLineInner();
        if (CkGui.IconTextButton(FAI.StopCircle, "Stop", disabled: _penumbraWatcher.Watcher is null || !KeyMonitor.CtrlPressed()))
            _penumbraWatcher.StopMonitoring();
        CkGui.AttachToolTip("Halts monitoring Penumbra Storage. While disabled, files will not sync.--NL--Hold CTRL to enable this button" +
            "--SEP----COL--Unless you are changing cache folders, do not stop monitoring!--COL--", ImGuiColors.DalamudRed);
    }

    private void DrawSundouleiaWatcherControls(float offsetX, bool allWatchersValid)
    {
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - offsetX);
        if (CkGui.IconTextButton(FAI.Play, "Restart", disabled: _penumbraWatcher.Watcher is not null, id: "penMonitor"))
        {
            _mainWatcher.StartWatcher(_config.Current.CacheFolder);
            if (allWatchersValid)
                _monitor.InvokeScan();
        }
        CkGui.AttachToolTip("Restarts the Sundouleia watcher and invokes a full scan over both directories if both are valid." +
            "--NL--If the button remains present after clicking it, consult /xllog for errors");
        ImUtf8.SameLineInner();
        if (CkGui.IconTextButton(FAI.StopCircle, "Stop", disabled: _penumbraWatcher.Watcher is null || !KeyMonitor.CtrlPressed()))
            _mainWatcher.StopMonitoring();
        CkGui.AttachToolTip("Halts monitoring Sundouleia Storage. While disabled, files will not sync.--NL--Hold CTRL to enable this button" +
            "--SEP----COL--Unless you are changing cache folders, do not stop monitoring!--COL--", ImGuiColors.DalamudRed);
    }

    public void DrawFileCompactor(bool allowCompactOperations)
    {
        CkGui.FontText("File Compactor", UiFontService.UidFont);

        CkGui.FramedIconText(FAI.Hdd);
        CkGui.TextFrameAlignedInline("Utilized Storage:");
        var hasData = _monitor.FileCacheSize >= 0;
        var text = hasData ? SundouleiaEx.ByteToString(_monitor.FileCacheSize) : "Calculating...";
        CkGui.ColorTextFrameAlignedInline(text, hasData ? ImGuiColors.TankBlue : ImGuiColors.DalamudGrey);

        CkGui.FramedIconText(FAI.Hdd);
        CkGui.TextFrameAlignedInline("Drive Capacity Remaining:");
        CkGui.ColorTextFrameAlignedInline(SundouleiaEx.ByteToString(_monitor.FileCacheDriveFree), ImGuiColors.TankBlue);

        // File compactor option stuff.
        var useCompactor = _config.Current.CompactCache;
        if (!useCompactor && !_isLinux)
            CkGui.ColorTextWrapped("Free up space when using Sundouleia by enabling the File Compactor", ImGuiColors.DalamudYellow);

        // If we are on linux or our drive does not support NewTechnologyFileSystem (NTFS), we cannot use the file compactor.
        var canUseCompactor = !_isLinux && _mainWatcher.StorageisNTFS;
        using (ImRaii.Disabled(!canUseCompactor))
            if (ImGui.Checkbox("Use Compactor", ref useCompactor))
            {
                _config.Current.CompactCache = useCompactor;
                _config.Save();
            }
        CkGui.AttachToolTip("Sundouleia's File Compactor helps compress any downloaded mod files by a large amount." +
            "--NL----COL--It might incur a minor penalty on loading files on a slow CPU.--COL--" +
            "--NL--It is recommended to leave it enabled to save on space.", ImGuiColors.DalamudYellow);

        // Only display the compacting options if allowed.
        if (!_compactor.MassCompactRunning)
        {
            if (allowCompactOperations)
            {
                ImGui.SameLine();
                if (CkGui.IconTextButton(FAI.FileArchive, "Compact all files in storage"))
                {
                    _ = Task.Run(() =>
                    {
                        _compactor.CompactStorage();
                        _monitor.RecalculateFileCacheSize(CancellationToken.None);
                    });
                }
                CkGui.AttachToolTip("Run compression on all files in the Sundouleia Cache." +
                    "--NL--This doesn't need to be ran if the file compactor is kept active!");

                ImGui.SameLine();
                if (CkGui.IconTextButton(FAI.File, "Decompact Sundouleia Cache files."))
                {
                    _ = Task.Run(() =>
                    {
                        _compactor.DecompactStorage();
                        _monitor.RecalculateFileCacheSize(CancellationToken.None);
                    });
                }
                CkGui.AttachToolTip("Runs a decompression across all Sundouleia Cache files.");
            }
        }
        else
        {
            CkGui.ColorTextInline($"Compactor currently running: ({_compactor.Progress})", ImGuiColors.DalamudYellow);
        }

        // Inform client that the compactor is inaccessible they do not meet the conditions for it.
        if (_isLinux || !_monitor.StorageIsNTFS)
            ImGui.Text("The file compactor is only available on Windows and NTFS drives.");
    }


    public void DrawTransfers()
    {
        CkGui.FontText("File Transfer Monitor", UiFontService.UidFont);

        using var _ = ImRaii.Table("transfer monitor", 2, ImGuiTableFlags.SizingFixedSame | ImGuiTableFlags.BordersInnerV);
        if (!_)
            return;

        ImGui.TableSetupColumn("Uploads");
        ImGui.TableSetupColumn("Downloads");
        ImGui.TableHeadersRow();

        ImGui.TableNextColumn();
        DrawUploads();

        ImGui.TableNextColumn();
        DrawDownloads();
    }

    private void DrawUploads()
    {
        var uploads = _uploader.CurrentUploads;
        if (uploads.TotalFiles > 0)
        {
            var doneUploads = uploads.FilesCompleted;
            var totalUploads = uploads.TotalFiles;

            var totalUploaded = uploads.Transferred;
            var totalToUpload = uploads.TotalSize;

            CkGui.TextFrameAligned($"Uploading {doneUploads}/{totalUploads} Files");
            CkGui.TextFrameAligned($"Progress: {SundouleiaEx.ByteToString(totalUploaded)}/{SundouleiaEx.ByteToString(totalToUpload)}");
        }
        else
        {
            CkGui.CenterTextAligned("No uploads in progress");
        }
    }

    private void DrawDownloads()
    {
        if (TransferBarUI.Downloads.Count <= 0)
        {
            CkGui.CenterTextAligned("No downloads in progress");
            return;
        }
        else
        {
            foreach (var (pair, pairDLs) in TransferBarUI.Downloads)
            {
                var doneDLs = pairDLs.FilesCompleted;
                var totalDLs = pairDLs.TotalFiles;
                var totalBytesDLed = pairDLs.Transferred;
                var totalBytesToDL = pairDLs.TotalSize;
                CkGui.ColorText($"{pair.NameString} ({pair.Sundesmo.GetNickAliasOrUid()})", ImGuiColors.TankBlue);
                CkGui.FrameSeparatorV();
                CkGui.TextFrameAlignedInline($"Downloading {doneDLs}/{totalDLs} Files");
                CkGui.FrameSeparatorV();
                CkGui.TextFrameAlignedInline($"Progress: {SundouleiaEx.ByteToString(totalBytesDLed)}/{SundouleiaEx.ByteToString(totalBytesToDL)}");
                ImGui.Separator();
            }
        }
    }

    private void OpenDialogBox()
    {
        _dialogService.OpenFolderPicker("Pick Sundouleia's Cache Folder", (success, path) =>
        {
            // Ensure dialog success is yippee 
            if (!success)
                return;

            // Need to validate that the selected path is a valid path prior to setting it.
            if (path.Contains("onedrive", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning($"User attempted to set cache path to OneDrive folder: {path}");
                _pathValidation = FilePathValidation.IsOneDrive;
                return;
            }
            if (string.Equals(path.ToLowerInvariant(), IpcCallerPenumbra.ModDirectory?.ToLowerInvariant(), StringComparison.Ordinal))
            {
                _logger.LogWarning($"User attempted to set cache path to Penumbra Mod Directory: {path}");
                _pathValidation = FilePathValidation.IsPenumbraDir;
                return;
            }
            var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (fileName.Length != 64 && !string.Equals(fileName, "desktop", StringComparison.OrdinalIgnoreCase))
                {
                    _pathValidation = FilePathValidation.HasOtherFiles;
                    _logger.LogWarning($"Found illegal file in {path}: {file}");
                    return;
                }
            }
            var dirs = Directory.GetDirectories(path);
            if (dirs.Any())
            {
                _pathValidation = FilePathValidation.HasOtherFiles;
                _logger.LogWarning($"Found folders in {path} not belonging to Sundouleia: {string.Join(", ", dirs)}");
                return;
            }

            if (!IsDirectoryWritable(path))
            {
                _pathValidation = FilePathValidation.NotWritable;
                return;
            }

            // Validate the format of the path.
            if (!ValidPathRegex().IsMatch(path))
            {
                _pathValidation = FilePathValidation.InvalidPath;
                return;
            }

            // Validate the existence of the path.
            if (IsValidPath(path))
            {
                _pathValidation = FilePathValidation.Valid;
                _config.Current.CacheFolder = path;
                _config.Save();
                _mainWatcher.StartWatcher(path);
                _monitor.InvokeScan();
            }
        }, _rootPath, true);
    }

    private bool IsValidPath(string path) => !string.IsNullOrEmpty(path) && Directory.Exists(path);
    private bool CachePresetRootDirExists()
    {
        var sundouleiaCacheDir = Path.Combine(_rootPath, "SundouleiaCache");
        return Directory.Exists(sundouleiaCacheDir);
    }

    private bool CachePresetPenumbraParentDirExists()
    {
        if (string.IsNullOrEmpty(IpcCallerPenumbra.ModDirectory)) 
            return false;
        // grab the root path.
        if (Path.GetPathRoot(IpcCallerPenumbra.ModDirectory!) is not { } driveRoot)
            return false;
        // Grab the relative path from the root.
        var relativePath = Path.GetRelativePath(driveRoot, IpcCallerPenumbra.ModDirectory!);
        // get the directory name of the mod directory.
        if (Path.GetDirectoryName(IpcCallerPenumbra.ModDirectory!) is not { } penumbraParentDir)
            return false;
        // create the new path at this location.
        var sundouleiaCacheDir = Path.Combine(penumbraParentDir, "SundouleiaCache");
        return Directory.Exists(sundouleiaCacheDir);

    }

    private string CreateRootDirCachePreset()
    {
        var sundouleiaCacheDir = Path.Combine(_rootPath, "SundouleiaCache");
        Directory.CreateDirectory(sundouleiaCacheDir);
        return sundouleiaCacheDir;
    }

    private string CreatePenumbraParentDirCachePreset()
    {
        if (string.IsNullOrEmpty(IpcCallerPenumbra.ModDirectory))
            return string.Empty;
        // grab the root path.
        if (Path.GetPathRoot(IpcCallerPenumbra.ModDirectory!) is not { } driveRoot)
            return string.Empty;
        // Grab the relative path from the root.
        var relativePath = Path.GetRelativePath(driveRoot, IpcCallerPenumbra.ModDirectory!);
        // get the directory name of the mod directory.
        if (Path.GetDirectoryName(IpcCallerPenumbra.ModDirectory!) is not { } penumbraParentDir)
            return string.Empty;
        // create the new path at this location.
        var sundouleiaCacheDir = Path.Combine(penumbraParentDir, "SundouleiaCache");
        // Create a directory at this location.
        Directory.CreateDirectory(sundouleiaCacheDir);
        return sundouleiaCacheDir;
    }


    // Attempt to write a file to a specified directory to check if it is indeed writable.
    private bool IsDirectoryWritable(string dir)
    {
        try
        {
            var testFile = Path.Combine(dir, Path.GetRandomFileName());
            using FileStream fs = File.Create(testFile, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [GeneratedRegex(@"^(?:[a-zA-Z]:\\[\w\s\-\\]+?|\/(?:[\w\s\-\/])+?)$", RegexOptions.ECMAScript, 5000)]
    private static partial Regex ValidPathRegex();
}
