using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using Sundouleia.Gui.Profiles;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;

namespace Sundouleia.Gui.MainWindow;

/// <summary>
///     Like a Quick-Access shortcut menu for general things. <para />
///     Think Profile Customizer, Group management, ext.
/// </summary>
public class HomepageTab
{
    private readonly SundouleiaMediator _mediator;
    private readonly CharaObjectWatcher _watcher;

    private int HoveredItemIndex = -1;
    private readonly List<(string Label, FontAwesomeIcon Icon, Action OnClick)> Modules;

    public HomepageTab(SundouleiaMediator mediator, CharaObjectWatcher watcher)
    {
        _mediator = mediator;
        _watcher = watcher;
        // Define all module information in a single place
        Modules = new List<(string, FontAwesomeIcon, Action)>
        {
            // Make this editor a better UI in the future.
            ("Profile Customizer", FAI.ObjectGroup, () => _mediator.Publish(new UiToggleMessage(typeof(ProfileEditorUI)))),
            // ("Group Management", FAI.PeopleGroup, () => _mediator.Publish(new UiToggleMessage(typeof(GroupManagementUI)))),
            // ("MCDF Exporter", FAI.FileExport, () => _mediator.Publish(new UiToggleMessage(typeof(MCDFExporterUI)))),
            // ("Optimize Data", FAI.Eye, () => _mediator.Publish(new UiToggleMessage(typeof(OptimizerUI)))),
            // ("Achievements", FAI.Trophy, () => _mediator.Publish(new UiToggleMessage(typeof(AchievementsUI))))
        };
    }

    public void DrawHomepageSection()
    {
        using var s = ImRaii.PushStyle(ImGuiStyleVar.WindowBorderSize, 1f)
            .Push(ImGuiStyleVar.ChildRounding, 4f)
            .Push(ImGuiStyleVar.WindowPadding, new Vector2(6, 1));
        using var c = ImRaii.PushColor(ImGuiCol.Border, ImGuiColors.ParsedPink);

        using var _ = CkRaii.Child("##Homepage", new Vector2(CkGui.GetWindowContentRegionWidth(), 0), wFlags: WFlags.NoScrollbar);

        // Should add sundouleia font.
        var sizeFont = CkGui.CalcFontTextSize("Achievements Module", UiFontService.UidFont);
        var selectableSize = new Vector2(CkGui.GetWindowContentRegionWidth(), sizeFont.Y + ImGui.GetStyle().WindowPadding.Y * 2);
        var itemGotHovered = false;

        for (var i = 0; i < Modules.Count; i++)
        {
            var module = Modules[i];
            var isHovered = HoveredItemIndex == i;

            if (HomepageSelectable(module.Label, module.Icon, selectableSize, isHovered))
                module.OnClick?.Invoke();

            if (ImGui.IsItemHovered())
            {
                itemGotHovered = true;
                HoveredItemIndex = i;
            }
        }
        // if itemGotHovered is false, reset the index.
        if (!itemGotHovered)
            HoveredItemIndex = -1;

        try
        {
            unsafe
            {
                var game = GameMain.Instance();
                CkGui.ColorText($"Territory Transition Delay:", ImGuiColors.TankBlue);
                CkGui.TextInline(game->TerritoryTransitionDelay.ToString());

                CkGui.ColorText($"Territory Transition State:", ImGuiColors.TankBlue);
                CkGui.TextInline(game->TerritoryTransitionState.ToString());

                CkGui.ColorText($"Connected To Zone:", ImGuiColors.TankBlue);
                CkGui.TextInline(game->ConnectedToZone ? "Yes" : "No");

                CkGui.ColorText($"Territory Load State:", ImGuiColors.TankBlue);
                CkGui.TextInline(game->TerritoryLoadState.ToString());

                CkGui.ColorText($"Next Territory Type Id:", ImGuiColors.TankBlue);
                CkGui.TextInline(game->NextTerritoryTypeId.ToString());

                CkGui.ColorText($"Current Territory Type Id:", ImGuiColors.TankBlue);
                CkGui.TextInline(game->CurrentTerritoryTypeId.ToString());

                CkGui.ColorText($"Current Territory Intended Use Id:", ImGuiColors.TankBlue);
                CkGui.TextInline(game->CurrentTerritoryIntendedUseId.ToString());

                CkGui.ColorText($"Current Territory Filter Key:", ImGuiColors.TankBlue);
                CkGui.TextInline(game->CurrentTerritoryFilterKey.ToString());

                CkGui.ColorText($"Current Content Finder Condition Id:", ImGuiColors.TankBlue);
                CkGui.TextInline(game->CurrentContentFinderConditionId.ToString());

                CkGui.ColorText($"Transition Territory Type Id:", ImGuiColors.TankBlue);
                CkGui.TextInline(game->TransitionTerritoryTypeId.ToString());

                CkGui.ColorText($"Transition Territory Filter Key:", ImGuiColors.TankBlue);
                CkGui.TextInline(game->TransitionTerritoryFilterKey.ToString());

                CkGui.ColorText($"Current Map Id:", ImGuiColors.TankBlue);
                CkGui.TextInline(game->CurrentMapId.ToString());

                CkGui.ColorText($"Millisecond Counter:", ImGuiColors.TankBlue);
                CkGui.TextInline(game->MilisecondCounter.ToString("F2"));

                CkGui.ColorText($"Runtime Seconds:", ImGuiColors.TankBlue);
                CkGui.TextInline(game->RuntimeSeconds.ToString());

                CkGui.ColorText($"Runtime Seconds Changed:", ImGuiColors.TankBlue);
                CkGui.TextInline(game->RuntimeSecondsChanged ? "Yes" : "No");

                CkGui.ColorText($"Runtime:", ImGuiColors.TankBlue);
                CkGui.TextInline(game->Runtime.ToString("F2"));

                CkGui.ColorText("In Idle Cam:", ImGuiColors.TankBlue);
                CkGui.TextInline(GameMain.IsInIdleCam() ? "Yes" : "No");
            }
        }
        catch (Exception ex)
        {
            Svc.Logger.Error($"Exception in HomepageTab Draw: {ex}");
        }


    }

    private bool HomepageSelectable(string label, FontAwesomeIcon icon, Vector2 region, bool hovered = false)
    {
        using var bgColor = ImRaii.PushColor(ImGuiCol.ChildBg, hovered ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : new Vector4(0.25f, 0.2f, 0.2f, 0.4f).ToUint());

        // store the screen position before drawing the child.
        var buttonPos = ImGui.GetCursorScreenPos();
        using (ImRaii.Child($"##HomepageItem{label}", region, true, WFlags.NoInputs | WFlags.NoScrollbar))
        {
            using var group = ImRaii.Group();
            var height = ImGui.GetContentRegionAvail().Y;

            CkGui.FontText(label, UiFontService.UidFont);
            ImGui.SetWindowFontScale(1.5f);

            var size = CkGui.IconSize(FAI.WaveSquare);
            var color = hovered ? ImGuiColors.ParsedGold : ImGuiColors.DalamudWhite;
            ImGui.SameLine(CkGui.GetWindowContentRegionWidth() - size.X - ImGui.GetStyle().ItemInnerSpacing.X);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (height - size.Y) / 2);
            CkGui.IconText(icon, color);

            ImGui.SetWindowFontScale(1.0f);
        }
        // draw the button over the child.
        ImGui.SetCursorScreenPos(buttonPos);
        if (ImGui.InvisibleButton("##Button-" + label, region))
            return true && !UiService.DisableUI;

        return false;
    }
}
