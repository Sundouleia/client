using CkCommons.Gui;
using CkCommons.Gui.Utility;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Microsoft.IdentityModel.Tokens;
using OtterGui.Text;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Tutorial;
using Sundouleia.Utils;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data;
using SundouleiaAPI.Hub;

namespace Sundouleia.Gui.Profiles;

public class ProfileEditorUI : WindowMediatorSubscriberBase
{
    private readonly MainHub _hub;
    private readonly ProfileService _service;
    private readonly TutorialService _guides;

    public ProfileEditorUI(ILogger<ProfileEditorUI> logger, SundouleiaMediator mediator, 
        MainHub hub, ProfileService service, TutorialService guides)
        : base(logger, mediator, "Profile Editor###SundouleiaProfile_EditorUI")
    {
        _hub = hub;
        _service = service;
        _guides = guides;

        Flags = WFlags.NoScrollbar | WFlags.NoResize;
        this.SetBoundaries(new Vector2(500, 450));

        Mediator.Subscribe<DisconnectedMessage>(this, (_) => IsOpen = false);
    }

    private Vector2 RectMin = Vector2.Zero;
    private Vector2 RectMax = Vector2.Zero;
    private PlateElement SelectedComponent = PlateElement.Plate;
    private StyleKind SelectedStyle = StyleKind.Background;

    // Update once we implement achievements and stuff i guess.
    private IEnumerable<PlateBG> UnlockedBackgrounds() => Array.Empty<PlateBG>();
    private IEnumerable<PlateBorder> UnlockedBorders() => Array.Empty<PlateBorder>();
    private IEnumerable<PlateOverlay> UnlockedOverlays() => Array.Empty<PlateOverlay>();


    protected override void PreDrawInternal()
    { }
    protected override void PostDrawInternal()
    { }

    protected override void DrawInternal()
    {
        var drawList = ImGui.GetWindowDrawList();
        RectMin = drawList.GetClipRectMin();
        RectMax = drawList.GetClipRectMax();
        var contentRegion = RectMax - RectMin;
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        // grab our profile.
        var profile = _service.GetProfile(MainHub.OwnUserData);
        var pos = new Vector2(ImGui.GetCursorScreenPos().X + contentRegion.X - 242, ImGui.GetCursorScreenPos().Y);

        var publicRef = profile.Info.IsPublic;
        if (ImGui.Checkbox("Public", ref publicRef))
            profile.Info.IsPublic = publicRef;
        CkGui.AttachToolTip("Makes your profile visible to anyone in radar interactions or radar chats. " +
            "--NL--Otherwise, only your pairs will see your profile.");
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.ProfilePublicity, ImGui.GetWindowPos(), ImGui.GetWindowSize());

        ImGui.SameLine();
        var isNsfw = profile.Info.IsNSFW;
        if (ImGui.Checkbox("Is NSFW", ref isNsfw))
            profile.Info.IsNSFW = isNsfw;
        CkGui.AttachToolTip("Your profile can be reported if the avatar or description is NSFW while this option is disabled." +
            "--SEP--If it is checked, you can post NSFW content just fine.");

        ImUtf8.SameLineInner();
        if (CkGui.IconTextButton(FAI.Edit, "Image Editor"))
            Mediator.Publish(new UiToggleMessage(typeof(ProfileAvatarEditor)));
        CkGui.AttachToolTip("Open the Avatar Image Editor to customize your profile picture further!");

        ImUtf8.SameLineInner();
        if (CkGui.IconButton(FAI.Save))
            UiService.SetUITask(async () =>
            {
                if (await _hub.UserUpdateProfileContent(profile.Info) is { } res && res.ErrorCode is SundouleiaApiEc.Success)
                    Mediator.Publish(new ClearProfileDataMessage(MainHub.OwnUserData));
            });
        CkGui.AttachToolTip("Updates your stored profile with latest information");

        // Post the image over to the right.
        drawList.AddDalamudImageRounded(profile.GetAvatarOrDefault(), pos, new(232f), 116f, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)));

        using (ImRaii.Group())
        {
            var titles = new SortedList<int, string>();
            titles.Add(0, "None");

            CkGui.ColorText("Select Title", ImGuiColors.ParsedGold);
            CkGui.HelpText("Select a title to display on your Profile.");
            if (CkGuiUtils.IntCombo("##Title", 200f, profile.Info.ChosenTitleId, out var newId, [], num => titles.GetValueOrDefault(num, string.Empty), "Still WIP..."))
                profile.Info.ChosenTitleId = newId;
            _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.SettingTitles, ImGui.GetWindowPos(), ImGui.GetWindowSize());
        }

        using (ImRaii.Disabled())
        {
            using (ImRaii.Group())
            {
                // Create a dropdown for all the different components of the Profile
                CkGui.ColorText("Select Component", ImGuiColors.ParsedGold);
                CkGui.HelpText("Select the component of the Profile you'd like to customize!");
                if (CkGuiUtils.EnumCombo("##PlateElement", 200f, SelectedComponent, out var newComponent))
                    SelectedComponent = newComponent;

                // Create a dropdown for all the different styles of the Profile
                CkGui.ColorText("Select Style", ImGuiColors.ParsedGold);
                CkGui.HelpText("Select the Style Kind from the selected component you wish to change the customization of.");
                if (CkGuiUtils.EnumCombo("##ProfileStyleKind", 200f, SelectedStyle, out var newStyle))
                    SelectedStyle = newStyle;

                // grab the reference value for the selected component and style from the profile.Info based on the currently chosen options.
                CkGui.ColorText("Customization for Section", ImGuiColors.ParsedGold);
                if (SelectedStyle is StyleKind.Background)
                {
                    CkGui.HelpText("Select the background style for your Profile!--SEP--You will only be able to see cosmetics you've unlocked from Achievements!");
                    if (CkGuiUtils.EnumCombo("##ProfileBackgroundStyle", 200f, profile.GetBackground(SelectedComponent), out var newBG, UnlockedBackgrounds()))
                        profile.SetBG(SelectedComponent, newBG);
                }
                else if (SelectedStyle is StyleKind.Border)
                {
                    CkGui.HelpText("Select the border style for your Profile!--SEP--You will only be able to see cosmetics you've unlocked from Achievements!");
                    if (CkGuiUtils.EnumCombo("##ProfileBorderStyle", 200f, profile.GetBorder(SelectedComponent), out var newBorder, UnlockedBorders()))
                        profile.SetBorder(SelectedComponent, newBorder);
                }
                else if (SelectedStyle is StyleKind.Overlay)
                {
                    CkGui.HelpText("Select the overlay style for your Profile!--SEP--You will only be able to see cosmetics you've unlocked from Achievements!");
                    if (CkGuiUtils.EnumCombo("##ProfileOverlayStyle", 200f, profile.GetOverlay(SelectedComponent), out var newOverlay, UnlockedOverlays()))
                        profile.SetOverlay(SelectedComponent, newOverlay);
                }
            }
        }
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.CustomizingProfile, ImGui.GetWindowPos(), ImGui.GetWindowSize());
        CkGui.AttachToolTip("Customization is WIP as we wait for any digital artists to help Sundouleia with asset creation!");

        // below this, we should draw out the description editor
        ImGui.AlignTextToFramePadding();
        CkGui.ColorText("Description", ImGuiColors.ParsedGold);
        using (ImRaii.Disabled(profile.Info.Disabled))
        {
            var refText = profile.Info.Description.IsNullOrEmpty() ? "No Description Set..." : profile.Info.Description;
            var size = new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing());
            if (ImGui.InputTextMultiline("##pfpDescription", ref refText, 1000, size))
                profile.Info.Description = refText;
        }
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.ProfileDescription, ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            () => Mediator.Publish(new ProfileOpenMessage(MainHub.OwnUserData)));
        if (profile.Info.Disabled || !MainHub.Reputation.ProfileEditing)
            CkGui.AttachToolTip("You're Profile Customization Access has been Revoked!--SEP--You will not be able to edit your Profile Description!");

        // draw the plate preview buttons.
        var width = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2;

        if (CkGui.IconTextButton(FAI.Expand, "Preview Profile", ImGui.GetContentRegionAvail().X))
            Mediator.Publish(new ProfileOpenMessage(MainHub.OwnUserData));
        CkGui.AttachToolTip("Preview your profile in a separate window!");
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.ProfilePreview, ImGui.GetWindowPos(), ImGui.GetWindowSize(), () => Mediator.Publish(new UiToggleMessage(typeof(ProfileAvatarEditor))));
    }
}
