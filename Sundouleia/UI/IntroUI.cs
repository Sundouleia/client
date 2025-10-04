using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Tutorial;
using Sundouleia.WebAPI;

namespace Sundouleia.Gui;

/// <summary> The introduction UI that will be shown the first time that the user starts the plugin. </summary>
public class IntroUi : WindowMediatorSubscriberBase
{
    private readonly MainHub _hub;
    private readonly MainConfig _config;
    private readonly ServerConfigManager _serverConfigs;
    private readonly TutorialService _guides;

    private bool ThemePushed = false;
    private bool _readFirstPage = false; // mark as false so nobody sneaks into official release early.
    private Task? _fetchAccountDetailsTask = null;
    private Task? _initialAccountCreationTask = null;
    private string _secretKey = string.Empty;

    public IntroUi(ILogger<IntroUi> logger, SundouleiaMediator mediator, MainHub mainHub,
        MainConfig config, ServerConfigManager serverConfigs, TutorialService guides)
        : base(logger, mediator, "Welcome to Sundouleia! ♥")
    {
        _hub = mainHub;
        _config = config;
        _serverConfigs = serverConfigs;
        _guides = guides;

        IsOpen = false;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        AllowPinning = false;
        AllowClickthrough = false;

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(600, 500),
            MaximumSize = new Vector2(600, 1000),
        };
        Flags = WFlags.NoScrollbar;

        Mediator.Subscribe<SwitchToMainUiMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) => IsOpen = true);
    }

    protected override void PreDrawInternal()
    {
        if (!ThemePushed)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(ImGui.GetStyle().WindowPadding.X, 0));
            ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.331f, 0.081f, 0.169f, .803f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.579f, 0.170f, 0.359f, 0.828f));

            ThemePushed = true;
        }
    }

    protected override void PostDrawInternal()
    {
        if (ThemePushed)
        {
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(2);
            ThemePushed = false;
        }
    }

    protected override void DrawInternal()
    {
        // if the user has not accepted the agreement and they have not read the first page,
        // Then show the first page (everything in this if statement)
        if (!_config.Current.AcknowledgementUnderstood && !_readFirstPage)
            DrawWelcomePage();
        // if they have read the first page but not yet created an account, we will need to present the account setup page for them.
        else if (!_config.Current.AcknowledgementUnderstood && _readFirstPage)
            DrawAcknowledgement();
        // if the user has read the acknowledgements and the server is not alive, display the account creation window.
        else if (!MainHub.IsServerAlive || !_serverConfigs.HasValidAccount())
            DrawAccountSetup();
        // otherwise, if the server is alive, meaning we are validated, then boot up the main UI.
        else
        {
            _logger.LogDebug("Switching to main UI");
            Mediator.Publish(new SwitchToMainUiMessage());
            IsOpen = false;
        }
    }

    private void DrawWelcomePage()
    {
        CkGui.FontText("Welcome to Sundouleia!", UiFontService.UidFont);

        ImGui.Separator();
        ImGui.TextWrapped("Sundouleia is a plugin that revives player data syncronization in a manner that aims " +
            "to keep its functionality for the betterment of community health and longevity.");

        CkGui.SeparatorSpaced(ImGuiColors.ParsedGold.ToUint());

        ImGui.TextWrapped("If you are even thinking about using this please know that you are going to be prone to " +
            "errors, this is still in the process of being debugged, and file transfers do not work yet.");

        CkGui.SeparatorSpaced(ImGuiColors.ParsedGold.ToUint());

        CkGui.FontText("Plugin Features:", UiFontService.UidFont);
        if (ImGui.IsItemClicked()) 
            _readFirstPage = true;

        CkGui.ColorText("- Profiles", ImGuiColors.ParsedGold);
        CkGui.TextInline("Fully Customizable Sundouleia AdventurePlate-Like Profiles!", false);

        CkGui.ColorText("- Pair Grouping", ImGuiColors.ParsedGold);
        CkGui.TextInline("Arrange your added pairs into various groups to help change others in bulk!", false);

        CkGui.ColorText("- Pair Requests", ImGuiColors.ParsedGold);
        CkGui.TextInline("Can send requests given only one code now, and accept or decline any pending!", false);

        CkGui.ColorText("- Radar System", ImGuiColors.ParsedGold);
        CkGui.TextInline("Radars embed sundouleia in-world, allowing you to meet others in your zone!", false);

        CkGui.ColorText("- Radar Chat", ImGuiColors.ParsedGold);
        CkGui.TextInline("World-Zone localized chats to converse with other users in!", false);

        ImGui.TextWrapped("Clicking the big Plugin Features text above will advance you to acknowledgements.");
    }

    private void DrawAcknowledgement()
    {
        CkGui.FontTextCentered("Acknowledgement Of Usage & Privacy", UiFontService.UidFont);
        ImGui.Separator();
        CkGui.FontTextCentered("YOU WILL ONLY SEE THIS PAGE ONCE", UiFontService.UidFont, ImGuiColors.DalamudRed);
        CkGui.FontTextCentered("READ CAREFULLY BEFORE PROCEEDING", UiFontService.UidFont, ImGuiColors.DalamudRed);
        ImGui.Separator();

        ImGui.TextWrapped("Being a Server-Side Plugin, there are always going to be *those* people who will try to ruin the fun for everyone.");
        ImGui.Spacing();
        CkGui.TextWrapped("As Such, by joining Sundouleia, you must acknowledge & accept the following:");

        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, ImGuiColors.DalamudRed);
        using var scrollbarSize = ImRaii.PushStyle(ImGuiStyleVar.ScrollbarSize, 12f);

        var remainingHeight = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing();
        using (ImRaii.Child("AgreementWindow", new Vector2(ImGui.GetContentRegionAvail().X, remainingHeight), true))
        {
            ImGui.Spacing();
            CkGui.ColorText("WIP:", ImGuiColors.ParsedGold);
            ImGui.TextWrapped("This area will likely be updated in the future we the rest of the plugin is fine tuned.");
            ImGui.Spacing();
            ImGui.Spacing();

            CkGui.ColorText("Assume That:", ImGuiColors.ParsedGold);
            ImGui.TextWrapped("Respect the authors descisions and judgements on what happens with your profile, " +
                "if you are breeaching the rules or not being responsible, your account can be revoked.");
            ImGui.Spacing();
            ImGui.Spacing();

            CkGui.ColorText("Predatory Behavior:", ImGuiColors.DalamudRed);
            ImGui.TextWrapped("Reports are handled carefully by our team, and taken very seriously.");
            ImGui.Spacing();
            ImGui.TextWrapped("If you notice any such behavior occuring, report it in detail. We have designed our system such that any reported players are" +
                "unaffected until the report is resolved, so they will not be able to witch hunt.");
            ImGui.Spacing();
        }

        if(ImGui.Button("I Understand, Proceed To Account Creation.", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeightWithSpacing())))
        {
            _config.Current.AcknowledgementUnderstood = true;
            _config.Save();
        }
        ImGui.Spacing();
    }

    private void DrawAccountSetup()
    {
        CkGui.FontTextCentered("Primary Account Creation", UiFontService.UidFont);
        
        CkGui.SeparatorSpaced();

        CkGui.ColorText("Generating your Primary Account", ImGuiColors.ParsedGold);
        ImGui.TextWrapped("You can ONLY PRESS THE BUTTON BELOW ONCE.");
        CkGui.ColorTextWrapped("The Primary Account is linked to all other created profiles made later.", ImGuiColors.DalamudRed);
        ImGui.TextWrapped("If you remove this profile, all other profiles will also be deleted!");
        ImGui.Spacing();


        CkGui.ColorTextFrameAligned("Generate Primary Account: ", ImGuiColors.ParsedGold);

        // Under the condition that we are not recovering an account, display the primary account generator:
        if (_secretKey.IsNullOrWhitespace())
        {
            // generate a secret key for the user and attempt initial connection when pressed.
            if (CkGui.IconTextButton(FAI.UserPlus, "Primary Account Generator (One-Time Use!)", disabled: _config.Current.ButtonUsed))
            {
                _config.Current.ButtonUsed = true;
                _config.Save();
                _fetchAccountDetailsTask = FetchAccountDetailsAsync();
            }
            // while we are awaiting to fetch the details and connect display a please wait text.
            if (_fetchAccountDetailsTask != null && !_fetchAccountDetailsTask.IsCompleted)
                CkGui.ColorTextWrapped("Fetching details, please wait...", ImGuiColors.DalamudYellow);
        }

        // Below this we will provide the user with a space to insert an existing UID & Key to
        // log back into a account they already have if they needed to reset for any reason.
        CkGui.SeparatorSpaced();

        CkGui.FontText("Does your Character already have a Primary Account?", UiFontService.UidFont);
        CkGui.ColorText("Retreive the key from where you saved it, or the discord bot, and insert it below.", ImGuiColors.ParsedGold);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * .85f);
        ImGui.InputText("Key##RefNewKey", ref _secretKey, 64);

        ImGui.Spacing();
        CkGui.ColorText("ServerState (For Debug Purposes): " + MainHub.ServerStatus, ImGuiColors.DalamudGrey);
        CkGui.ColorText("Auth Exists for Character (Debug): " + _serverConfigs.CharaHasLoginAuth(), ImGuiColors.DalamudGrey);
        if(_secretKey.Length == 64)
        {
            CkGui.ColorText("Connect with existing Key?", ImGuiColors.ParsedGold);
            ImGui.SameLine();
            if (CkGui.IconTextButton(FAI.Signal, "Yes! Log me in!", disabled: _initialAccountCreationTask is not null))
            {
                _logger.LogInformation("Creating Authentication for current character.");
                try
                {
                    if (_serverConfigs.CharaHasLoginAuth())
                        throw new InvalidOperationException("Auth already exists for current character, cannot create new Primary auth if one already exists!");
                    // if the auth does not exist for the current character, we can create a new one.
                    _serverConfigs.GenerateAuthForCurrentCharacter();

                    // set the key to that newly added authentication
                    var addedIdx = _serverConfigs.AddProfileToAccount(new()
                    {
                        ProfileLabel = $"Main Account Key - ({DateTime.Now:yyyy-MM-dd})",
                        Key = _secretKey,
                        IsPrimary = true,
                    });
                    // set the secret key for the character
                    _serverConfigs.SetProfileForLoginAuth(PlayerData.ContentId, addedIdx);
                    // run the create connections and set our account created to true
                    _initialAccountCreationTask = PerformFirstLoginAsync();
                }
                catch (Bagagwa ex)
                {
                    _logger.LogError($"Failed to create authentication for current character: {ex}");
                }
            }
            CkGui.AttachToolTip("THIS WILL CREATE YOUR PRIMARY ACCOUNT. ENSURE YOUR KEY IS CORRECT.");
        }

        if (_initialAccountCreationTask is not null && !_initialAccountCreationTask.IsCompleted)
            CkGui.ColorTextWrapped("Attempting to connect for First Login, please wait...", ImGuiColors.DalamudYellow);
    }

    private async Task PerformFirstLoginAsync()
    {
        try
        {
            _logger.LogInformation("Attempting to connect to the server for the first time.");
            await _hub.Connect();
            _logger.LogInformation("Connection Attempt finished, marking account as created.");
            if (MainHub.IsConnected)
            {
                _guides.StartTutorial(TutorialType.MainUi);
            }
            _config.Save(); // save the configuration
        }
        catch (Bagagwa ex)
        {
            _logger.LogError(ex, "Failed to connect to the server for the first time.");
            _config.Save();
        }
        finally
        {
            _initialAccountCreationTask = null;
        }
    }

    private async Task FetchAccountDetailsAsync()
    {
        try
        {
            if (!_serverConfigs.CharaHasLoginAuth())
                _serverConfigs.GenerateAuthForCurrentCharacter();

            // Begin by fetching the account details for the player. If this fails we will throw to the catch statement and perform an early return.
            var accountDetails = await _hub.FetchFreshAccountDetails();

            // if we are still in the try statement by this point we have successfully retrieved our new account details.
            // This means that we can not create the new authentication and validate our account as created.

            // However, if an auth already exists for the current content ID, and we are trying to create a new primary account, this should not be possible, so early throw.
            if (_serverConfigs.CharaHasValidLoginAuth())
                throw new InvalidOperationException("Auth already exists, cannot create new Primary auth if one already exists!");

            // set the key to that newly added authentication
            var addedIdx = _serverConfigs.AddProfileToAccount(new()
            {
                ProfileLabel = $"Main Account Key - ({DateTime.Now:yyyy-MM-dd})",
                UserUID = accountDetails.Item1,
                Key = accountDetails.Item2,
                IsPrimary = true,
                HadValidConnection = true,
            });
            // create the new secret key object to store.
            _serverConfigs.SetProfileForLoginAuth(PlayerData.ContentId, addedIdx);
            _config.Save();
            // Log the details.
            _logger.LogInformation("UID: " + accountDetails.Item1);
            _logger.LogInformation("Secret Key: " + accountDetails.Item2);
            _logger.LogInformation("Fetched Account Details Successfully and finished creating Primary Account.");
        }
        catch (Bagagwa)
        {
            // Log the error
            _logger.LogError("Failed to fetch account details and create the primary authentication. Performing early return.");
            _config.Current.ButtonUsed = false;
            _config.Save();
            // set the task back to null and return.
            _fetchAccountDetailsTask = null;
            return;
        }

        // Next step is to attempt a initial connection to the server with this now primary authentication.
        // If it suceeds then it will mark the initialConnectionSuccessful flag to true (This is done in the connection function itself)
        try
        {
            _logger.LogInformation("Attempting to connect to the server for the first time.");
            await _hub.Connect();
            _logger.LogInformation("Connection Attempt finished.");

            if (MainHub.IsConnected)
                _guides.StartTutorial(TutorialType.MainUi);
        }
        catch (Bagagwa ex)
        {
            _logger.LogError(ex, "Failed to connect to the server for the first time.");
        }
        finally
        {
            _fetchAccountDetailsTask = null;
        }
    }
}
