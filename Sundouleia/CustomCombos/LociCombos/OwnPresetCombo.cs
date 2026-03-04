using CkCommons.Helpers;
using CkCommons.RichText;
using CkCommons.Textures;
using Dalamud.Bindings.ImGui;
using OtterGui.Text;
using Sundouleia.Loci.Data;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.WebAPI;
using SundouleiaAPI.Hub;

namespace Sundouleia.CustomCombos;

public sealed class OwnPresetCombo : LociComboBase<LociPreset>
{
    private readonly LociManager _loci;
    private int _maxPresetCount => _loci.SavedPresets.Max(x => x.Statuses.Count);
    private float _iconWithPadding => IconSize.X + ImGui.GetStyle().ItemInnerSpacing.X;
    public OwnPresetCombo(ILogger log, MainHub hub, LociManager loci, Sundesmo sundesmo, float scale)
        : base(log, hub, sundesmo, scale, () => [.. loci.SavedPresets.OrderBy(x => x.Title.StripColorTags())])
    {
        _loci = loci;
    }

    protected override bool DisableCondition()
        => Current is null || !_sundesmo.PairPerms.LociAccess.HasAny(LociAccess.AllowOther);

    protected override string ToString(LociPreset obj)
        => obj.Title.StripColorTags();

    public bool DrawApplyPresets(string id, float width, string buttonTT)
    {
        InnerWidth = width + _iconWithPadding * _maxPresetCount;
        var prevLabel = Current is null ? "Select Presets.." : Current.Title.StripColorTags();
        return DrawComboButton(id, prevLabel, width, true, buttonTT);
    }

    protected override bool DrawSelectable(int globalIdx, bool selected)
    {
        var lociPreset = Items[globalIdx];
        var size = new Vector2(GetFilterWidth(), IconSize.Y);
        var iconsSpace = (_iconWithPadding * lociPreset.Statuses.Count);
        var titleSpace = size.X - iconsSpace;

        // Push the font first so the height is correct.
        using var _ = Fonts.Default150Percent.Push();

        var ret = ImGui.Selectable($"##{lociPreset.Title}", selected, ImGuiSelectableFlags.None, size);

        if (lociPreset.Statuses.Count > 0)
        {
            ImGui.SameLine(titleSpace);
            for (int i = 0; i < lociPreset.Statuses.Count; i++)
            {
                var status = lociPreset.Statuses[i];
                if (_loci.SavedStatuses.FirstOrDefault(s => s.GUID == status) is not { } info)
                {
                    ImGui.SameLine(0, _iconWithPadding);
                    continue;
                }

                LociIcon.Draw((uint)info.IconID, info.Stacks, IconSize);
                LociEx.AttachTooltip(info, _loci.SavedStatuses);

                if (i < lociPreset.Statuses.Count)
                    ImUtf8.SameLineInner();
            }
        }

        ImGui.SameLine(ImUtf8.ItemInnerSpacing.X);
        var adjust = (size.Y - ImUtf8.TextHeight) * 0.5f;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + adjust);
        CkRichText.Text(titleSpace, lociPreset.Title);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - adjust);
        return ret;
    }

    protected override bool CanDoAction(LociPreset item)
    {
        var ids = item.Statuses.ToHashSet();
        var toCheck = _loci.SavedStatuses.Where(s => ids.Contains(s.GUID));
        return LociEx.CanApply(_sundesmo.PairPerms, toCheck);
    }

    protected override void OnApplyButton(LociPreset item)
    {
        if (!CanDoAction(item))
            return;

        UiService.SetUITask(async () =>
        {
            var ids = item.Statuses.ToHashSet();
            var toSend = _loci.SavedStatuses.Where(s => ids.Contains(s.GUID)).Select(s => s.ToTuple());

            var res = await _hub.UserApplyLociStatusTuples(new(_sundesmo.UserData, toSend));
            if (res.ErrorCode is not SundouleiaApiEc.Success)
                Log.LogWarning($"Failed to apply loci preset {item.Title} on {_sundesmo.GetNickAliasOrUid()}: [{res.ErrorCode}]");
        });
    }
}
