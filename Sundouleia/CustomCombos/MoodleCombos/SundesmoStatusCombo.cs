using CkCommons.Helpers;
using CkCommons.RichText;
using CkCommons.Textures;
using Dalamud.Bindings.ImGui;
using OtterGui.Text;
using Sundouleia.Pairs;
using Sundouleia.Services;
using Sundouleia.WebAPI;
using SundouleiaAPI.Hub;

namespace Sundouleia.CustomCombos;

// Could maybe split between an applier and remover but idk.
public sealed class SundesmoStatusCombo : MoodleComboBase<MoodlesStatusInfo>
{
    public SundesmoStatusCombo(ILogger log, MainHub hub, Sundesmo sundesmo, float scale)
        : base(log, hub, sundesmo, scale, () => [ .. sundesmo.SharedData.Statuses.Values.OrderBy(x => x.Title)])
    { }

    public SundesmoStatusCombo(ILogger log, MainHub hub, Sundesmo sundesmo, float scale, Func<IReadOnlyList<MoodlesStatusInfo>> generator)
        : base(log, hub, sundesmo, scale, generator)
    { }

    protected override bool DisableCondition()
        => Current.GUID == Guid.Empty || !_sundesmo.PairPerms.MoodleAccess.HasAny(MoodleAccess.AllowOwn);

    protected override string ToString(MoodlesStatusInfo obj)
        => obj.Title.StripColorTags();

    public bool DrawStatuses(string id, float width, bool isApplying, string buttonTT)
    {
        InnerWidth = width + IconSize.X + ImUtf8.ItemInnerSpacing.X;
        var prevLabel = Current.GUID == Guid.Empty ? "Select Status.." : Current.Title.StripColorTags();
        return DrawComboButton(id, prevLabel, width, isApplying, buttonTT);
    }

    protected override bool DrawSelectable(int globalIdx, bool selected)
    {
        var size = new Vector2(GetFilterWidth(), IconSize.Y);
        var titleSpace = size.X - IconSize.X;
        var myStatus = Items[globalIdx];

        // Push the font first so the height is correct.
        using var _ = Fonts.Default150Percent.Push();

        var ret = ImGui.Selectable("##" + myStatus.Title, selected, ImGuiSelectableFlags.None, size);

        ImGui.SameLine(titleSpace);
        MoodleIcon.DrawMoodleIcon(myStatus.IconID, myStatus.Stacks, IconSize);
        myStatus.AttachTooltip(_sundesmo.SharedData.StatusList);

        ImGui.SameLine(ImUtf8.ItemInnerSpacing.X);
        var adjust = (size.Y - ImUtf8.TextHeight) * 0.5f;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + adjust);
        CkRichText.Text(titleSpace, myStatus.Title);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - adjust);
        return ret;
    }

    protected override bool CanDoAction(MoodlesStatusInfo item)
        => MoodlesEx.CanApplyMoodles(_sundesmo.PairPerms, [ item ]);

    protected override void OnApplyButton(MoodlesStatusInfo item)
    {
        UiService.SetUITask(async () =>
        {
            var res = await _hub.UserApplyMoodles(new(_sundesmo.UserData, [item.GUID], false));
            if (res.ErrorCode is not SundouleiaApiEc.Success)
                Log.LogWarning($"Failed to apply moodle status {item.Title} on {_sundesmo.GetNickAliasOrUid()}: [{res.ErrorCode}]");
        });
    }

    protected override void OnRemoveButton(MoodlesStatusInfo item)
    {
        UiService.SetUITask(async () =>
        {
            var res = await _hub.UserRemoveMoodles(new (_sundesmo.UserData, [item.GUID]));
            if (res.ErrorCode is not SundouleiaApiEc.Success)
                Log.LogWarning($"Failed to remove moodle status {item.Title} from {_sundesmo.GetNickAliasOrUid()}: [{res.ErrorCode}]");
        });
    }
}
