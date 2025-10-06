using CkCommons.Gui;
using CkCommons.Helpers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.ModFiles.Cache;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using System.IO;
using System.Text.RegularExpressions;
using TerraFX.Interop.Windows;

namespace Sundouleia.Gui;

public partial class ModStorageTab
{
    private readonly ILogger<ModStorageTab> _logger;
    private readonly MainConfig _config;
    private readonly FileCompactor _compactor;
    private readonly CacheMonitor _monitor;
    private readonly SundouleiaWatcher _mainWatcher;
    private readonly PenumbraWatcher _penumbraWatcher;
    private readonly UiFileDialogService _dialogService;

    // Cache location validators.
    private bool _isDirWritable = false;
    private bool _isOneDrive = false;
    private bool _isPenumbraDir = false;
    private bool _hasOtherFiles = false;
    private bool _isValidPath = true;

    public ModStorageTab(ILogger<ModStorageTab> logger, MainConfig config, FileCompactor compactor,
        CacheMonitor monitor, SundouleiaWatcher mainWatcher, PenumbraWatcher penumbraWatcher,
        UiFileDialogService dialogService)
    {
        _logger = logger;
        _config = config;
        _compactor = compactor;
        _monitor = monitor;
        _mainWatcher = mainWatcher;
        _penumbraWatcher = penumbraWatcher;
        _dialogService = dialogService;
    }

    public void DrawModStorage()
    {
        CkGui.FontText("Storage", UiFontService.UidFont);

        CkGui.TextWrapped("Sundouleia's File storage is a self-regulated cache for downloaded mod files.\n" +
            "The Cache exists to improve performance when loading mods from others and reduces how often you need to download.\n" +
            "Sundouleia's Cache runs a cleanup on any cached files not accessed in 6 weeks upon each statup in the background.");

        CkGui.TextWrapped("Additionally, exceeding the set storage size will automatically trigger a cleanup of your oldest files.");

        // Draw out the file scanner state.
        CkGui.TextWrapped("Set the storage size accordingly.");
        DrawFileScanState();

        // For Penumbra Monitoring
        CkGui.TextFrameAligned("Monitoring Penumbra Folder: " + (_monitor.PenumbraPath ?? "Not monitoring"));
        if (string.IsNullOrEmpty(_monitor.PenumbraPath))
        {
            ImGui.SameLine();
            if (CkGui.IconTextButton(FontAwesomeIcon.ArrowsToCircle, "Attempt Reinitializing Monitor", id: "penMonitor"))
                _penumbraWatcher.StartWatcher(IpcCallerPenumbra.ModDirectory);
        }

        CkGui.TextFrameAligned("Monitoring Sundouleia Storage: " + (_monitor.SundeouleiaPath ?? "Not monitoring"));
        if (string.IsNullOrEmpty(_monitor.SundeouleiaPath))
        {
            ImGui.SameLine();
            if (CkGui.IconTextButton(FontAwesomeIcon.ArrowsToCircle, "Attempt Reinitializing Monitor", id: "sunMonitor"))
                _mainWatcher.StartWatcher(_config.Current.CacheFolder);
        }

        // If either watchers are null, prompt an option to resume monitors.
        if (_mainWatcher.Watcher is null || _penumbraWatcher.Watcher is null)
        {
            if (CkGui.IconTextButton(FontAwesomeIcon.Play, "Resume Monitoring"))
            {
                _mainWatcher.StartWatcher(_config.Current.CacheFolder);
                _penumbraWatcher.StartWatcher(IpcCallerPenumbra.ModDirectory);
                _monitor.InvokeScan();
            }
            CkGui.AttachToolTip("Attempts to resume monitoring for both Penumbra and Sundouleia Storage." + 
                "--NL--Resuming the monitoring will also force a full scan to run." + 
                "--NL--If the button remains present after clicking it, consult /xllog for errors");
        }
        else
        {
            if (CkGui.IconTextButton(FontAwesomeIcon.Stop, "Stop Monitoring", disabled: !KeyMonitor.CtrlPressed()))
            {
                _mainWatcher.StopMonitoring();
                _penumbraWatcher.StopMonitoring();
            }
            CkGui.AttachToolTip("Stops the monitoring for both Penumbra and Sundouleia Storage. " + 
                "--NL--Do not stop the monitoring, unless you plan to move the Penumbra and Sundouleia folders, to ensure correct functionality of Sundouleia." + 
                "--NL--If you stop the monitoring to move folders around, resume it after you are finished moving the files." +
                "--SEP--Hold CTRL to enable this button");
        }

        // Allow the client to define the location of the cache directory. We can use our own file dialog serivce for this.
        DrawCacheDirectorySetting();

        // If our caches file size isn't -1 we can display the current size of the cache.
        var hasData = _monitor.FileCacheSize >= 0;
        var text = hasData ? SundouleiaEx.ByteToString(_monitor.FileCacheSize) : "Calculating...";
        CkGui.TextFrameAligned($"Utilized storage: {text}");

        // Display remaining free space left.
        ImGui.Text($"Remaining space on drive: {SundouleiaEx.ByteToString(_monitor.FileCacheDriveFree)}");

        // File compactor option stuff.
        var useCompactor = _config.Current.CompactCache;
        var linux = Util.IsWine();
        if (!useCompactor && !linux)
            CkGui.ColorTextWrapped("Free up space when using Sundouleia by enabling the File Compactor", ImGuiColors.DalamudYellow);

        // If we are on linux or our drive does not support NewTechnologyFileSystem (NTFS), we cannot use the file compactor.
        var canUseCompactor = !linux && _mainWatcher.StorageisNTFS;
        using (ImRaii.Disabled(!canUseCompactor))
            if (ImGui.Checkbox("Use Compactor", ref useCompactor))
            {
                _config.Current.CompactCache = useCompactor;
                _config.Save();
            }
        CkGui.AttachToolTip("Sundouleia's File Compactor helps compress any downloaded mod files by a large ammount." +
            "--NL----COL--It might incur a minor penalty on loading files on a slow CPU.--COL--" +
            "--NL--It is recommended to leave it enabled to save on space.", ImGuiColors.DalamudYellow);

        ImGui.SameLine();
        if (!_compactor.MassCompactRunning)
        {
            if (CkGui.IconTextButton(FontAwesomeIcon.FileArchive, "Compact all files in storage"))
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
            if (CkGui.IconTextButton(FontAwesomeIcon.File, "Decompact Sundouleia Cache files."))
            {
                _ = Task.Run(() =>
                {
                    _compactor.DecompactStorage();
                    _monitor.RecalculateFileCacheSize(CancellationToken.None);
                });
            }
            CkGui.AttachToolTip("Runs a decompression across all Sundouleia Cache files.");
        }
        else
        {
            CkGui.ColorText($"Compactor currently running: ({_compactor.Progress})", ImGuiColors.DalamudYellow);
        }

        // Inform client that the compactor is inaccessible they do not meet the conditions for it.
        if (linux || !_monitor.StorageIsNTFS)
            ImGui.Text("The file compactor is only available on Windows and NTFS drives.");

        // Shift & Split
        ImGuiHelpers.ScaledDummy(new Vector2(10, 10));
        ImGui.Separator();

        // We could add a slider that lets us clear the storage up to a certain size honestly, it doesnt have to be the whole damn thing.
        CkGui.TextWrapped("Sundouleia Storage validation makes sure all files in the cache are valid.\n" +
            "Run the validation before you clear the Storage for no reason!\n" +
            "Depending on how large the cache is, this may take a bit!");

        // Can add validation here later if we feel like it but im kinda meh about it right now.
        // Would be better if we could just select how much we want to delete and such honestly, the old method was too all-or-nothing.
    }

    private void DrawFileScanState()
    {
        CkGui.TextFrameAligned("File Scanner Status:");        
        if (_monitor.IsScanRunning)
        {
            CkGui.TextFrameAlignedInline("Scan is running");
            // next line, no frame align.
            ImGui.TextUnformatted("Current Progress:");
            CkGui.TextInline(_monitor.TotalFiles is 1 ? "Collecting files" : $"Processing {_monitor.ScanProgressString} from storage ({_monitor.TotalFiles} scanned in)");
            CkGui.AttachToolTip("--COL--Note:--COL-- It's possible to have more files in storage than scanned in." +
                "--NL--This is due to the scanner ignoring files that are processed by the game currently, which are appended into the local storage.", ImGuiColors.TankBlue);
        }
        else
        {
            CkGui.TextInline("Idle");
            if (!_config.Current.InitialScanComplete)
                return;
            // Show force rescan if we have already done an initial scan.
            ImGui.SameLine();
            if (CkGui.IconTextButton(FontAwesomeIcon.Play, "Force rescan"))
                _monitor.InvokeScan();
        }
    }

    public void DrawCacheDirectorySetting()
    {
        CkGui.ColorTextWrapped("Note: The storage folder should be somewhere close to root (i.e. C:\\SundouleiaFiles) in a new empty folder.\n" +
            "DO NOT point this to your game or Penumbra folder.", ImGuiColors.DalamudYellow);

        // Identify chosen directory.
        var currentDir = _config.Current.CacheFolder;
        // Display the directory in a readonly input text so that we can open the file dailog for selection.
        ImGui.InputText("FileCache Folder##cache", ref currentDir, 255, ImGuiInputTextFlags.ReadOnly);

        ImGui.SameLine();
        if (CkGui.IconButton(FontAwesomeIcon.Folder, disabled: _mainWatcher.Watcher is not null))
        {
            _dialogService.OpenFolderPicker("Pick Sundouleia's Cache Folder", (success, path) =>
            {
                // Ensure dialog success is yippee 
                if (!success) return;

                // Need to validate that the selected path is a valid path prior to setting it.
                _isOneDrive = path.Contains("onedrive", StringComparison.OrdinalIgnoreCase);
                _isPenumbraDir = string.Equals(path.ToLowerInvariant(), IpcCallerPenumbra.ModDirectory?.ToLowerInvariant(), StringComparison.Ordinal);
                
                var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                _hasOtherFiles = false;
                foreach (var file in files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    if (fileName.Length != 40 && !string.Equals(fileName, "desktop", StringComparison.OrdinalIgnoreCase))
                    {
                        _hasOtherFiles |= true;
                        _logger.LogWarning($"Found illegal file in {path}: {file}");
                        break;
                    }
                }
                var dirs = Directory.GetDirectories(path);
                if (dirs.Any())
                {
                    _hasOtherFiles = true;
                    _logger.LogWarning($"Found folders in {path} not belonging to Mare: {string.Join(", ", dirs)}");
                }

                _isDirWritable = IsDirectoryWritable(path);
                _isValidPath = PathRegex().IsMatch(path);

                if (!IsValidPath(path))
                {
                    _config.Current.CacheFolder = path;
                    _config.Save();
                    _mainWatcher.StartWatcher(path);
                    _monitor.InvokeScan();
                }
            }, Util.IsWine() ? @"Z:\" : @"C:\", true);
        }
        if (_mainWatcher.Watcher is not null)
            CkGui.AttachToolTip("Must stop monitoring the cache folder before changing it.");

        if (_isPenumbraDir)
            CkGui.ColorTextWrapped("Path cannot be the penumbra directory!", ImGuiColors.DalamudRed);
        else if (_isOneDrive)
            CkGui.ColorTextWrapped("Path cannot be in OneDrive Try putting it closer to a root (C:FFXIVModding\\)!", ImGuiColors.DalamudRed);
        else if (!_isDirWritable)
            CkGui.ColorTextWrapped("Path must be writable!", ImGuiColors.DalamudRed);
        else if (_hasOtherFiles)
            CkGui.ColorTextWrapped("Directory has files or other sub-folders not beloning to Sundouleia!", ImGuiColors.DalamudRed);
        else if (!_isValidPath)
            CkGui.ColorTextWrapped("The selected directory contains illegal characters unreadable by FFXIV.\n" +
                "Restrict yourself to latin letters (A-Z), underscores (_), dashes (-) and arabic numbers (0-9).", ImGuiColors.DalamudRed);

        // Slider for the maximum size.
        float maxCacheSize = (float)_config.Current.MaxCacheInGiB;
        if (ImGui.SliderFloat("Maximum Storage Size in GiB", ref maxCacheSize, 1f, 200f, "%.2f GiB"))
        {
            _config.Current.MaxCacheInGiB = maxCacheSize;
            _config.Save();
        }
        CkGui.HelpText("Storage is governed by Sundouleia, and clears itself upon reaching capacity, or after a file has not been accessed for 6 weeks.");
    }

    private bool IsValidPath(string path)
        => !string.IsNullOrEmpty(path) && Directory.Exists(path) && _isDirWritable && !_isPenumbraDir && !_isOneDrive && !_hasOtherFiles && _isValidPath;

    // Attempt to write a file to a spesified directory to check if it is indeed writable.
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
    private static partial Regex PathRegex();
}
