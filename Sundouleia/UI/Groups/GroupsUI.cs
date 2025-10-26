using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui;
using OtterGui.Text;
using Sundouleia.CustomCombos;
using Sundouleia.Gui.Handlers;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Tutorial;
using Sundouleia.Utils;

namespace Sundouleia.Gui;

public class GroupsUI : WindowMediatorSubscriberBase
{
    private readonly GroupsSelector _selector;
    private readonly GroupsManager _manager;
    private readonly FolderHandler _drawFolders;
    private readonly TutorialService _guides;

    private FAIconCombo _iconGalleryCombo;

    private SundesmoGroup? _editingGroup = null;
    public GroupsUI(ILogger<GroupsUI> logger, SundouleiaMediator mediator, GroupsSelector selector,
        GroupsManager manager, FolderHandler handler, TutorialService guides) 
        : base(logger, mediator, "Group Manager###Sundouleia_GroupUI")
    {
        _selector = selector;
        _manager = manager;
        _drawFolders = handler;
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
        CkGui.FontText("Group Manager", UiFontService.UidFont);
        CkGui.SeparatorSpaced(CkColor.VibrantPink.Uint());

        if (CkGui.IconTextButton(FAI.Plus, "Create New Group", disabled: _editingGroup is not null))
        {
            _editingGroup = new SundesmoGroup();
        }

        if (_editingGroup is not null)
            DrawGroupEditor();

        // Display active groups.
        ImGui.Separator();
        CkGui.FontText("Existing Groups", UiFontService.UidFont);

        foreach (var group in _drawFolders.GroupFolders)
            group.Draw();
    }

    private void DrawGroupEditor()
    {
        CkGui.FontText("Group Editor.", UiFontService.Default150Percent);
        using var _ = CkRaii.FramedChildPaddedW($"##GroupEditorFrame", ImGui.GetContentRegionAvail().X, CkStyle.GetFrameRowsHeight(6), 0, CkColor.LushPinkLine.Uint());

        if (_editingGroup is not { } group)
            return;

        using (var t = ImRaii.Table("GroupEditorTable", 2, ImGuiTableFlags.SizingFixedFit))
        {
            if (!t)
                return;

            ImGui.TableSetupColumn("Attribute");
            ImGui.TableSetupColumn("Value");
            ImGui.TableNextRow();

            // Icon Line.
            var iconCol = ImGui.ColorConvertU32ToFloat4(group.IconColor);
            ImGuiUtil.DrawFrameColumn("Icon");

            ImGui.TableNextColumn();
            _iconGalleryCombo.Draw("IconSelector", group.Icon, 50f, 10, ImGuiComboFlags.NoArrowButton);
            ImUtf8.SameLineInner();
            if (ImGui.ColorEdit4("##IconColor", ref iconCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
                group.IconColor = ImGui.ColorConvertFloat4ToU32(iconCol);
            ImGui.TableNextRow();

            // Label Line.
            var label = group.Label;
            var labelCol = ImGui.ColorConvertU32ToFloat4(group.LabelColor);
            ImGuiUtil.DrawFrameColumn("Name");

            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputText("##GroupLabelInput", ref label, 64))
                group.Label = label;
            ImUtf8.SameLineInner();
            if (ImGui.ColorEdit4("##LabelColor", ref labelCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
                group.LabelColor = ImGui.ColorConvertFloat4ToU32(labelCol);
            ImGui.TableNextRow();

            // Description Line.
            var desc = group.Description;
            var descCol = ImGui.ColorConvertU32ToFloat4(group.DescriptionColor);
            ImGuiUtil.DrawFrameColumn("Description");

            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputText("##GroupDescInput", ref desc, 128))
                group.Description = desc;
            ImUtf8.SameLineInner();
            if (ImGui.ColorEdit4("##DescColor", ref descCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
                group.DescriptionColor = ImGui.ColorConvertFloat4ToU32(descCol);
            ImGui.TableNextRow();

            // Preferences.
            var seeOffline = group.ShowOffline;
            ImGuiUtil.DrawFrameColumn("Show Offline");
            ImGui.TableNextColumn();
            if (ImGui.Checkbox("##Show Offline", ref seeOffline))
                group.ShowOffline = seeOffline;
        }

        if (ImGui.Button("Create Group", new Vector2(ImGui.GetContentRegionAvail().X, ImUtf8.FrameHeight)))
        {
            if (_manager.TryAddNewGroup(group))
            {
                _logger.LogInformation($"Created new group {{{group.Label}}}");
                _editingGroup = null;
            }
        }

        CkGui.SeparatorSpaced(CkColor.VibrantPink.Uint());
        CkGui.FontText("Linked Users", UiFontService.Default150Percent);

        // List of users for drag drop multi-selection.
    }

    private void DrawCreatorEditor()
    {
        // Framed child.
        var height = CkStyle.TwoRowHeight() + CkGui.GetSeparatorHeight() + CkGui.CalcFontTextSize("A", UiFontService.UidFont).Y;
        using var _ = CkRaii.FramedChildPaddedW("Storage", ImGui.GetContentRegionAvail().X, height, 0, CkColor.VibrantPink.Uint(), CkStyle.ChildRoundingLarge());
        var topLeftPos = ImGui.GetCursorScreenPos();

        CkGui.FontTextCentered("FileCache Storage", UiFontService.UidFont);
        CkGui.Separator(CkColor.VibrantPink.Uint());
    }
}
