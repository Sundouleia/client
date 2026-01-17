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
    private IDynamicFolderGroup<Sundesmo>? _current;
    public DDSFolderGroupCombo(ILogger log, GroupsDrawSystem dds)
        : base(() => [.. dds.FolderMap.Values.OfType<IDynamicFolderGroup<Sundesmo>>()], log)
    {
        SearchByParts = true;
    }

    // Can pull the update refresh function from pair combo if needed,
    // or just run mimic the multi-select combo i guess.
    protected override bool IsVisible(int globalIndex, LowerString filter)
        => Items[globalIndex].FullPath.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Items[globalIndex].Name.Contains(filter, StringComparison.OrdinalIgnoreCase);

    protected override string ToString(IDynamicFolderGroup<Sundesmo> obj)
        => obj.Name;

    protected override int UpdateCurrentSelected(int currentSelected)
    {
        CurrentSelectionIdx = Items.IndexOf(p => _current == p);
        UpdateSelection(CurrentSelectionIdx >= 0 ? Items[CurrentSelectionIdx] : null);
        return CurrentSelectionIdx;
    }

    public void ClearSelected()
        => UpdateSelection(null);


    /// <summary> An override to the normal draw method that forces the current item to be the item passed in. </summary>
    /// <returns> True if a new item was selected, false otherwise. </returns>
    public bool Draw(IDynamicFolderGroup<Sundesmo>? current, float width, float innerScalar = 1.25f, uint? searchBg = null)
        => Draw(current, width, innerScalar, CFlags.None, searchBg);

    public bool Draw(IDynamicFolderGroup<Sundesmo>? current, float width, float innerScalar, CFlags flags, uint? searchBg = null)
    {
        _current = current;

        InnerWidth = width * innerScalar;
        var preview = Current?.Name ?? "Set Parent Folder.. (Optional)";
        var ret = Draw("##FolderGroupCombo", preview, string.Empty, width, ImUtf8.TextHeightSpacing, flags, searchBg);
        _current = null;
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
