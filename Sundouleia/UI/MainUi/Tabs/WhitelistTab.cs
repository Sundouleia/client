using CkCommons.Gui;
using CkCommons.Gui.Utility;
using CkCommons.Raii;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.Gui.Handlers;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Gui.MainWindow;

public class WhitelistTab : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly GroupsManager _groups;
    private readonly SundesmoManager _sundesmos;
    private readonly FolderHandler _drawFolders;

    public enum ViewMode
    {
        Basic,
        Groups,
    }

    private ViewMode _viewMode = ViewMode.Basic;

    public WhitelistTab(ILogger<WhitelistTab> logger, SundouleiaMediator mediator,
        MainConfig config, GroupsManager groups, SundesmoManager sundesmos, 
        FolderHandler drawFolders)
        : base(logger, mediator)
    {
        _config = config;
        _groups = groups;
        _sundesmos = sundesmos;
        _drawFolders = drawFolders;
    }

    public void DrawWhitelistSection()
    {
        DrawSearchFilter();
        ImGui.Separator();

        using var _ = CkRaii.Child("content", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);

        var toDraw = _viewMode is ViewMode.Groups ? _drawFolders.GroupFolders : _drawFolders.DefaultFolders;

        // Draw Folders out.
        foreach (var folder in toDraw)
            folder.Draw();
    }

    // Find a way to improve this soon, right now it is a little messy.
    private void DrawSearchFilter()
    {
        var enumWidth = ImGui.CalcTextSize("Groups").X + ImUtf8.FramePadding.X * 2 + ImUtf8.ItemInnerSpacing.X;
        
        var filter = _drawFolders.Filter;
        if (_viewMode is ViewMode.Groups)
        {
            var buttonWidth = _viewMode is ViewMode.Groups ? CkGui.IconButtonSize(FAI.Filter).X : 0f;
            if (FancySearchBar.Draw("SundesmoSearch", ImGui.GetContentRegionAvail().X - enumWidth, "filter..", ref filter, 128, buttonWidth, DrawFilterButton))
                _drawFolders.Filter = filter;
        }
        else
        {
            if (FancySearchBar.Draw("SundesmoSearch", ImGui.GetContentRegionAvail().X - enumWidth, "filter..", ref filter, 128))
                _drawFolders.Filter = filter;
        }

        ImUtf8.SameLineInner();
        if (CkGuiUtils.EnumCombo("##pair_view_type", ImGui.GetContentRegionAvail().X, _viewMode, out var newMode, Enum.GetValues<ViewMode>(), flags: CFlags.NoArrowButton))
            _viewMode = newMode;

        void DrawFilterButton()
        {
            var pressed = CkGui.IconButton(FAI.Filter, inPopup: true);
            var popupDrawPos = ImGui.GetItemRectMin() + new Vector2(ImGui.GetItemRectSize().X, 0);
            CkGui.AttachToolTip("Arrange Group display order and visibility.");

            if (pressed)
                ImGui.OpenPopup("##GroupConfiguration");

            DrawGroupListPopup(popupDrawPos);
        }
    }

    private void DrawGroupListPopup(Vector2 openPos)
    {
        using var s = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var c = ImRaii.PushColor(ImGuiCol.Border, ImGuiColors.ParsedPink);

        // Begin the popup thingy.
        ImGui.SetNextWindowPos(openPos);
        using var popup = ImRaii.Popup("##GroupConfiguration", ImGuiWindowFlags.AlwaysAutoResize);
        var id = ImGui.GetID("##GroupConfiguration");
        if (popup)
        {
            DrawGroupCheckboxList();
        }
    }
    public void DrawGroupCheckboxList()
    {
        var activeGroups = _groups.Config.VisibleGroupFolders; // HashSet<string>
        var wdl = ImGui.GetWindowDrawList();

        CkGui.ColorText("Visible Groups", ImGuiColors.ParsedGold);
        ImGui.Separator();

        foreach (var group in _groups.Config.Groups)
        {
            var label = group.Label;
            var isVisible = activeGroups.Contains(label);
            var changed = false;

            using var disabled = ImRaii.Disabled(false); // Optional: disable if needed, like locked groups, etc.

            // Push green color for changed state (highlight toggled items)
            using (ImRaii.PushColor(ImGuiCol.CheckMark, ImGuiColors.HealerGreen, changed))
            {
                if (ImGui.Checkbox($"##VisibleGroup_{label}", ref isVisible))
                {
                    if (isVisible)
                    {
                        activeGroups.Add(label);
                        Svc.Logger.Information($"Enabled group visibility: {label}");
                    }
                    else
                    {
                        activeGroups.Remove(label);
                        Svc.Logger.Information($"Disabled group visibility: {label}");
                    }
                    changed = true;
                }

                // Optional: draw an outline or indicator for groups recently toggled
                if (changed)
                    wdl.AddRect(
                        ImGui.GetItemRectMin(),
                        ImGui.GetItemRectMax(),
                        ImGui.GetColorU32(ImGuiCol.CheckMark),
                        ImGui.GetStyle().FrameRounding,
                        ImDrawFlags.RoundCornersAll
                    );
            }

            // Draw label text
            ImUtf8.SameLineInner();
            ImUtf8.TextFrameAligned(label);
        }
    }
}
