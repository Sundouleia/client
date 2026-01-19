using CkCommons.DrawSystem;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using OtterGui.Classes;
using OtterGui.Extensions;
using OtterGui.Text;
using Sundouleia.DrawSystem;
using Sundouleia.Pairs;

namespace Sundouleia.CustomCombos.Editor;

// A special combo for pairs, that must maintain its distinctness and update accordingly based on changes.
public sealed class DDSFolderGroupCombo : CkFilterComboCache<IDynamicFolderGroup<Sundesmo>>
{
    public DDSFolderGroupCombo(ILogger log, GroupsDrawSystem dds)
        : base(() => [.. dds.FolderMap.Values.OfType<IDynamicFolderGroup<Sundesmo>>()], log)
    {
        SearchByParts = true;
    }

    private void UpdateCurrentSelection(IDynamicFolderGroup<Sundesmo>? current)
    {
        if (current == Current)
            return;

        Log.LogInformation($"Current is: {current?.Name ?? "null"}, But Interal Current is: {Current?.Name ?? "null"}");
        Log.LogInformation($"DDSFolderGroupCombo: Updating current selection to {current?.Name ?? "null"}");

        // Need to refresh.
        var priorState = IsInitialized;
        if (priorState)
            Cleanup();

        // Update the Idx from the cache.
        CurrentSelectionIdx = Items.IndexOf(i => i.ID == current?.ID);
        // if the index is a valid index, update the selection.
        if (CurrentSelectionIdx >= 0)
        {
            UpdateSelection(Items[CurrentSelectionIdx]);
        }
        else
        {
            UpdateSelection(null);
        }

        // If we were not in a prior state by this point, go ahead and cleanup.
        if (!priorState)
            Cleanup();
    }

    // Can pull the update refresh function from pair combo if needed,
    // or just run mimic the multi-select combo i guess.
    protected override bool IsVisible(int globalIndex, LowerString filter)
        => Items[globalIndex].FullPath.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Items[globalIndex].Name.Contains(filter, StringComparison.OrdinalIgnoreCase);

    protected override string ToString(IDynamicFolderGroup<Sundesmo> obj)
        => obj.Name;

    public void ClearSelected()
        => UpdateSelection(null);


    /// <summary> An override to the normal draw method that forces the current item to be the item passed in. </summary>
    /// <returns> True if a new item was selected, false otherwise. </returns>
    public bool Draw(IDynamicFolderGroup<Sundesmo>? current, float width, float innerScalar = 1.25f, uint? searchBg = null)
        => Draw(current, width, innerScalar, CFlags.None, searchBg);

    public bool Draw(IDynamicFolderGroup<Sundesmo>? current, float width, float innerScalar, CFlags flags, uint? searchBg = null)
    {
        UpdateCurrentSelection(current);
        InnerWidth = width * innerScalar;
        var preview = Current?.Name ?? "Set Parent Folder.. (Optional)";
        var ret = Draw("##FolderGroupCombo", preview, string.Empty, width, ImUtf8.TextHeightSpacing, flags, searchBg);
        return ret;
    }

    protected override bool DrawSelectable(int globalIdx, bool selected)
    {
        var folderGroup = Items[globalIdx];

        var ret = base.DrawSelectable(globalIdx, selected);
        if (folderGroup.Parent is not null && !folderGroup.Parent.IsRoot)
            DrawRightAlignedPath(folderGroup);

        return ret;
    }

    private void DrawRightAlignedPath(IDynamicFolderGroup<Sundesmo> folderGroup)
    {
        ImGui.SameLine(0f);
        CkGui.RightFrameAlignedColor(folderGroup.FullPath, ImGuiColors.DalamudGrey2);
    }
}
