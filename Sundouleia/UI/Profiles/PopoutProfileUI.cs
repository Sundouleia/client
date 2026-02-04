using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Sundouleia.Gui.MainWindow;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Data;

namespace Sundouleia.Gui.Profiles;

public class PopoutProfileUi : WindowMediatorSubscriberBase
{
    private bool ThemePushed = false;

    private readonly ProfileHelper _drawHelper;
    private readonly ProfileService _service;

    private UserData? User = null;

    public PopoutProfileUi(ILogger<PopoutProfileUi> logger, SundouleiaMediator mediator,
        ProfileHelper helper, ProfileService service) : base(logger, mediator, "###PopoutProfileUI")
    {
        _drawHelper = helper;
        _service = service;
        Flags = WFlags.NoDecoration;

        Mediator.Subscribe<OpenProfilePopout>(this, (msg) =>
        {
            IsOpen = true;
            User = msg.UserData;
        });
        Mediator.Subscribe<CloseProfilePopout>(this, (msg) =>
        {
            IsOpen = false;
            User = null;
        });
    }

    private static float Rounding => 35f * ImGuiHelpers.GlobalScale;

    protected override void PreDrawInternal()
    {
        if (!ThemePushed)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Rounding);
            ThemePushed = true;
        }

        var position = MainUI.LastPos;
        position.X -= 288 * ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowPos(position);

        Flags |= WFlags.NoMove;

        var size = ImGuiHelpers.ScaledVector2(288, 576);

        ImGui.SetNextWindowSize(size);
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
        var dispName = User.AliasOrUID;

        var wdl = ImGui.GetWindowDrawList();
        _drawHelper.RectMin = wdl.GetClipRectMin();
        _drawHelper.RectMax = wdl.GetClipRectMax();
        _drawHelper.DrawProfile(wdl, Rounding, toDraw, dispName, User, true);
    }
}