using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Sundouleia.Pairs;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data;

namespace Sundouleia.Gui.Profiles;

public class ProfileUI : WindowMediatorSubscriberBase
{
    private bool ThemePushed = false;

    private readonly ProfileHelper _drawHelper;
    private readonly SundesmoManager _sundesmos;
    private readonly ProfileService _service;

    private bool ShowFullUID { get; init; }
    private bool HoveringCloseButton = false;
    private bool HoveringReportButton = false;

    public ProfileUI(ILogger<ProfileUI> logger, SundouleiaMediator mediator,
        ProfileHelper helper, SundesmoManager pairs, ProfileService service, UserData user) 
        : base(logger, mediator, $"###Profile-{user.UID}")
    {
        _drawHelper = helper;
        _sundesmos = pairs;
        _service = service;
        User = user;
        ShowFullUID = user.UID == MainHub.UID || _sundesmos.DirectPairs.Any(x => x.UserData.UID == user.UID);

        Flags = WFlags.NoResize | WFlags.NoScrollbar | WFlags.NoTitleBar;
        IsOpen = true;
        ForceMainWindow = true;
        this.SetBoundaries(new(288, 576));
    }

    public UserData User { get; init; }

    private static float Rounding => 35f * ImGuiHelpers.GlobalScale;

    protected override void PreDrawInternal()
    {
        if (!ThemePushed)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Rounding);

            ThemePushed = true;
        }
    }
    protected override void PostDrawInternal()
    {
        if (ThemePushed)
        {
            ImGui.PopStyleVar(2);
            ThemePushed = false;
        }
    }

    protected override void DrawInternal()
    {
        if (User is null)
            return;

        // obtain the profile for this userPair.
        var toDraw = _service.GetProfile(User);
        var dispName = ShowFullUID ? User.AliasOrUID : User.AnonName;
        // Always show full UID in dev build.
#if DEBUG
        dispName = User.UID;
#endif

        var wdl = ImGui.GetWindowDrawList();
        // clip based on the region of our draw space.
        _drawHelper.RectMin = wdl.GetClipRectMin();
        _drawHelper.RectMax = wdl.GetClipRectMax();

        // Draw the plate and store if we hovered the report button.
        _drawHelper.DrawProfile(wdl, Rounding, toDraw, dispName, User, ShowFullUID, ref HoveringReportButton);
        // Close button.
        CloseButton(wdl);
        CkGui.AttachToolTipRect(_drawHelper.CloseButtonPos, _drawHelper.CloseButtonSize, $"Close {dispName}'s Profile");
    }

    private void CloseButton(ImDrawListPtr drawList)
    {
        var btnPos = _drawHelper.CloseButtonPos;
        var btnSize = _drawHelper.CloseButtonSize;
        var col = HoveringCloseButton ? ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)) : ImGui.GetColorU32(ImGuiColors.ParsedPink);
        drawList.AddLine(btnPos, btnPos + btnSize, col, 3 * ImGuiHelpers.GlobalScale);
        drawList.AddLine(new Vector2(btnPos.X + btnSize.X, btnPos.Y), new Vector2(btnPos.X, btnPos.Y + btnSize.Y), col, 3 * ImGuiHelpers.GlobalScale);

        ImGui.SetCursorScreenPos(btnPos);
        if (ImGui.InvisibleButton($"CloseButton##ProfileClose-{User.UID}", btnSize))
            this.IsOpen = false;

        HoveringCloseButton = ImGui.IsItemHovered();
    }

    public override void OnClose()
    {
        // Clear if not showing full UID, otherwise cache it.
        if (!ShowFullUID)
            Mediator.Publish(new ClearProfileDataMessage(User));
    }
}
