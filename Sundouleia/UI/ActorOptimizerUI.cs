using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;

// Doing the hecking brainstorm voodoo

namespace Sundouleia.Gui;

// Redesign a lot of this after it runs. Primarily copied over for an initial look at what it used to look like.
// Much of this functionality is excessive, not needed, and can be cleaned up for a more polished look
// that gives the right information while not overwhelming the end-user.


// One of the main, really beneficial things about Sundouleia is that your modded file updates do not transfer your actors
// modded files all at once. They are pushed as they are processed, and only bump what was added, or what was removed.
// Additionally, modded updates are pushed in 2 batches, where the 2nd has you only update what is not present on the server.
// Because of this, the old 'DataAnalyzer' should be more like a 'ActorOptimizer' UI, serving primarily as a compression utility tool.
public class ActorOptimizerUI : WindowMediatorSubscriberBase
{
    private readonly TransientCacheConfig _config;
    private readonly ActorAnalyzer _analyzer;
    private readonly IpcManager _ipc;
    private readonly ModdedStateManager _manager;

    private readonly Progress<(string, int)> _conversionProgress = new();
    private readonly Dictionary<string, string[]> _texturesToConvert = new(StringComparer.Ordinal);

    // The last cached analysis on our client actor(s).
    private Dictionary<OwnedObject, Dictionary<string, FileDataEntry>>? _cachedAnalysis;

    private Task? _conversionTask;
    private CancellationTokenSource _conversionCTS = new();

    private OwnedObject _currentTab = OwnedObject.Player;
    private string _conversionCurrentFileName = string.Empty;
    private int _conversionCurrentFileProgress = 0;
    private bool _enableBc7ConversionMode = false;
    private bool _hasUpdate = false;
    private bool _modalOpen = false;
    private string _selectedFileTypeTab = string.Empty;
    private string _selectedHash = string.Empty;
    private bool _showModal = false;

    public ActorOptimizerUI(ILogger<ActorOptimizerUI> logger, SundouleiaMediator mediator,
        ActorAnalyzer analyzer, IpcManager ipc, ModdedStateManager manager, TransientCacheConfig config)
        : base(logger, mediator, "Client Actor(s) Optimizer")
    {
        _config = config;
        _analyzer = analyzer;
        _ipc = ipc;
        _manager = manager;

        this.SetBoundaries(new(800, 600), ImGui.GetIO().DisplaySize);

        Mediator.Subscribe<ActorAnalyzedMessage>(this, _ => _hasUpdate = true);
        // Track progress changes for BC7 conversion
        _conversionProgress.ProgressChanged += ConversionProgress_ProgressChanged;
    }

    protected override void PreDrawInternal()
    { }

    protected override void DrawInternal()
    {
        if (_conversionTask != null && !_conversionTask.IsCompleted)
        {
            _showModal = true;
            if (ImGui.BeginPopupModal("BC7 Conversion in Progress"))
            {
                ImGui.TextUnformatted("BC7 Conversion in progress: " + _conversionCurrentFileProgress + "/" + _texturesToConvert.Count);
                CkGui.TextWrapped("Current file: " + _conversionCurrentFileName);
                if (CkGui.IconTextButton(FontAwesomeIcon.StopCircle, "Cancel conversion"))
                    _conversionCTS.SafeCancel();

                CkGui.SetScaledWindowSize(500);
                ImGui.EndPopup();
            }
            else
            {
                _modalOpen = false;
            }
        }
        else if (_conversionTask != null && _conversionTask.IsCompleted && _texturesToConvert.Count > 0)
        {
            _conversionTask = null;
            _texturesToConvert.Clear();
            _showModal = false;
            _modalOpen = false;
            _enableBc7ConversionMode = false;
        }

        if (_showModal && !_modalOpen)
        {
            ImGui.OpenPopup("BC7 Conversion in Progress");
            _modalOpen = true;
        }

        ImGui.Text($"{_analyzer.LastAnalysis.Count} data");

        // Not sure why we dont just keep the last analysis considering it only updates when we ask it too but ok.
        if (_hasUpdate)
        {
            _cachedAnalysis = _analyzer.LastAnalysis.DeepClone();
            _hasUpdate = false;
            // If the new analysis no longer has the selected tab, revert it to player.
            if (!_cachedAnalysis.ContainsKey(_currentTab))
                _currentTab = OwnedObject.Player;
        }

        using var tabBar = ImRaii.TabBar("analysisRecordingTabBar");
        using (var tabItem = ImRaii.TabItem("Analysis"))
        {
            if (tabItem)
            {
                using var id = ImRaii.PushId("analysis");
                DrawAnalysis();
            }
        }
        using (var tabItem = ImRaii.TabItem("Transient Data"))
        {
            if (tabItem)
            {
                using var id = ImRaii.PushId("data");
                DrawStoredData();
            }
        }
    }

    protected override void PostDrawInternal()
    { }

    private string _selectedStoredCharacter = string.Empty;
    private string _selectedJobEntry = string.Empty;
    private readonly List<string> _storedPathsToRemove = [];
    private readonly Dictionary<string, string> _filePathResolve = [];
    private string _filterGamePath = string.Empty;
    private string _filterFilePath = string.Empty;

    private void DrawStoredData()
    {
        var config = _config.Current.PlayerCaches;
        Vector2 availableContentRegion = Vector2.Zero;
        using (ImRaii.Group())
        {
            ImGui.TextUnformatted("Character");
            ImGui.Separator();
            ImGuiHelpers.ScaledDummy(3);
            availableContentRegion = ImGui.GetContentRegionAvail();
            using (ImRaii.ListBox("##characters", new Vector2(200, availableContentRegion.Y)))
            {
                foreach (var (nameWorld, caches) in config)
                {
                    var name = nameWorld.Split("_");
                    if (!GameDataSvc.WorldData.TryGetValue(ushort.Parse(name[1]), out var worldname))
                        continue;

                    if (ImGui.Selectable($"{name[0]} ({worldname})", string.Equals(_selectedStoredCharacter, nameWorld, StringComparison.Ordinal)))
                    {
                        _selectedStoredCharacter = nameWorld;
                        _selectedJobEntry = string.Empty;
                        _storedPathsToRemove.Clear();
                        _filePathResolve.Clear();
                        _filterFilePath = string.Empty;
                        _filterGamePath = string.Empty;
                    }
                }
            }
        }
        ImGui.SameLine();
        bool selectedData = config.TryGetValue(_selectedStoredCharacter, out var transientStorage) && transientStorage != null;
        using (ImRaii.Group())
        {
            ImGui.TextUnformatted("Job");
            ImGui.Separator();
            ImGuiHelpers.ScaledDummy(3);
            using (ImRaii.ListBox("##data", new Vector2(150, availableContentRegion.Y)))
            {
                if (selectedData)
                {
                    if (ImGui.Selectable("All Jobs", string.Equals(_selectedJobEntry, "alljobs", StringComparison.Ordinal)))
                        _selectedJobEntry = "alljobs";

                    foreach (var job in transientStorage!.JobBasedCache)
                    {
                        if (!GameDataSvc.JobData.TryGetValue(job.Key, out var jobName))
                            continue;
                        
                        if (ImGui.Selectable(jobName, string.Equals(_selectedJobEntry, job.Key.ToString(), StringComparison.Ordinal)))
                        {
                            _selectedJobEntry = job.Key.ToString();
                            _storedPathsToRemove.Clear();
                            _filePathResolve.Clear();
                            _filterFilePath = string.Empty;
                            _filterGamePath = string.Empty;
                        }
                    }
                }
            }
        }
        ImGui.SameLine();
        using (ImRaii.Group())
        {
            var selectedList = string.Equals(_selectedJobEntry, "alljobs", StringComparison.Ordinal)
                ? config[_selectedStoredCharacter].PersistentCache
                : (string.IsNullOrEmpty(_selectedJobEntry) ? [] : config[_selectedStoredCharacter].JobBasedCache[uint.Parse(_selectedJobEntry)]);
            
            ImGui.TextUnformatted($"Attached Files (Total Files: {selectedList.Count})");
            ImGui.Separator();
            ImGuiHelpers.ScaledDummy(3);
            using (ImRaii.Disabled(string.IsNullOrEmpty(_selectedJobEntry)))
            {

                var restContent = availableContentRegion.X - ImGui.GetCursorPosX();
                using var group = ImRaii.Group();
                if (CkGui.IconTextButton(FontAwesomeIcon.ArrowRight, "Resolve Game Paths to used File Paths"))
                {
                    _ = Task.Run(async () =>
                    {
                        var paths = selectedList.ToArray();
                        var resolved = await _ipc.Penumbra.ResolveModPaths(paths, []).ConfigureAwait(false);
                        _filePathResolve.Clear();
                        // Rebuild the resolve dictionary
                        for (int i = 0; i < resolved.forward.Length; i++)
                            _filePathResolve[paths[i]] = resolved.forward[i];
                    });
                }

                ImGui.SameLine();
                ImGuiHelpers.ScaledDummy(20, 1);
                ImGui.SameLine();
                
                using (ImRaii.Disabled(!_storedPathsToRemove.Any()))
                {
                    if (CkGui.IconTextButton(FontAwesomeIcon.Trash, "Remove selected Game Paths"))
                    {
                        foreach (var item in _storedPathsToRemove)
                            selectedList.Remove(item);

                        _config.Save();
                        _manager.ReloadPersistentTransients();
                        _filterFilePath = string.Empty;
                        _filterGamePath = string.Empty;
                    }
                }
                ImGui.SameLine();
                using (ImRaii.Disabled(!ImGui.GetIO().KeyCtrl))
                {
                    if (CkGui.IconTextButton(FontAwesomeIcon.Trash, "Clear ALL Game Paths"))
                    {
                        selectedList.Clear();
                        _config.Save();
                        _manager.ReloadPersistentTransients();
                        _filterFilePath = string.Empty;
                        _filterGamePath = string.Empty;
                    }
                }
                CkGui.AttachToolTip("--COL--Hold CTRL--COL-- to remove all game paths from the displayed list." +
                    "--SEP--Animation & VFX data will be handled automatically.", ImGuiColors.DalamudOrange);

                ImGuiHelpers.ScaledDummy(5);
                ImGuiHelpers.ScaledDummy(30);
                ImGui.SameLine();
                ImGui.SetNextItemWidth((restContent - 30) / 2f);
                ImGui.InputTextWithHint("##filterGamePath", "Filter by Game Path", ref _filterGamePath, 255);
                ImGui.SameLine();
                ImGui.SetNextItemWidth((restContent - 30) / 2f);
                ImGui.InputTextWithHint("##filterFilePath", "Filter by File Path", ref _filterFilePath, 255);

                using (var dataTable = ImRaii.Table("##table", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg))
                {
                    if (dataTable)
                    {
                        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 30);
                        ImGui.TableSetupColumn("Game Path", ImGuiTableColumnFlags.WidthFixed, (restContent - 30) / 2f);
                        ImGui.TableSetupColumn("File Path", ImGuiTableColumnFlags.WidthFixed, (restContent - 30) / 2f);
                        ImGui.TableSetupScrollFreeze(0, 1);
                        ImGui.TableHeadersRow();
                        int id = 0;
                        foreach (var entry in selectedList)
                        {
                            if (!string.IsNullOrWhiteSpace(_filterGamePath) && !entry.Contains(_filterGamePath, StringComparison.OrdinalIgnoreCase))
                                continue;
                            bool hasFileResolve = _filePathResolve.TryGetValue(entry, out var filePath);

                            if (hasFileResolve && !string.IsNullOrEmpty(_filterFilePath) && !filePath!.Contains(_filterFilePath, StringComparison.OrdinalIgnoreCase))
                                continue;

                            using var imguiid = ImRaii.PushId(id++);
                            ImGui.TableNextColumn();
                            bool isSelected = _storedPathsToRemove.Contains(entry, StringComparer.Ordinal);
                            if (ImGui.Checkbox("##", ref isSelected))
                            {
                                if (isSelected)
                                    _storedPathsToRemove.Add(entry);
                                else
                                    _storedPathsToRemove.Remove(entry);
                            }
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(entry);
                            CkGui.AttachToolTip($"{entry}--SEP--Click to copy to clipboard");
                            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                                ImGui.SetClipboardText(entry);

                            ImGui.TableNextColumn();
                            if (hasFileResolve)
                            {
                                ImGui.TextUnformatted(filePath ?? "Unk");
                                CkGui.AttachToolTip($"{(filePath ?? "Unk")}--SEP--Click to copy to clipboard");
                                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                                    ImGui.SetClipboardText(filePath);
                            }
                            else
                            {
                                ImGui.TextUnformatted("-");
                                CkGui.AttachToolTip("Resolve Game Paths to used File Paths to display the associated file paths.");
                            }
                        }
                    }
                }
            }
        }
    }

    // Recording was originally to capture played animations or vfx's, but any of these items are trackable with our
    // new penumbra API methods, so it would not be needed.
    //
    // The reason for this is that the new API methods allow us to record all transients performed and store them for
    // comparison against existing items tracked as used.
    // 
    // As such, ignore recording for now, add it in if it is necessary.

    // Analysis overview.
    private void DrawAnalysis()
    {
        CkGui.TextWrapped("This tab shows you all files and their sizes that are currently in use through your character and associated entities.");

        if (_cachedAnalysis!.Count is 0)
            return;

        if (_analyzer.AnalyzingActor)
        {
            CkGui.ColorTextWrapped($"Analyzing {_analyzer.CurrentFile}/{_analyzer.TotalFiles}", ImGuiColors.DalamudYellow);
            if (CkGui.IconTextButton(FontAwesomeIcon.StopCircle, "Cancel analysis"))
                _analyzer.Halt();
        }
        else
        {
            if (_cachedAnalysis!.Any(c => c.Value.Any(f => !f.Value.IsComputed)))
            {
                CkGui.ColorTextWrapped("Some entries have their file size not determined yet, press the button below to analyze current data.", ImGuiColors.DalamudYellow);
                if (CkGui.IconTextButton(FontAwesomeIcon.PlayCircle, "Start analysis (missing entries)"))
                    _ = _analyzer.Compute(print: false);
            }
            else
            {
                if (CkGui.IconTextButton(FontAwesomeIcon.PlayCircle, "Start analysis (recalculate all entries)"))
                    _ = _analyzer.Compute(print: false, recalculate: true);
            }
        }

        ImGui.Separator();

        ImGui.Text("Total files:");
        CkGui.TextInline(_cachedAnalysis!.Values.Sum(c => c.Values.Count).ToString());
        ImGui.SameLine();
        CkGui.HoverIconText(FAI.InfoCircle, CkColor.VibrantPink.Uint());
        if (ImGui.IsItemHovered())
        {
            string text = "";
            var filesOfType = _cachedAnalysis.Values.SelectMany(a => a.Values).GroupBy(f => f.FileType, StringComparer.Ordinal);
            text = string.Join(Environment.NewLine, filesOfType.OrderBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => $"{f.Key}: {f.Count()} files | Size: {SundouleiaEx.ByteToString(f.Sum(v => v.OriginalSize))} " +
                $"| Compressed: {SundouleiaEx.ByteToString(f.Sum(v => v.CompressedSize))}"));
            CkGui.ToolTipInternal(text);
        }

        ImGui.Text($"Total size (actual): {SundouleiaEx.ByteToString(_cachedAnalysis!.Sum(c => c.Value.Sum(c => c.Value.OriginalSize)))}");
        ImGui.Text($"Total size (compressed for up/download only): {SundouleiaEx.ByteToString(_cachedAnalysis!.Sum(c => c.Value.Sum(c => c.Value.CompressedSize)))}");
        ImGui.Text($"Total modded model triangles: {_cachedAnalysis.Sum(c => c.Value.Sum(f => f.Value.Triangles))}");

        ImGui.Separator();
        // Make use of ModdedStates's seperation of files by object to split the current files between each owned actor.
        using var tabbar = ImRaii.TabBar("objectSelection");
        
        foreach (var (ownedObject, dataAnalysis) in _cachedAnalysis)
        {
            using var id = ImRaii.PushId(ownedObject.ToString());
            var tabText = ownedObject.ToString();   
            if (dataAnalysis.Any(f => !f.Value.IsComputed))
                tabText += " (!)";

            using var tab = ImRaii.TabItem(tabText + "###" + ownedObject.ToString());
            if (!tab)
                continue;

            var filesByType = dataAnalysis.Values.GroupBy(f => f.FileType, StringComparer.Ordinal).OrderBy(k => k.Key, StringComparer.Ordinal).ToList();

            ImGui.Text($"Files for {ownedObject}");
            ImGui.SameLine();
            CkGui.HoverIconText(FAI.InfoCircle, CkColor.VibrantPink.Uint());
            if (ImGui.IsItemHovered())
            {
                var text = string.Join('\n', filesByType.Select(f => $"{f.Key}: {f.Count()} files | " +
                    $"Size: {SundouleiaEx.ByteToString(f.Sum(v => v.OriginalSize))} " +
                    $"| Compressed: {SundouleiaEx.ByteToString(f.Sum(v => v.CompressedSize))}"));
                CkGui.ToolTipInternal(text);
            }

            ImGui.Text($"Total Size (actual):");
            CkGui.ColorTextInline(SundouleiaEx.ByteToString(dataAnalysis.Sum(c => c.Value.OriginalSize)), ImGuiColors.DalamudYellow);

            ImGui.Text($"Total Size (compressed for up/download only):");
            CkGui.ColorTextInline(SundouleiaEx.ByteToString(dataAnalysis.Sum(c => c.Value.CompressedSize)), ImGuiColors.DalamudYellow);

            // VRAM calculations are done through original, uncompressed sizes. Please note this means that the individual cannot 'optimize their mods'
            // outside of direct texture compression on the original textures.
            // The server does its own compression when files are in transit, but it ultimate is on the mod creators to make models with more optimized tricounts,
            // as they contribute to the final VRAM calculations that the end user cannot change from the plugin.
            var vRamUsage = filesByType.SingleOrDefault(v => string.Equals(v.Key, "tex", StringComparison.Ordinal));
            if (vRamUsage is not null)
            {
                var actualVramUsage = vRamUsage.Sum(f => f.OriginalSize);
                ImGui.Text($"Total VRAM usage:");
                CkGui.ColorTextInline(SundouleiaEx.ByteToString(actualVramUsage), ImGuiColors.DalamudYellow);
                // Could maybe have some kind of threshold thing here but i'd rather not deal with that right now.
            }

            // Display the actual tri-count for this owned object.
            var actualTriCount = dataAnalysis.Values.Sum(f => f.Triangles);
            ImGui.Text($"{ownedObject} modded model tris");
            CkGui.ColorTextInline($"{actualTriCount}", ImGuiColors.DalamudYellow);
            // Again, could have some kind of threshold warning here but not necessary right now.
            
            ImGui.Separator();
            // Handle tab switching.
            if (_currentTab != ownedObject)
            {
                _selectedHash = string.Empty;
                _currentTab = ownedObject;
                _selectedFileTypeTab = string.Empty;
                _enableBc7ConversionMode = false;
                _texturesToConvert.Clear();
            }

            // Secondary tab bar for seperation by fileTypes.
            using var fileByTypeTabBar = ImRaii.TabBar("fileByTypeTabs");
            foreach (IGrouping<string, FileDataEntry>? fileGroup in filesByType)
            {
                var fileGroupText = $"{fileGroup.Key} [{fileGroup.Count()}]";
                var requiresCompute = fileGroup.Any(k => !k.IsComputed);

                // This is cursed UI design holy moly.
                using var col = ImRaii.PushColor(ImGuiCol.Tab, ImGuiColors.DalamudYellow, requiresCompute);
                if (requiresCompute)
                    fileGroupText += " (!)";

                col.Push(ImGuiCol.Text, 0xFF000000, requiresCompute && !string.Equals(_selectedFileTypeTab, fileGroup.Key, StringComparison.Ordinal));
                using var fileTab = ImRaii.TabItem(fileGroupText + "###" + fileGroup.Key);
                col.Pop();

                if (!fileTab)
                    continue;

                if (!string.Equals(fileGroup.Key, _selectedFileTypeTab, StringComparison.Ordinal))
                {
                    _selectedFileTypeTab = fileGroup.Key;
                    _selectedHash = string.Empty;
                    _enableBc7ConversionMode = false;
                    _texturesToConvert.Clear();
                }

                ImGui.Text($"{fileGroup.Key} files:");
                CkGui.ColorTextInline($"{fileGroup.Count()}", ImGuiColors.DalamudYellow);

                ImGui.Text($"{fileGroup.Key} files size (actual):");
                CkGui.ColorTextInline($"{SundouleiaEx.ByteToString(fileGroup.Sum(c => c.OriginalSize))}", ImGuiColors.DalamudYellow);

                ImGui.Text($"{fileGroup.Key} files size (compressed for up/download only):");
                CkGui.ColorTextInline($"{SundouleiaEx.ByteToString(fileGroup.Sum(c => c.CompressedSize))}", ImGuiColors.DalamudYellow);

                // For textures specifically, allow conversions.
                if (string.Equals(_selectedFileTypeTab, "tex", StringComparison.Ordinal))
                {
                    ImGui.Checkbox("Enable BC7 Conversion Mode", ref _enableBc7ConversionMode);
                    if (_enableBc7ConversionMode)
                    {
                        CkGui.ColorText("WARNING BC7 CONVERSION:", ImGuiColors.DalamudYellow);
                        CkGui.ColorTextInline("Converting textures to BC7 is irreversible!", ImGuiColors.DalamudRed);
                        // BC7 conversion info
                        CkGui.ColorTextWrapped("" +
                            "- Converting textures to BC7 will reduce their size (compressed and uncompressed) drastically. It is recommended to be used for large (4k+) textures.\n" +
                            "- Some textures, especially ones utilizing colorsets, might not be suited for BC7 conversion and might produce visual artifacts.\n" +
                            "- Before converting textures, make sure to have the original files of the mod you are converting so you can reimport it in case of issues.\n" +
                            "- Conversion will convert all found texture duplicates (entries with more than 1 file path) automatically.\n" +
                            "- Converting textures to BC7 is a very expensive operation and, depending on the amount of textures to convert, will take a while to complete."
                        , ImGuiColors.DalamudYellow);
                        // If we have any textures selected to convert, allow us to convert them.
                        if (_texturesToConvert.Count is not 0 && CkGui.IconTextButton(FontAwesomeIcon.PlayCircle, $"Convert {_texturesToConvert.Count} texture(s)"))
                        {
                            _conversionCTS = _conversionCTS.SafeCancelRecreate();
                            _conversionTask = _ipc.Penumbra.ConvertTextureFiles(_texturesToConvert, _conversionProgress, _conversionCTS.Token);
                        }
                    }
                }

                ImGui.Separator();
                // Draw out the table for this file type.
                DrawTable(fileGroup);
            }
        }

        // Individual file details.
        ImGui.Separator();
        ImGui.Text("Selected file:");
        CkGui.ColorTextInline(_selectedHash, ImGuiColors.DalamudYellow);
        if (_cachedAnalysis[_currentTab].TryGetValue(_selectedHash, out FileDataEntry? item))
        {
            var filePaths = item.FilePaths;
            ImGui.Text("Local file path:");
            ImGui.SameLine();
            CkGui.TextWrapped(filePaths[0]);
            // Show other paths if applicable.
            if (filePaths.Count > 1)
            {
                CkGui.TextInline($"(and {filePaths.Count - 1} more)");
                CkGui.HelpText(string.Join('\n', filePaths.Skip(1)), true);
            }

            var gamePaths = item.GamePaths;
            ImGui.Text("Used by game paths:");
            ImGui.SameLine();
            CkGui.TextWrapped(gamePaths[0]);
            if (gamePaths.Count > 1)
            {
                CkGui.TextInline($"(and {gamePaths.Count - 1} more)");
                CkGui.HelpText(string.Join('\n', gamePaths.Skip(1)));
            }
        }
    }

    public override void OnOpen()
    {
        _hasUpdate = true;
        _selectedHash = string.Empty;
        _enableBc7ConversionMode = false;
        _texturesToConvert.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _conversionProgress.ProgressChanged -= ConversionProgress_ProgressChanged;
    }

    private void ConversionProgress_ProgressChanged(object? sender, (string, int) e)
    {
        _conversionCurrentFileName = e.Item1;
        _conversionCurrentFileProgress = e.Item2;
    }

    private void DrawTable(IGrouping<string, FileDataEntry > fileGroup)
    {
        // Colum logic
        var tableColumns = string.Equals(fileGroup.Key, "tex", StringComparison.Ordinal)
            ? (_enableBc7ConversionMode ? 7 : 6)
            : (string.Equals(fileGroup.Key, "mdl", StringComparison.Ordinal) ? 6 : 5);
        
        using var table = ImRaii.Table("Analysis" + fileGroup.Key, tableColumns, ImGuiTableFlags.Sortable | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit, new Vector2(0, 300));
        if (!table.Success)
            return;

        ImGui.TableSetupColumn("Hash");
        ImGui.TableSetupColumn("Filepaths");
        ImGui.TableSetupColumn("Gamepaths");
        ImGui.TableSetupColumn("Original Size");
        ImGui.TableSetupColumn("Compressed Size");
        if (string.Equals(fileGroup.Key, "tex", StringComparison.Ordinal))
        {
            ImGui.TableSetupColumn("Format");
            if (_enableBc7ConversionMode) ImGui.TableSetupColumn("Convert to BC7");
        }
        if (string.Equals(fileGroup.Key, "mdl", StringComparison.Ordinal))
        {
            ImGui.TableSetupColumn("Triangles");
        }
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        // Bomb them.
        var sortSpecs = ImGui.TableGetSortSpecs();
        if (sortSpecs.SpecsDirty && _cachedAnalysis != null)
        {
            var idx = sortSpecs.Specs.ColumnIndex;
            var asc = sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending;
            var analyzedData = _cachedAnalysis![_currentTab];

            // Hash
            if (idx is 0)
                analyzedData = asc
                    ? analyzedData.OrderBy(k => k.Key, StringComparer.Ordinal).ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal)
                    : analyzedData.OrderByDescending(k => k.Key, StringComparer.Ordinal).ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal);
            // FilePaths count
            else if (idx is 1)
                analyzedData = asc
                    ? analyzedData.OrderBy(k => k.Value.FilePaths.Count).ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal)
                    : analyzedData.OrderByDescending(k => k.Value.FilePaths.Count).ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal);
            // GamePaths count
            else if (idx is 2)
                analyzedData = asc
                    ? analyzedData.OrderBy(k => k.Value.GamePaths.Count).ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal)
                    : analyzedData.OrderByDescending(k => k.Value.GamePaths.Count).ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal);
            // OriginalSize
            else if (idx is 3)
                analyzedData = asc
                    ? analyzedData.OrderBy(k => k.Value.OriginalSize).ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal)
                    : analyzedData.OrderByDescending(k => k.Value.OriginalSize).ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal);
            // CompressedSize
            else if (idx is 4)
                analyzedData = asc
                    ? analyzedData.OrderBy(k => k.Value.CompressedSize).ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal)
                    : analyzedData.OrderByDescending(k => k.Value.CompressedSize).ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal);
            // Triangles (mdl)
            else if (string.Equals(fileGroup.Key, "mdl", StringComparison.Ordinal) && idx == 5)
                analyzedData = asc
                    ? analyzedData.OrderBy(k => k.Value.Triangles).ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal)
                    : analyzedData.OrderByDescending(k => k.Value.Triangles).ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal);
            // Format (tex)
            else if (string.Equals(fileGroup.Key, "tex", StringComparison.Ordinal) && idx == 5)
                analyzedData = asc
                    ? analyzedData.OrderBy(k => k.Value.Format.Value, StringComparer.Ordinal).ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal)
                    : analyzedData.OrderByDescending(k => k.Value.Format.Value, StringComparer.Ordinal).ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal);

            sortSpecs.SpecsDirty = false;
        }

        foreach (var item in fileGroup)
        {
            using var text = ImRaii.PushColor(ImGuiCol.Text, new Vector4(0, 0, 0, 1), string.Equals(item.Hash, _selectedHash, StringComparison.Ordinal));
            using var text2 = ImRaii.PushColor(ImGuiCol.Text, new Vector4(1, 1, 1, 1), !item.IsComputed);
            ImGui.TableNextColumn();
            if (!item.IsComputed)
            {
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGuiColors.DalamudRed.ToUint());
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGuiColors.DalamudRed.ToUint());
            }
            if (string.Equals(_selectedHash, item.Hash, StringComparison.Ordinal))
            {
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGuiColors.DalamudYellow.ToUint());
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGuiColors.DalamudYellow.ToUint());
            }
            ImGui.TextUnformatted(item.Hash);
            if (ImGui.IsItemClicked())
                _selectedHash = item.Hash;

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(item.FilePaths.Count.ToString());
            if (ImGui.IsItemClicked())
                _selectedHash = item.Hash;

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(item.GamePaths.Count.ToString());
            if (ImGui.IsItemClicked())
                _selectedHash = item.Hash;

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(SundouleiaEx.ByteToString(item.OriginalSize));
            if (ImGui.IsItemClicked())
                _selectedHash = item.Hash;

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(SundouleiaEx.ByteToString(item.CompressedSize));
            if (ImGui.IsItemClicked())
                _selectedHash = item.Hash;

            if (string.Equals(fileGroup.Key, "tex", StringComparison.Ordinal))
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(item.Format.Value);
                if (ImGui.IsItemClicked()) _selectedHash = item.Hash;
                if (_enableBc7ConversionMode)
                {
                    ImGui.TableNextColumn();
                    if (string.Equals(item.Format.Value, "BC7", StringComparison.Ordinal))
                    {
                        ImGui.TextUnformatted("");
                        continue;
                    }
                    var filePath = item.FilePaths[0];
                    bool toConvert = _texturesToConvert.ContainsKey(filePath);
                    if (ImGui.Checkbox("###convert" + item.Hash, ref toConvert))
                    {
                        if (toConvert && !_texturesToConvert.ContainsKey(filePath))
                            _texturesToConvert[filePath] = item.FilePaths.Skip(1).ToArray();
                        else if (!toConvert && _texturesToConvert.ContainsKey(filePath))
                            _texturesToConvert.Remove(filePath);
                    }
                }
            }
            if (string.Equals(fileGroup.Key, "mdl", StringComparison.Ordinal))
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(item.Triangles.ToString());
                if (ImGui.IsItemClicked())
                    _selectedHash = item.Hash;
            }
        }
    }
}