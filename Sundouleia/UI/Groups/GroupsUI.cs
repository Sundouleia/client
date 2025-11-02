using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.CustomCombos;
using Sundouleia.Gui.Components;
using Sundouleia.Gui.Handlers;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Tutorial;
using Sundouleia.Utils;

namespace Sundouleia.Gui;

public class GroupsUI : WindowMediatorSubscriberBase
{
    private readonly GroupsManager _manager;
    private readonly DrawEntityFactory _factory;
    private readonly TutorialService _guides;

    private FAIconCombo _iconGalleryCombo;
    private List<DrawFolderGroup> _groups;
    private DrawFolderDefault _allSundesmos;

    private SundesmoGroup _creator = new SundesmoGroup();
    public GroupsUI(ILogger<GroupsUI> logger, SundouleiaMediator mediator,
        GroupsManager manager, DrawEntityFactory factory, TutorialService guides) 
        : base(logger, mediator, "Group Manager###Sundouleia_GroupUI")
    {
        _manager = manager;
        _factory = factory;
        _guides = guides;

        _iconGalleryCombo = new FAIconCombo(logger);

        CreateGroupFolders();
        _allSundesmos = _factory.CreateDefaultFolder(Constants.FolderTagAllDragDrop, FolderOptions.FolderEditor);

        Mediator.Subscribe<FolderUpdateGroups>(this, _ => CreateGroupFolders());

        this.PinningClickthroughFalse();
        this.SetBoundaries(new(550, 470), ImGui.GetIO().DisplaySize);        
        TitleBarButtons = new TitleBarButtonBuilder()
            .AddTutorial(guides, TutorialType.Groups)
            .Build();
    }

    public List<DrawFolderGroup> Groups => _groups;

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
            foreach (var group in _groups)
                group.DrawContents();
        }

        ImUtf8.SameLineInner();

        using (CkRaii.Child("GroupManagerEditor_AllSundesmos", childSize))
        {
            _allSundesmos.DrawContents();
        }
    }

    #region Creator
    private void DrawGroupEditor()
    {
        // Icon & Name Line. (with create button)
        var createButtonWidth = CkGui.IconTextButtonSize(FAI.Plus, "Create Group");
        var rightArea = createButtonWidth + ImUtf8.FrameHeight + ImUtf8.ItemInnerSpacing.X * 2;
        var label = _creator.Label;
        var desc = _creator.Description;
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

        // Next Line, the Description.
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - rightArea);
        if (ImGui.InputTextWithHint("##GroupDescInput", "Short Description of Group..", ref desc, 150))
            _creator.Description = desc;
        CkGui.AttachToolTip("The description of this group.");
        
        // Then the offline checkbox.
        ImUtf8.SameLineInner();
        if (ImGui.Checkbox("Show Offline", ref seeOffline))
            _creator.ShowOffline = seeOffline;
    }
    #endregion Creator
    private void CreateGroupFolders()
    {
        // Create the folders based on the current config options.
        var groupFolders = new List<DrawFolderGroup>();
        foreach (var group in _manager.Config.Groups)
            groupFolders.Add(_factory.CreateGroupFolder(group, FolderOptions.FolderEditor));
        _groups = groupFolders;
    }
}