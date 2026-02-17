using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;
using Sundouleia.Services.Tutorial;
using Sundouleia.Utils;
using Sundouleia.WebAPI;

namespace Sundouleia.Gui;

/// <summary> The introduction UI that will be shown the first time that the user starts the plugin. </summary>
public class IntroUi : WindowMediatorSubscriberBase
{
    private enum IntroUiPage : byte
    {
        Welcome             = 0,   
        AttributionsAbout   = 1,
        UsageAgreement      = 2,
        CacheSetup          = 3,
        AccountSetup        = 4,
        Initialized         = 5
    }

    private readonly MainHub _hub;
    private readonly MainConfig _config;
    private readonly AccountManager _accounts;
    private readonly UiDataStorageShared _fileCacheShared;
    private readonly TutorialService _guides;

    private IntroUiPage _currentPage = IntroUiPage.Welcome;
    private IntroUiPage _furthestPage = IntroUiPage.Welcome;
    private bool ThemePushed = false;
    private string _recoveryKey = string.Empty;

    public IntroUi(ILogger<IntroUi> logger, SundouleiaMediator mediator, MainHub mainHub, 
        MainConfig config, AccountManager accounts, UiDataStorageShared fileCacheShared, 
        TutorialService guides)
        : base(logger, mediator, "###SundouleiaWelcome")
    {
        _hub = mainHub;
        _config = config;
        _accounts = accounts;
        _fileCacheShared = fileCacheShared;
        _guides = guides;

        this.PinningClickthroughFalse();
        this.SetBoundaries(new(630, 800));

        ShowCloseButton = false;
        RespectCloseHotkey = false;
        Flags = WFlags.NoScrollbar | WFlags.NoResize;

        Mediator.Subscribe<SwitchToMainUiMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) => IsOpen = true);

        // Make initial page assumptions.
        if (!_config.Current.AcknowledgementUnderstood)
        {
            _currentPage = IntroUiPage.Welcome;
            _furthestPage = IntroUiPage.Welcome;
        }
        // if the user has read the acknowledgements and the server is not alive, display the account creation window.
        else if (!_config.HasValidCacheFolderSetup())
        {
            _currentPage = IntroUiPage.CacheSetup;
            _furthestPage = IntroUiPage.CacheSetup;
        }
        else if (!MainHub.IsServerAlive || !_accounts.HasValidAccount())
        {
            _currentPage = IntroUiPage.AccountSetup;
            _furthestPage = IntroUiPage.AccountSetup;
        }
        else
        {
            _currentPage = IntroUiPage.Initialized;
            _furthestPage = IntroUiPage.Initialized;
        }
    }

    protected override void PreDrawInternal()
    {
        // Center window on first appearance.
        // ImGui.SetNextWindowPos(new((ImGui.GetIO().DisplaySize.X - Size!.Value.X) / 2, (ImGui.GetIO().DisplaySize.Y - Size!.Value.Y) / 2), ImGuiCond.Appearing);

        if (!ThemePushed)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 12f);
            ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, ImGui.GetColorU32(ImGuiCol.TitleBg));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, ImGui.GetColorU32(ImGuiCol.TitleBg));
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
        if (_furthestPage is IntroUiPage.Initialized)
        {
            _logger.LogDebug("Switching to main UI");
            Mediator.Publish(new SwitchToMainUiMessage());
            IsOpen = false;
            return;
        }


        // Obtain the image wrap for the introduction screen header to draw it based on the current position via scaled ratio.
        var pos = ImGui.GetCursorScreenPos();
        var wdl = ImGui.GetWindowDrawList();
        var winClipX = ImGui.GetWindowContentRegionMin().X / 2;
        // push clip rect.
        var winPadding = ImGui.GetStyle().WindowPadding;
        var minPos = wdl.GetClipRectMin();
        var maxPos = wdl.GetClipRectMax();

        // Push padding.
        var expandedMin = minPos - new Vector2(winClipX, 0); // Extend the min boundary to include the padding
        var expandedMax = maxPos + new Vector2(winClipX, 0); // Extend the max boundary to include the padding
        wdl.PushClipRect(expandedMin, expandedMax, false);
        var availX = expandedMax.X - expandedMin.X;

        // Grab image & get ratio.
        var headerImg = CosmeticService.CoreTextures.Cache[CoreTexture.WelcomeOverlay];

        // Scale the headerImage size to fit within the window size while maintaining aspect ratio.
        var scaledRatio = availX / headerImg.Size.X;
        var scaledSize = headerImg.Size * scaledRatio;
        // Draw out the welcome image over this area.
        wdl.AddDalamudImage(headerImg, expandedMin, scaledSize);

        // Validate the button.
        ImGui.SetCursorScreenPos(expandedMin);
        if (_furthestPage is IntroUiPage.Welcome)
        {
            if (ImGui.InvisibleButton("readingSkillCheck", scaledSize) && _currentPage == IntroUiPage.Welcome)
            {
                _currentPage = IntroUiPage.AttributionsAbout;
                _furthestPage = IntroUiPage.AttributionsAbout;
            }
        }
        else
        {
            ImGui.Dummy(scaledSize);
        }
        
        // Below this we can draw out the progress display, with a gradient multicolor.
        var progressH = ImUtf8.FrameHeight * 1.5f;
        var progressPos = expandedMin + new Vector2(0, scaledSize.Y - (ImUtf8.FrameHeight * 2).AddWinPadY());

        ImGui.SetCursorScreenPos(progressPos);
        using (CkRaii.ChildPaddedW("progress display", availX, ImUtf8.FrameHeight * 1.5f))
            DrawProgressDisplay();

        // Add a final gradient lining to the bottom of the progress display.
        var contentPos = expandedMin + new Vector2(0, scaledSize.Y);
        wdl.AddRectFilledMultiColor(contentPos, expandedMax, SundColor.LightAlpha.Uint(), SundColor.LightAlpha.Uint(), 0, 0);
        wdl.AddLine(contentPos, contentPos + new Vector2(scaledSize.X, 0), 0xFF000000, 1f);
        wdl.PopClipRect();

        // Draw the contents based on the page.
        ImGui.SetCursorScreenPos(contentPos);
        ImGui.Spacing();
        var contentArea = ImGui.GetContentRegionAvail() - new Vector2(0, (ImUtf8.FrameHeight + ImUtf8.ItemSpacing.Y * 3) + ImGui.GetStyle().WindowPadding.Y);

        using var style = ImRaii.PushStyle(ImGuiStyleVar.ScrollbarSize, 10f).Push(ImGuiStyleVar.ScrollbarRounding, 2f);
        using (var _ = CkRaii.Child("IntroPageContents", contentArea))
        {
            switch (_currentPage)
            {
                case IntroUiPage.Welcome:
                    PageContentsWelcome(_.InnerRegion);
                    break;
                case IntroUiPage.AttributionsAbout:
                    PageContentsAbout(_.InnerRegion);
                    break;
                case IntroUiPage.UsageAgreement:
                    PageContentsUsage(_.InnerRegion);
                    break;
                case IntroUiPage.CacheSetup:
                    PageContentsCache(_.InnerRegion);
                    break;
                case IntroUiPage.AccountSetup:
                    PageContentsAccountSetup(_.InnerRegion);
                    break;
            }
        }

        ImGui.Separator();
        // If on welcome page, do not show button.
        if (_currentPage is IntroUiPage.Welcome || _currentPage >= IntroUiPage.AccountSetup)
            return;

        var text = GetNextButtonText();
        var buttonSize = CkGui.IconTextButtonSize(FAI.ArrowRight, text);
        CkGui.SetCursorXtoCenter(buttonSize);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ImGui.GetStyle().WindowPadding.Y);
        if (CkGui.IconTextButton(FAI.ArrowRight, text, disabled: DisableButton(_currentPage)))
            DoButtonAdvancement();
    }

    private void DoButtonAdvancement()
    {
        // Perform update & action based on _currentPage condition.
        if (_currentPage != _furthestPage)
        {
            _currentPage = (IntroUiPage)(byte)_currentPage + 1;
            return;
        }

        switch(_furthestPage)
        {
            case IntroUiPage.Welcome:
                _furthestPage = IntroUiPage.AttributionsAbout;
                _currentPage = IntroUiPage.AttributionsAbout;
                break;

            case IntroUiPage.AttributionsAbout:
                _furthestPage = IntroUiPage.UsageAgreement;
                _currentPage = IntroUiPage.UsageAgreement;
                break;

            case IntroUiPage.UsageAgreement:
                _config.Current.AcknowledgementUnderstood = true;
                _config.Save();
                _furthestPage = IntroUiPage.CacheSetup;
                _currentPage = IntroUiPage.CacheSetup;
                break;

            case IntroUiPage.CacheSetup:
                if (_config.HasValidCacheFolderSetup())
                {
                    _furthestPage = IntroUiPage.AccountSetup;
                    _currentPage = IntroUiPage.AccountSetup;
                }
                break;

            case IntroUiPage.AccountSetup:
                // Attempt to generate an account. If this is successful, advance the page to initialized.
                if (_accounts.HasValidAccount())
                {
                    _furthestPage = IntroUiPage.Initialized;
                    _currentPage = IntroUiPage.Initialized;
                }
                break;
        }
    }

    private bool DisableButton(IntroUiPage page)
        => page switch
        {
            IntroUiPage.CacheSetup => !_config.HasValidCacheFolderSetup(),
            IntroUiPage.AccountSetup => !_accounts.HasValidAccount(),
            _ => false
        };

    private string GetNextButtonText()
        => _currentPage switch
        {
            IntroUiPage.AttributionsAbout => "To Usage Agreement",
            IntroUiPage.UsageAgreement => "I Understand Sundouleia's Usage & Privacy",
            IntroUiPage.CacheSetup => "Generate Main Account",
            IntroUiPage.AccountSetup => "Login to Sundouleia!",
            _ => string.Empty
        };

    private void DrawProgressDisplay()
    {
        var frameH = ImUtf8.FrameHeight;
        var buttonSize = new Vector2((ImGui.GetContentRegionAvail().X - (frameH * 4)) / 5, frameH * 1.5f);
        var offsetY = (buttonSize.Y - frameH) / 2;

        // Draw out the buttons.
        DrawSetupButton("Welcome", buttonSize, IntroUiPage.Welcome, null);
        ImGui.SameLine(0, 0);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offsetY);
        CkGui.FramedIconText(FAI.ChevronRight);

        ImGui.SameLine(0, 0);
        DrawSetupButton("About", buttonSize, IntroUiPage.AttributionsAbout, null);
        ImGui.SameLine(0, 0);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offsetY);
        CkGui.FramedIconText(FAI.ChevronRight);
        
        ImGui.SameLine(0, 0);
        DrawSetupButton("Usage", buttonSize, IntroUiPage.UsageAgreement, null);
        ImGui.SameLine(0, 0);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offsetY);
        CkGui.FramedIconText(FAI.ChevronRight);

        ImGui.SameLine(0, 0);
        DrawSetupButton("File Cache", buttonSize, IntroUiPage.CacheSetup, null);
        ImGui.SameLine(0, 0);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offsetY);
        CkGui.FramedIconText(FAI.ChevronRight);

        ImGui.SameLine(0, 0);
        DrawSetupButton("Create Account", buttonSize, IntroUiPage.AccountSetup, null);
    }

    private void DrawSetupButton(string label, Vector2 region, IntroUiPage page, Action? onClick)
    {
        using var dis = ImRaii.Disabled(page > _furthestPage);
        using var alpha = ImRaii.PushStyle(ImGuiStyleVar.Alpha, 1f);
        var color = _currentPage == page ? SundColor.GoldAlpha.Vec4() : ImGuiColors.ParsedGrey.Darken(.15f).WithAlpha(.5f);
        using var col = ImRaii.PushColor(ImGuiCol.Button, color).Push(ImGuiCol.ButtonHovered, color).Push(ImGuiCol.ButtonActive, color);

        if (ImGui.Button(label, region))
            _currentPage = page;
    }

    // Plugin Overview & Landing UI Contents.
    private void PageContentsWelcome(Vector2 region)
    {
        using (ImRaii.Group())
        {
            CkGui.FontText("Welcome to Sundouleia!", UiFontService.UidFont);
            ImGui.SameLine();
            var height = CkGui.CalcFontTextSize("W", UiFontService.UidFont);
            var pronounceHeight = CkGui.CalcFontTextSize("S", UiFontService.Default150Percent);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (height.Y - pronounceHeight.Y) / 2);
            CkGui.FontText("(Sun-dull-ee-uh)", UiFontService.Default150Percent, ImGuiColors.DalamudGrey3);
        }
                
        CkGui.TextWrapped("Sundouleia is a player data synchronization plugin that aims for creating an improved " +
            "approach to data synchronization and features that focuses on positive community health and longevity.");
        CkGui.ColorText("Clicking the above image will advance you to the next page.", SundColor.Gold.Uint());

        CkGui.FontText("Features:", UiFontService.Default150Percent);
        using (CkRaii.Child("FeaturesListScrollable", ImGui.GetContentRegionAvail()))
        {
            CkGui.BulletText("Profiles");
            using (ImRaii.PushIndent())
            {
                CkGui.BulletText("Formatted in an AdventurePlate-Like style", ImGuiColors.DalamudGrey2);
                CkGui.BulletText("Allows avatar image imports of any size", ImGuiColors.DalamudGrey2);
                CkGui.BulletText("Pan, Rotate, Zoom, and Crop imported images", ImGuiColors.DalamudGrey2);
                CkGui.BulletText("Customize profile Backgrounds, Borders, and Overlay styles! (WIP)", ImGuiColors.DalamudGrey2);
            }

            ImGui.Spacing();
            CkGui.BulletText("Pair Requests");
            using (ImRaii.PushIndent())
            {
                CkGui.BulletText("Only need one code to pair", ImGuiColors.DalamudGrey2);
                CkGui.BulletText("Accept requests for temporary pairing, or permanent", ImGuiColors.DalamudGrey2);
                CkGui.BulletText("Attach messages, pre-assign groups, and nickname preferences", ImGuiColors.DalamudGrey2);
            }

            ImGui.Spacing();
            CkGui.BulletText("Radar System");
            using (ImRaii.PushIndent())
            {
                CkGui.BulletText("Improve meetups & requesting with others in-world", ImGuiColors.DalamudGrey2);
                CkGui.BulletText("Quick-Send Requests to others via Context Menus / Radar UI", ImGuiColors.DalamudGrey2);
                CkGui.BulletText("Zone-Localized Chats you can speak in Anonymously", ImGuiColors.DalamudGrey2);
            }

            ImGui.Spacing();
            CkGui.BulletText("Groups");
            using (ImRaii.PushIndent())
            {
                CkGui.BulletText("Helps simplify the management of your pairs.", ImGuiColors.DalamudGrey2);
                CkGui.BulletText("Can assign pairs to one or more groups", ImGuiColors.DalamudGrey2);
                CkGui.BulletText("Customize group names, descriptions, colors, and icons", ImGuiColors.DalamudGrey2);
            }

            CkGui.BulletText("File Optimization");
            CkGui.BulletText("MCDF Export Support");
        }
    }

    // Attributions, Acknowledgements, and 'Why Sundouleia'
    private void PageContentsAbout(Vector2 region)
    {
        using var _ = CkRaii.Child("innerAbout", region, wFlags: WFlags.AlwaysVerticalScrollbar);
        CkGui.FontText("Dedications", UiFontService.Default150Percent);
        CkGui.ColorText("Ottermandias & DarkArchon were not involved in Sundouleias development. These are Dedications.", ImGuiColors.DalamudGrey);

        CkGui.BulletText("DarkArchon/Floof", SundColor.Gold.Uint());
        using (ImRaii.PushIndent())
        {
            CkGui.BulletText("For your incredible work on Mare, for helping me understand its framework years ago when I was exploring similar tech " +
                "stacks, and for being the kind of person who gave so many a free service to enjoy without asking for anything in return.", ImGuiColors.DalamudGrey2);

            CkGui.BulletText("For sharing your heartfelt reflections after Mare’s shutdown. Your wish for people to come together and build something " +
                "wonderful, instead of competing to come out on top.", ImGuiColors.DalamudGrey2);

            CkGui.BulletText("For being one of the few developers I genuinely look up to as both a mentor and a person I deeply respect.", ImGuiColors.DalamudGrey2);
        }

        ImGui.Spacing();
        CkGui.BulletText("Ottermandias", SundColor.Gold.Uint());
        using (ImRaii.PushIndent())
        {
            CkGui.BulletText("Being an incredible cornerstone to modding and plugin development through OtterGui, Penumbra, and Glamourer.", ImGuiColors.DalamudGrey2);
            CkGui.BulletText("Handling all of the chaos and unfolded after Mare's shutdown with professionalism, helping calm the sea where you could.", ImGuiColors.DalamudGrey2);
            CkGui.BulletText("For always taking actions that help improve the betterment of our community", ImGuiColors.DalamudGrey2);
            CkGui.BulletText("Being one of the few developers who I look up to as a mentor, and respect deeply as a person.", ImGuiColors.DalamudGrey2);
        }
        
        // Special Thanks.
        ImGui.Spacing();
        CkGui.FontText("Special Thanks", UiFontService.Default150Percent);
        CkGui.BulletText("XenosysVex", SundColor.Gold.Uint());
        using (ImRaii.PushIndent())
        {
            CkGui.BulletText("For interviewing DarkArchon, which both gave them closure through speaking about their passion project on an " +
                "emotional and transparent level, while also giving Sundouleia's developers the drive to create this project.", ImGuiColors.DalamudGrey2);
        }

        ImGui.Spacing();
        ImGui.Separator();
        if (!ImGui.CollapsingHeader("About Sundouleia (If you wanted to know about us)"))
            return;

        CkGui.FontText("About Sundouleia", UiFontService.UidFont);

        ImGui.Spacing();
        CkGui.ColorTextWrapped("Sundouleia was created to honor Mare’s legacy while addressing the issues that led to its takedown. " +
            "It was built from the ground up with a focus on community health, transparency, and long-term sustainability.", SundColor.Gold.Uint());

        CkGui.SeparatorSpaced();
        CkGui.ColorTextWrapped("In the interview with DarkArchon, they emphasized how vital it is to protect the people who form the cornerstones" +
            "of our modding community. Developers like Ottermandias, Goat, and others need to be protected and respected for the works they’ve given us.", ImGuiColors.DalamudGrey);

        ImGui.Spacing();
        CkGui.ColorTextWrapped("As both DarkArchon and Ottermandias reminded us, those who rush to fill these gaps often cannot be trusted. " +
            "We must allow time for the dust to settle and for solutions to emerge that are built with care, not short-sighted ambition.", ImGuiColors.DalamudGrey);

        ImGui.Spacing();
        CkGui.ColorTextWrapped("Our team has worked with Mare’s tech stack for over two years. We know its structure, its strengths, and its limits " +
            "inside and out. But when Mare fell, we made a deliberate choice not to act immediately.", ImGuiColors.DalamudGrey);

        ImGui.Spacing();
        CkGui.ColorTextWrapped("We believed the community needed time. Time to process what was lost, reflect on why it mattered, and appreciate what " +
            "had been taken for granted. We chose patience over speed, and respect over reaction. True safety, trust, and lasting stability cannot be " +
            "rushed, but comes in time, handled with care.", ImGuiColors.DalamudGrey);

        ImGui.Spacing();
        CkGui.ColorTextWrapped("At the end of the day, we’re all in the same boat. Placing too much trust in those who act without considering the " +
            "broader impact puts everyone at risk. All it takes is one careless decision, a tool that exposes too much, repeats old mistakes, or grows " +
            "beyond control, can invite harsher action from SE.", ImGuiColors.DalamudGrey);

        CkGui.SeparatorSpaced();
        CkGui.TextWrapped("Sundouleia stands on its own codebase, UI library, authentication system, object management, and secure file sharing. " +
            "It’s designed for scalability, stability, and to honor Ottermandias, DarkArchon, and the health of the XIV community.");
    }

    // Understanding Sundouleia Privacy & Usage Transparency
    private void PageContentsUsage(Vector2 region)
    {
        CkGui.FontTextCentered("READ CAREFULLY, YOU WILL ONLY SEE THIS ONCE", UiFontService.Default150Percent, ImGuiColors.DalamudRed);
        ImGui.Spacing();
        CkGui.CenterText("Sundouleia Usage & Privacy");
        using (CkRaii.FramedChildPaddedWH("UsageAndPrivacy", ImGui.GetContentRegionAvail(), 0, SundColor.Dark.Uint(), wFlags: WFlags.AlwaysVerticalScrollbar))
        {
            CkGui.FontText("Wise Word of Advice", UiFontService.Default150Percent, SundColor.Gold.Uint());
            CkGui.TextWrapped("While a lot of effort has gone into ensuring your files are secure, remember nothing is 100% secure.");
            CkGui.ColorTextWrapped("If you are uncomfortable sharing the mod files on your character, then don't pair with people you don't trust.", ImGuiColors.DalamudRed);

            ImGui.Spacing();
            CkGui.FontText("Data Distribution", UiFontService.Default150Percent, SundColor.Gold.Uint());
            CkGui.TextWrapped("Data from Penumbra, Glamourer, CPlus, Heels, Honorific, Moodles, and PetNames are shared to pairs visible to you.");
            CkGui.BulletText("Only the files used to render your on-screen actor at any given point are shared. (not full mods)");
            CkGui.BulletText("Shared files are cached on our FileHost servers temporarily for retrievals.");
            CkGui.ColorTextInline("*See Below", ImGuiColors.DalamudYellow);

            ImGui.Spacing();
            CkGui.FontText("Account Reputation", UiFontService.Default150Percent, SundColor.Gold.Uint());
            CkGui.TextWrapped("Reputation is shared across all profiles and helps prevent misuse of social features. " +
                "Valid reports may result in strikes. 3 in any category restrict access, and excessive strikes may lead to a ban.");
            CkGui.BulletText("Verification / Ban Status");
            CkGui.BulletText("Profile Viewing");
            using (ImRaii.PushIndent())
            {
                CkGui.BulletText("Controls your access to viewing other profiles.", ImGuiColors.DalamudGrey2);
                CkGui.BulletText("Used for preventing Stalker behavior.", ImGuiColors.DalamudGrey2);
            }
            CkGui.BulletText("Profile Editing");
            using (ImRaii.PushIndent())
            {
                CkGui.BulletText("Controls your access to modify your Profile.", ImGuiColors.DalamudGrey2);
                CkGui.BulletText("Used to prevent unwanted displays or behaviors in public profiles.", ImGuiColors.DalamudGrey2);
            }
            CkGui.BulletText("Radar Usage");
            using (ImRaii.PushIndent())
            {
                CkGui.BulletText("Controls your access to send / receive pings from others.", ImGuiColors.DalamudGrey2);
                CkGui.BulletText("Used as an Anti-Stalking utility and for those abusing the Radar System.", ImGuiColors.DalamudGrey2);
            }
            CkGui.BulletText("Radar Chat Usage");
            using (ImRaii.PushIndent())
            {
                CkGui.BulletText("Controls your access to send / receive radar chat messages.", ImGuiColors.DalamudGrey2);
                CkGui.BulletText("A moderation utility for others breaking chat rules or displaying undesirable behaviors.", ImGuiColors.DalamudGrey2);
            }

            ImGui.Spacing();
            CkGui.FontText("File Server Privacy", UiFontService.Default150Percent, SundColor.Gold.Uint());
            CkGui.TextWrapped("Uploaded files are cached on our servers for quick download retrieval, " +
                "helping minimize how frequently users need to re-upload their mods.");
            CkGui.BulletText("Mare used SHA1 Encryption for file transfer (and partially open-ended connections), which was vulnerable to allowing files with " +
                "malicious contents with valid hash names to be sent. Sundouleia uses BLAKE3 & SHA256 for encryption, which prevents such occurrences in file " +
                "transit, and keeps you safer!", ImGuiColors.DalamudYellow);
            CkGui.BulletText("Download Links only come from server callbacks, not calls, preventing bad actors from sending unintended download links.", ImGuiColors.DalamudRed);
            CkGui.BulletText("Files are not permanently stored on any server, and are deleted after a period of time without recent access.");
            CkGui.BulletText("Files passing through the servers are small individual pieces of mods, and no information to associate them as belonging to " +
                "any specific mod is retained on any server.");

            ImGui.Spacing();
        }
    }

    // For Setting up the File Cache.
    private void PageContentsCache(Vector2 region)
    {
        CkGui.FontText("FileCache Requirements", UiFontService.UidFont);
        CkGui.ColorText("Before proceeding you must first setup your FileCache storage location & run an initial scan.", ImGuiColors.DalamudYellow);
        
        CkGui.FramedIconText(FAI.Folder);
        CkGui.TextFrameAlignedInline("FileCache set to valid location");
        CkGui.BooleanToColoredIcon(_fileCacheShared.IsCachePathValid);
        CkGui.HelpText("Setup requires a valid FileCache Storage folder path. You can assign this via:" +
            "--SEP----COL--[@ Drive Root]--COL-- adds a \"SundouleiaCache\" folder in your drive's root directory." +
            "--NL----COL--[@ Penumbra Parent]--COL-- adds a \"SundouleiaCache\" folder in the folder your Penumbra folder is in." +
            "--NL----COL--Folder Icon Button--COL-- lets you create/assign the folder how you'd like.", SundColor.Gold.Uint());

        CkGui.FramedIconText(FAI.BarsProgress);
        CkGui.TextFrameAlignedInline("Initial Scan Completed");
        CkGui.BooleanToColoredIcon(_config.HasValidCacheFolderSetup());
        CkGui.HelpText("This runs automatically once the above path is valid. " +
            "--NL--This initial scan caches all penumbra mod file paths for quick access.");

        ImGui.Separator();
        ImGui.Spacing();

        // Draw out the folder setup area.
        _fileCacheShared.DrawFileCacheStorageBox();
        // Display the monitoring but do not display the scan buttons.
        _fileCacheShared.DrawCacheMonitoring(false, false, false);
        // Can also show the compactor, but do not allow compacting (no reason to, we are making a fresh cache).
        _fileCacheShared.DrawFileCompactor(false);
    }

    // For Generating an Account.
    private void PageContentsAccountSetup(Vector2 region)
    {
        CkGui.FontText("Account Generation", UiFontService.UidFont);

        ImGui.Text("You are not required to join the discord to login. Instead, it is generated for you below.");

        ImGui.Spacing();
        CkGui.IconText(FAI.ExclamationTriangle, ImGuiColors.DalamudYellow);
        ImUtf8.SameLineInner();
        CkGui.ColorTextWrapped("NOTE: An unclaimed account can't access profiles, chats, or radars until claimed via the bot.", ImGuiColors.DalamudYellow);

        CkGui.ColorTextFrameAligned("You can claim your account after a successful login in settings", ImGuiColors.DalamudGrey2);

        // Account Generation Area.
        ImGui.Spacing();
        ImGui.Separator();
        DrawNewAccountGeneration();
        // Account Recovery / Existing Account Setting here.
        
        ImGui.Spacing();
        ImGui.Separator();
        DrawExistingAccountRecovery();
    }

    private void DrawNewAccountGeneration()
    {
        var hasAnyProfile = _accounts.HasValidProfile();
        var generateWidth = CkGui.IconTextButtonSize(FAI.IdCardAlt, "Create Account (One-Time Use!)");
        var recoveryKeyInUse = !string.IsNullOrWhiteSpace(_recoveryKey);

        CkGui.FontText("Generate New Account", UiFontService.Default150Percent);
        var blockButton = hasAnyProfile || recoveryKeyInUse || _config.Current.ButtonUsed || UiService.DisableUI;

        CkGui.FramedIconText(FAI.UserPlus);
        CkGui.TextFrameAlignedInline("Generate:");
        ImGui.SameLine();
        if (CkGui.SmallIconTextButton(FAI.IdCardAlt, "Create Account (One-Time Use!)", disabled: blockButton))
            FetchAccountDetailsAsync();

        // Next line to display the account UID.
        var uid = string.Empty;
        var key = string.Empty;
        if (_accounts.GetMainProfile() is { } profile)
        {
            uid = profile.UserUID;
            key = profile.Key;
        }
        CkGui.FramedIconText(FAI.IdBadge);
        ImUtf8.SameLineInner();
        ImGui.SetNextItemWidth(300 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("UID##AccountUID", "Generated Account UID..", ref uid, 10, ImGuiInputTextFlags.ReadOnly);

        // Next Line to display account key.
        CkGui.FramedIconText(FAI.Key);
        ImUtf8.SameLineInner();
        ImGui.SetNextItemWidth(300 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("Key##AccountKey", "Generated Account Secret Key..", ref key, 64, ImGuiInputTextFlags.ReadOnly);
        CkGui.HelpText("SAVE THIS KEY SOMEWHERE SAFE!--NL--" +
            "--COL--THIS IS THE ONLY WAY TO RECOVER YOUR ACCOUNT IF YOU LOSE ACCESS TO IT!--COL--", ImGuiColors.DalamudRed, true);

        // if we have valid profile details but failed to connect, allow the user to attempt connection again.
        if (hasAnyProfile && !MainHub.IsConnected && _config.Current.ButtonUsed)
        {
            CkGui.FramedIconText(FAI.SatelliteDish);
            CkGui.TextFrameAlignedInline("Attempt Reconnection with Account Login:");
            ImGui.SameLine();
            if (CkGui.SmallIconTextButton(FAI.Wifi, "Connect with Login", disabled: UiService.DisableUI))
                UiService.SetUITask(TryConnectForInitialization);
        }
    }

    private void DrawExistingAccountRecovery()
    {
        CkGui.FontText("Use Existing Account / Recover Account", UiFontService.Default150Percent);
        // Warning Notice.
        CkGui.FramedIconText(FAI.ExclamationTriangle, ImGuiColors.DalamudYellow);
        CkGui.ColorTextInline("To use an existing account / login with a recovered key from the discord bot, use it here and connect.", ImGuiColors.DalamudYellow);

        CkGui.FramedIconText(FAI.ShieldHeart);
        ImUtf8.SameLineInner();
        ImGui.SetNextItemWidth(300 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##RefRecoveryKey", "Existing Account Key / Recovered Account Key..", ref _recoveryKey, 64);
        ImUtf8.SameLineInner();

        var blockButton = string.IsNullOrWhiteSpace(_recoveryKey) || _recoveryKey.Length != 64 || _accounts.HasValidProfile() || UiService.DisableUI;
        if (CkGui.IconTextButton(FAI.Wrench, "Login with Key", disabled: blockButton))
            TryLoginWithExistingKeyAsync();
        CkGui.AttachToolTip("--COL--THIS WILL CREATE YOUR PRIMARY ACCOUNT. ENSURE YOUR KEY IS CORRECT.--COL--", ImGuiColors.DalamudRed);
    }

    private async Task TryConnectForInitialization()
    {
        try
        {
            _logger.LogInformation("Attempting to connect to the server for the first time.");
            await _hub.Connect();
            _logger.LogInformation("Connection Attempt finished, marking account as created.");
            if (MainHub.IsConnected)
            {
                _furthestPage = IntroUiPage.Initialized;
                _guides.StartTutorial(TutorialType.MainUi);
            }
        }
        catch (Bagagwa ex)
        {
            _logger.LogError($"Failed to connect to the server for the first time: {ex}");
        }
    }

    private void TryLoginWithExistingKeyAsync()
    {
        UiService.SetUITask(async () =>
        {
            try
            {
                if (_accounts.HasValidProfile())
                    throw new InvalidOperationException("Cannot recover account when a valid profile already exists!");
                // Set the new profile to add using the key.
                var newProfile = new AccountProfile()
                {
                    ProfileLabel = "Primary Profile",
                    Key = _recoveryKey,
                    IsPrimary = true,
                };

                // Ensure the tracked player exists for this profile.
                if (!_accounts.CharaIsTracked())
                    _accounts.CreateTrackedPlayer();

                // Add this to the accounts and set the player profile to it.
                if (!_accounts.Profiles.Add(newProfile))
                    throw new InvalidOperationException("Failed to add the profile, as it already exists!");

                // Link this profile to the current tracked player.
                _accounts.LinkPlayerToProfile(PlayerData.CID, newProfile);
                // Attempt an initialization connection test.
                await TryConnectForInitialization();
            }
            catch (Bagagwa ex)
            {
                _logger.LogError($"Failed to recover account for current character: {ex}");
            }
        });
    }

    private void FetchAccountDetailsAsync()
    {
        UiService.SetUITask((Func<Task>)(async () =>
        {
            _config.Current.ButtonUsed = true;
            _config.Save();
            try
            {
                if (!_accounts.CharaIsTracked())
                    _accounts.CreateTrackedPlayer();

                // Begin by fetching the account details for the player. If this fails we will throw to the catch statement and perform an early return.
                var accountDetails = await _hub.FetchFreshAccountDetails();

                // if we are still in the try statement by this point we have successfully retrieved our new account details.
                // This means that we can not create the new authentication and validate our account as created.
                _logger.LogInformation("Fetched Account Details, proceeding to create Primary Account authentication.");
                // However, if an auth already exists for the current content ID, and we are trying to create a new primary account, this should not be possible, so early throw.
                if (_accounts.CharaIsAttached())
                    throw new InvalidOperationException("Auth already exists, cannot create new Primary auth if one already exists!");

                _logger.LogInformation("No existing authentication found, proceeding to create new Primary Account authentication.");
                // set the key to that newly added authentication
                var newMainProfile = new AccountProfile()
                {
                    ProfileLabel = $"Main Profile",
                    UserUID = accountDetails.Item1,
                    Key = accountDetails.Item2,
                    IsPrimary = true,
                    HadValidConnection = true,
                };

                // Add this to the accounts and set the player profile to it.
                _logger.LogInformation("Storing MainProfile data.");
                if (!_accounts.Profiles.Add(newMainProfile))
                    throw new InvalidOperationException("Failed to add MainProfile, as it already exists!");

                // Link this profile to the current tracked player.
                _accounts.LinkPlayerToProfile(PlayerData.CID, newMainProfile);
                _logger.LogInformation("MainProfile for Account stored successfully.");
                _config.Save();
                // Log the details.
                _logger.LogInformation($"MainProfile Details: [UID: {accountDetails.Item1}] [Key: {accountDetails.Item2}]");
                _logger.LogInformation("Fetched Account Details Successfully and finished creating Primary Account.");
            }
            catch (Bagagwa ex)
            {
                _logger.LogError($"Failed to fetch account details for current character: {ex}");
                _config.Current.ButtonUsed = false;
                _config.Save();
                return;
            }

            // Next, attempt an initialization connection test.
            try
            {
                await TryConnectForInitialization();
            }
            catch (Bagagwa ex)
            {
                _logger.LogError($"Failed to fetch account details and create the primary authentication. Performing early return: {ex}");
            }
        }));
    }
}
