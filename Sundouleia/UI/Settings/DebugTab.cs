using Dalamud.Interface.Utility.Raii;
using CkCommons.Gui.Utility;
using Sundouleia.PlayerClient;
using Dalamud.Bindings.ImGui;
using CkCommons.Gui;
using Sundouleia.Services;

namespace Sundouleia.Gui;

public class DebugTab
{
    /// <summary> Displays the Debug section within the settings, where we can set our debug level </summary>
    private static readonly (string Label, LoggerType[] Flags)[] FlagGroups =
    {
        ("Achievements", [ LoggerType.Achievements, LoggerType.AchievementEvents, LoggerType.AchievementInfo ]),
        ("Interop / IPC", [
            LoggerType.IpcSundouleia, LoggerType.IpcPenumbra, LoggerType.IpcGlamourer, LoggerType.IpcCustomize,
            LoggerType.IpcMoodles, LoggerType.IpcHeels, LoggerType.IpcHonorific, LoggerType.IpcPetNames ]),
        ("Client Data", [ 
            LoggerType.ResourceMonitor, LoggerType.PlayerMods, LoggerType.MinionMods, LoggerType.PetMods,
            LoggerType.CompanionMods, LoggerType.OwnedObjects, LoggerType.DataDistributor, LoggerType.ClientUpdates ]),
        ("File Info", [ 
            LoggerType.FileCache, LoggerType.FileCsv, LoggerType.FileMonitor, LoggerType.FileCompactor,
            LoggerType.FileWatcher, LoggerType.FileUploads, LoggerType.FileDownloads, LoggerType.FileService ]),
        ("Pair Data", [
            LoggerType.PairManagement, LoggerType.PairDataTransfer, LoggerType.PairHandler, LoggerType.PairMods, 
            LoggerType.PairAppearance ]),
        ("Radar", [ LoggerType.RadarManagement, LoggerType.RadarData, LoggerType.RadarChat ]),
        ("Services", [
            LoggerType.UIManagement, LoggerType.Textures, LoggerType.DtrBar, LoggerType.Profiles, 
            LoggerType.Mediator, LoggerType.Combos ]),
        ("SundouleiaHub", [ LoggerType.ApiCore, LoggerType.Callbacks, LoggerType.HubFactory, LoggerType.Health, LoggerType.JwtTokens ])
    };

    private readonly MainConfig _mainConfig;
    public DebugTab(MainConfig config)
    {
        _mainConfig = config;
    }

    public void DrawDebugMain()
    {
        CkGui.FontText("Debug Configuration", UiFontService.UidFont);

        // display the combo box for setting the log level we wish to have for our plugin
        if (CkGuiUtils.EnumCombo("Log Level", 400, MainConfig.LogLevel, out var newValue))
        {
            MainConfig.LogLevel = newValue;
            _mainConfig.Save();
        }

        var logFilters = MainConfig.LoggerFilters;

        // draw a collapsible tree node here to draw the logger settings:
        ImGui.Spacing();
        if (ImGui.TreeNode("Advanced Logger Filters (Only Edit if you know what you're doing!)"))
        {
            AdvancedLogger();
            ImGui.TreePop();
        }
    }

    private void AdvancedLogger()
    {
        var flags = (ulong)MainConfig.LoggerFilters;
        bool isFirstSection = true;

        var drawList = ImGui.GetWindowDrawList();
        foreach (var (label, flagGroup) in FlagGroups)
        {
            using (ImRaii.Group())
            {
                // Draw separator line on top of the group
                var cursorPos = ImGui.GetCursorScreenPos();
                drawList.AddLine(
                    new Vector2(cursorPos.X, cursorPos.Y),
                    new Vector2(cursorPos.X + ImGui.GetContentRegionAvail().X, cursorPos.Y),
                    ImGui.GetColorU32(ImGuiCol.Border)
                );
                ImGui.Dummy(new Vector2(0, 4));

                // Begin table for 4 columns
                using (var table = ImRaii.Table(label, 4, ImGuiTableFlags.None))
                {
                    for (int i = 0; i < flagGroup.Length; i++)
                    {
                        ImGui.TableNextColumn();

                        var flag = flagGroup[i];
                        bool flagState = (flags & (ulong)flag) != 0;
                        if (ImGui.Checkbox(flag.ToString(), ref flagState))
                        {
                            if (flagState) 
                                flags |= (ulong)flag;
                            else
                                flags &= ~(ulong)flag;

                            // update the loggerFilters.
                            MainConfig.LoggerFilters = (LoggerType)flags;
                            _mainConfig.Save();
                        }
                    }

                    // In the first section, add "All On" / "All Off" buttons in the last column
                    if (isFirstSection)
                    {
                        ImGui.TableNextColumn();
                        if (ImGui.Button("All On"))
                        {
                            MainConfig.LoggerFilters = LoggerType.Recommended;
                            _mainConfig.Save();
                        }
                        ImGui.SameLine();
                        if (ImGui.Button("All Off"))
                        {
                            MainConfig.LoggerFilters = LoggerType.None;
                            _mainConfig.Save();
                        }
                    }
                }
                CkGui.AttachToolTip(label, color: new Vector4(1f, 0.85f, 0f, 1f));
            }

            isFirstSection = false;
        }
    }
}
