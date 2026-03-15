using CkCommons.Helpers;
using CkCommons.RichText;
using CkCommons.Textures;
using Dalamud.Bindings.ImGui;
using OtterGui.Text;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data;
using SundouleiaAPI.Hub;

namespace Sundouleia.CustomCombos;

public sealed class OwnStatusCombo : LociComboBase<LociStatusInfo>
{
    public OwnStatusCombo(ILogger log, MainHub hub, Sundesmo sundesmo, float scale)
        : base(log, hub, sundesmo, scale, () => [.. LociData.Cache.StatusList.OrderBy(x => x.Title)])
    {
    }

    protected override bool DisableCondition()
        => Guid.Empty.Equals(Current.GUID) || !_sundesmo.PairPerms.LociAccess.HasAny(LociAccess.AllowOther);

    protected override string ToString(LociStatusInfo obj)
        => obj.Title.StripColorTags();

    public bool DrawApplyStatuses(string id, float width, string buttonTT)
    {
        InnerWidth = width + IconSize.X + ImGui.GetStyle().ItemInnerSpacing.X;
        var prevLabel = Current.GUID == Guid.Empty ? "Select Status.." : Current.Title.StripColorTags();
        return DrawComboButton(id, prevLabel, width, true, buttonTT);
    }

    public bool DrawRemoveStatuses(string id, float width, string buttonTT)
    {
        InnerWidth = width + IconSize.X + ImGui.GetStyle().ItemInnerSpacing.X;
        var prevLabel = Current.GUID == Guid.Empty ? "Select Status.." : Current.Title.StripColorTags();
        return DrawComboButton(id, prevLabel, width, false, buttonTT);
    }

    protected override bool DrawSelectable(int globalIdx, bool selected)
    {
        var size = new Vector2(GetFilterWidth(), IconSize.Y);
        var titleSpace = size.X - IconSize.X;
        var myStatus = Items[globalIdx];

        // Push the font first so the height is correct.
        using var _ = Fonts.Default150Percent.Push();

        var ret = ImGui.Selectable($"##{myStatus.Title}", selected, ImGuiSelectableFlags.None, size);

        ImGui.SameLine(titleSpace);
        LociIcon.Draw(myStatus.IconID, myStatus.Stacks, IconSize);
        SundouleiaEx.AttachTooltip(myStatus, LociData.Cache);

        ImGui.SameLine(ImUtf8.ItemInnerSpacing.X);
        var adjust = (size.Y - ImUtf8.TextHeight) * 0.5f;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + adjust);
        CkRichText.Text(titleSpace, myStatus.Title);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - adjust);

        return ret;
    }

    protected override bool CanDoAction(LociStatusInfo item)
        => SundouleiaEx.CanApply(_sundesmo.PairPerms, [ item ]);

    protected override void OnApplyButton(LociStatusInfo item)
    {
        UiService.SetUITask(async () =>
        {
            var res = await _hub.UserApplyLociStatusTuples(new(_sundesmo.UserData, [item.ToStruct()]));
            if (res.ErrorCode is not SundouleiaApiEc.Success)
                Log.LogWarning($"Failed to apply status {item.Title} on {_sundesmo.GetNickAliasOrUid()}: [{res.ErrorCode}]");
        });
    }
}
