using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using OtterGui.Text;
using Sundouleia.CustomCombos;
using Sundouleia.DrawSystem;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Tutorial;
using Sundouleia.Utils;

namespace Sundouleia.Gui;

// Might phase out this window in favor of a our GroupsDrawer later.
public class GroupsUI : WindowMediatorSubscriberBase
{
    private readonly GroupsDrawer _drawer;
    private readonly GroupsManager _manager;
    private readonly TutorialService _guides;

    private FAIconCombo _iconGalleryCombo;

    private SundesmoGroup _creator = new SundesmoGroup();
    public GroupsUI(ILogger<GroupsUI> logger, SundouleiaMediator mediator,
        GroupsDrawer drawer, GroupsManager manager, TutorialService guides) 
        : base(logger, mediator, "Group Manager###SundouleiaGroups")
    {
        _drawer = drawer;
        _manager = manager;
        _guides = guides;

        _iconGalleryCombo = new FAIconCombo(logger);

        this.PinningClickthroughFalse();
        this.SetBoundaries(new(550, 470), ImGui.GetIO().DisplaySize);        
        TitleBarButtons = new TitleBarButtonBuilder()
            .AddTutorial(guides, TutorialType.Groups)
            .Build();
    }

    protected override void PreDrawInternal()
    { }

    protected override void PostDrawInternal()
    { }

    protected override void DrawInternal()
    {
        DrawGroupEditor();

        // Display active groups.
        ImGui.Spacing();
        CkGui.SeparatorSpaced(CkColor.VibrantPink.Uint());

        // calculate the width of the display area.
        var region = ImGui.GetContentRegionAvail();
        var childSize = new Vector2((region.X - ImUtf8.ItemSpacing.X) / 2, region.Y);

        using (CkRaii.Child("GroupManagerEditor_Groups", childSize))
        {
            // TBD: Draw the groups.
        }

        ImUtf8.SameLineInner();

        using (CkRaii.Child("GroupManagerEditor_AllSundesmos", childSize))
        {
            // TBD: Draw all sundesmos.
        }
    }

    private void DrawGroupEditor()
    {
        // Icon & Name Line. (with create button)
        var createButtonWidth = CkGui.IconTextButtonSize(FAI.Plus, "Create Group");
        var rightArea = createButtonWidth + ImUtf8.FrameHeight + ImUtf8.ItemInnerSpacing.X * 2;
        var label = _creator.Label;
        var iconCol = ImGui.ColorConvertU32ToFloat4(_creator.IconColor);
        var labelCol = ImGui.ColorConvertU32ToFloat4(_creator.LabelColor);
        var seeOffline = _creator.ShowOffline;

        // Draw out the gallery combo for the icon.
        _iconGalleryCombo.Draw("IconSelector", _creator.Icon, 10, ImGuiComboFlags.NoArrowButton);
        CkGui.AttachToolTip("Select an icon for the group.");
        // Then the icon color editor.
        ImUtf8.SameLineInner();
        if (ImGui.ColorEdit4("##IconColor", ref iconCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
            _creator.IconColor = ImGui.ColorConvertFloat4ToU32(iconCol);
        CkGui.AttachToolTip("The color of the icon.");

        // Then the Label.
        ImUtf8.SameLineInner();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X -  rightArea);
        if (ImGui.InputTextWithHint("##GroupLabelInput", "Define Group Name..", ref label, 64))
            _creator.Label = label;
        CkGui.AttachToolTip("The name of this group.");
        ImUtf8.SameLineInner();
        if (ImGui.ColorEdit4("##LabelColor", ref labelCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
            _creator.LabelColor = ImGui.ColorConvertFloat4ToU32(labelCol);
        // Then the create button.
        ImUtf8.SameLineInner();
        if (ImGui.Button("Create Group", new Vector2(ImGui.GetContentRegionAvail().X, ImUtf8.FrameHeight)))
        {
            if (_manager.TryAddNewGroup(_creator))
                _logger.LogInformation($"Created new group {{{_creator.Label}}}");
            else
                _logger.LogWarning($"Failed to create new group {{{_creator.Label}}}");
        }
        CkGui.AttachToolTip("Create the group with these current settings.");
        
        // Then the offline checkbox.
        ImUtf8.SameLineInner();
        if (ImGui.Checkbox("Show Offline", ref seeOffline))
            _creator.ShowOffline = seeOffline;
    }
}