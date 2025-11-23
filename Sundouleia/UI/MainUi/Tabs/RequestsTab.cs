using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.Gui.Components;

namespace Sundouleia.Gui.MainWindow;

// This is a placeholder UI structure for the sake of getting testing
// functional, and will be restructured later.
public class RequestsTab
{
    private DynamicRequestFolder _incomingFolder;
    private DynamicRequestFolder _pendingFolder;

    // Can turn this into an int for our button row if we want to add more options.
    private bool _onPending = false;        // If on incoming or pending.
    private bool _optionsExpanded = false;  // If options are expanded or not.

    public RequestsTab(DrawEntityFactory factory)
    {

        // Create the folders.
        _incomingFolder = factory.CreateIncomingRequestsFolder();
        _pendingFolder = factory.CreateOutgoingRequestsFolder();
    }
     
    public DynamicRequestFolder Incoming => _incomingFolder;
    public DynamicRequestFolder Pending => _pendingFolder;

    public void DrawSection()
    {

        using var _ = CkRaii.Child("content", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);

        ButtonSelectorStrip(_.InnerRegion.X);
        ImGui.Separator();

        _incomingFolder.DrawContents();
        _pendingFolder.DrawContents();
    }

    /// <summary>
    ///     Selector Strip.
    /// </summary>
    public void ButtonSelectorStrip(float width)
    {
        var bgCol = ColorHelpers.Fade(ImGui.GetColorU32(ImGuiCol.FrameBg), 0.4f);
        var cogSize = CkGui.IconButtonSize(FAI.Cog);

        DrawIncomingPendingSelector(width - cogSize.X - ImUtf8.ItemInnerSpacing.X, bgCol);
        ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(ImGuiCol.Button), 5f);

        ImUtf8.SameLineInner();
        if (CkGui.IconButton(FAI.Cog, inPopup: !_optionsExpanded))
            _optionsExpanded = !_optionsExpanded;
        CkGui.AttachToolTip("Default Configurations for these Requests.");
        ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(ImGuiCol.Button), 5f);
    }

    private void DrawIncomingPendingSelector(float width, uint bgCol)
    {
        using var _ = CkRaii.Child("IncomingPending", new Vector2(width, ImUtf8.FrameHeight), bgCol, 5f);
        var buttonWidth = (width - ImUtf8.ItemInnerSpacing.X) / 2;
        // Draw the incoming option.
        if (CkGui.IconTextButtonCentered(FAI.CloudDownloadAlt, "Incoming", buttonWidth, _onPending))
            _onPending = false;

        ImUtf8.SameLineInner();
        if (CkGui.IconTextButtonCentered(FAI.CloudUploadAlt, "Pending", buttonWidth, !_onPending))
            _onPending = true;
    }
}