using CkCommons;
using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Downloader;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using OtterGui.Classes;
using Sundouleia.Gui;
using Sundouleia.Gui.MainWindow;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;
using System.Globalization;

namespace Sundouleia;

public sealed class CommandManager : IDisposable
{
    private const string MainCommand = "/sundouleia";
    private const string ActionCommand = "/sund";
    private const string ChatCommand = "/schat";

    private CommandParser _parser;

    private readonly ILogger<CommandManager> _logger;
    private readonly SundouleiaMediator _mediator;
    private readonly MainHub _hub;
    private readonly MainConfig _config;
    private readonly AccountConfig _account;
    private readonly FolderConfig _folders;
    private readonly GroupsManager _groups;
    private readonly RadarManager _radar;
    private readonly RequestsManager _requests;
    private readonly SundesmoManager _sundesmos;

    public CommandManager(ILogger<CommandManager> logger, SundouleiaMediator mediator, MainHub hub,
        MainConfig config, AccountConfig account, FolderConfig folders, GroupsManager groups,
        RadarManager radar, RequestsManager requests, SundesmoManager sundesmos)
    {
        _logger = logger;
        _mediator = mediator;
        _hub = hub;
        _config = config;
        _account = account;
        _folders = folders;
        _groups = groups;
        _radar = radar;
        _requests = requests;
        _sundesmos = sundesmos;

        // Init the parser with our builder
        _parser = new CommandParser(InitDefinitions());

        // Add handlers to the main commands
        Svc.Commands.AddHandler(MainCommand, new CommandInfo(OnSundouleia) { HelpMessage = "Toggles the UI. Use with 'help' or '?' to view sub-commands." });
        Svc.Commands.AddHandler(ActionCommand, new CommandInfo(OnSund) { HelpMessage = "Displays a help guide in chat on various action commands." });
        Svc.Commands.AddHandler(ChatCommand, new CommandInfo(OnChat) { HelpMessage = "Set or talk through a Sundouleia chat channel. (In Development)" });
    }

    public void Dispose()
    {
        // Remove the handlers from the main commands
        Svc.Commands.RemoveHandler(MainCommand);
        Svc.Commands.RemoveHandler(ActionCommand);
        Svc.Commands.RemoveHandler(ChatCommand);
    }

    private void OnSundouleia(string command, string args)
    {
        var splitArgs = args.ToLowerInvariant().Trim().Split(" ", StringSplitOptions.RemoveEmptyEntries);
        // if no arguments.
        if (splitArgs.Length == 0)
        {
            // Interpret this as toggling the UI
            if (_config.HasValidSetup() && _account.IsConfigValid())
                _mediator.Publish(new UiToggleMessage(typeof(MainUI)));
            else
                _mediator.Publish(new UiToggleMessage(typeof(IntroUi)));
            return;
        }
        else if (string.Equals(splitArgs[0], "settings", StringComparison.OrdinalIgnoreCase))
        {
            if (_config.HasValidSetup())
                _mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
            return;
        }
        else if (string.Equals(splitArgs[0], "account", StringComparison.OrdinalIgnoreCase))
        {
            if (_config.HasValidSetup())
                _mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
            return;
        }
#if DEBUG
        else if (string.Equals(splitArgs[0], "intro", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(IntroUi)));
            return;
        }
#endif

        Svc.Chat.Print(new SeStringBuilder().AddYellow(" -- Sundouleia Commands --").BuiltString);
        Svc.Chat.Print(new SeStringBuilder().AddRed("Note that chat command logic is still being implemented!").BuiltString);
        Svc.Chat.Print(new SeStringBuilder().AddCommand("/sundouleia", "Toggle UI").BuiltString);
        Svc.Chat.Print(new SeStringBuilder().AddCommand("/sundouleia settings", "Open Settings").BuiltString);
        Svc.Chat.Print(new SeStringBuilder().AddCommand("/sundouleia account", "Open Account Manager").BuiltString);
    }

    private void OnChat(string command, string arguments)
    {
        Svc.Chat.PrintError(string.Format(CultureInfo.InvariantCulture, "[Sundouleia] Chat Functionality not yet added."));
    }


    private void OnSund(string command, string arguments)
    {
        // Parse the command result
        var res = _parser.ParseArguments(arguments);
        if (res.Result is not ParseOutcome.Success || res.ParsedData is null)
        {
            ShowSundCmdHelp(arguments, res);
            return;
        }

        // Ensure claimed account for request sending
        if (res.ParsedData.Definition.Entity == "request" && res.ParsedData.MatchedAction == "send" && !MainHub.Reputation.IsVerified)
        {
            Svc.Chat.PrintError(new SeStringBuilder().AddText("Sundouleia", 527, true).AddText(" You must claim an account to use send commands!").BuiltString);
            return;
        }

        // Execute command logic
        switch (res.ParsedData.Definition.Entity)
        {
            // Request Logic
            case "request" when res.ParsedData.MatchedAction == "send":
                HandleSendRequest(res.ParsedData); 
                break;
            case "request" when res.ParsedData.MatchedAction == "accept":
                HandleRequestResponse(res.ParsedData, true);
                break;
            case "request" when res.ParsedData.MatchedAction == "reject":
                HandleRequestResponse(res.ParsedData, false);
                break;
            // Group logic
            case "group":
                HandleGroupLogic(res.ParsedData);
                break;
            // Folder Logic
            case "folder":
                HandleFolderLogic(res.ParsedData);
                break;
        }
    }

    private void HandleSendRequest(ParsedCommand parsed)
    {
        // Fail if we are in a UIBlocking task (Already sending one) ((Prevent macro spam))
        if (UiService.DisableUI)
            return;

        var targets = ResolveTargets(parsed.Positionals[0].ToLowerInvariant()).ToList();
        if (targets.Count is 0)
            return;

        var isTemp = !parsed.Flags.Contains("-aspermanent");
        var message = parsed.Params.TryGetValue("-msg", out var msg) || parsed.Params.TryGetValue("-message", out msg) ? msg[0] : string.Empty;
        var details = new RequestDetails(isTemp, message, LocationSvc.WorldId, LocationSvc.Current.TerritoryId);
        // Single Send
        UiService.SetUITask(async () =>
        {
            if (targets.Count is 1)
            {
                var res = await _hub.UserSendRequest(new(targets[0], details)).ConfigureAwait(false);
                if (res.ErrorCode is not SundouleiaApiEc.Success || res.Value is null)
                    _logger.LogWarning($"Failed to send off request: {res.ErrorCode}");
                else
                {
                    _requests.AddNewRequest(res.Value);
                    _radar.RefreshUser(targets[0]);
                }
            }
            else
            {
                var res = await _hub.UserSendRequests(new(targets, details)).ConfigureAwait(false);
                if (res.ErrorCode is not SundouleiaApiEc.Success || res.Value is null)
                    _logger.LogWarning($"Failed to send off requests: {res.ErrorCode}");
                else
                {
                    _requests.AddNewRequest(res.Value);
                    _radar.RefreshUsers();
                }
            }
        });
    }

    private unsafe IEnumerable<UserData> ResolveTargets(string positional)
    {
        try
        {
            return positional.ToLowerInvariant() switch
            {
                "nearby" => GetNearbyTargets(),
                "ft" or "focustarget" => ResolveTarget(true),
                "t" or "target" => ResolveTarget(false),
                _ => []
            };
        }
        catch (Bagagwa ex)
        {
            _logger.LogError($"Exception while resolving targets: {ex}");
            return [];
        }

        IEnumerable<UserData> GetNearbyTargets()
        {
            var nearby = _radar.RadarUsers.Where(r => r.CanSendRequests);
            nearby = nearby.Where(u =>
            {
                if (!CharaObjectWatcher.RenderedCharas.Contains(u.Address))
                    return false;

                return PlayerData.DistanceTo(((Character*)u.Address)->Position) <= 5;
            });
            return nearby.Select(u => new UserData(u.UID));
        }
        ;

        IEnumerable<UserData> ResolveTarget(bool isFocus)
        {
            var target = isFocus ? TargetSystem.Instance()->FocusTarget : TargetSystem.Instance()->Target;
            // Return if a match was found that was valid
            return target != null && _radar.RadarUsers.FirstOrDefault(u => u.CanSendRequests && u.Address == (nint)target) is { } match
                ? [new UserData(match.UID)] : [];
        }
    }

    private void HandleRequestResponse(ParsedCommand parsed, bool isAccept)
    {
        var relatedRequests = (parsed.Positionals[0].ToLowerInvariant() switch
        {
            "area" => _requests.Incoming.Where(r => r.SentFromCurrentArea(LocationSvc.WorldId, LocationSvc.Current.TerritoryId)),
            "world" => _requests.Incoming.Where(r => r.SentFromWorld(LocationSvc.WorldId)),
            "all" => _requests.Incoming,
            _ => []
        }).ToList();

        // if none were filtered, abort.
        if (relatedRequests.Count is 0)
            return;
        
        // Accept or reject the requests.
        UiService.SetUITask(async () =>
        {
            if (relatedRequests.Count is 1)
            {
                var respondingTo = relatedRequests[0];
                if (isAccept)
                {
                    // If we should overwrite the accept for permanent or temporary
                    var asTemp = parsed.Flags.Contains("-astemp") || !parsed.Flags.Contains("-aspermanent");
                    var res = await _hub.UserAcceptRequest(new(new(respondingTo.SenderUID), asTemp)).ConfigureAwait(false);
                    // If already paired, we should remove the request from the manager.
                    if (res.ErrorCode is SundouleiaApiEc.AlreadyPaired)
                    {
                        _requests.RemoveRequest(respondingTo);
                        _radar.RefreshUser(new(respondingTo.SenderUID));
                    }
                    // Otherwise, if successful, proceed with pairing operations.
                    else if (res.ErrorCode is SundouleiaApiEc.Success)
                    {
                        // Remove the request from the manager.
                        _requests.RemoveRequest(respondingTo);
                        _sundesmos.AddSundesmo(res.Value!.Pair);
                        _radar.RefreshUser(new(respondingTo.SenderUID));
                        // If they are online, mark them online.
                        if (res.Value!.OnlineInfo is { } onlineSundesmo)
                            _sundesmos.MarkSundesmoOnline(onlineSundesmo);
                    }
                    else
                        _logger.LogWarning($"Failed to accept request from {respondingTo.SenderAnonName}: {res.ErrorCode}");
                }
                else
                {
                    var res = await _hub.UserRejectRequest(new(new(respondingTo.SenderUID))).ConfigureAwait(false);
                    if (res.ErrorCode is SundouleiaApiEc.Success)
                    {
                        _requests.RemoveRequest(respondingTo);
                        _radar.RefreshUser(new(respondingTo.SenderUID));
                    }
                    else
                        _logger.LogWarning($"Failed to reject request from {respondingTo.SenderAnonName}: {res.ErrorCode}");
                }
            }
            // Multi
            else
            {
                if (isAccept)
                {
                    var asTemp = parsed.Flags.Contains("-astemp") || !parsed.Flags.Contains("-aspermanent");
                    var responses = relatedRequests.Select(r => new RequestResponse(new(r.SenderUID), asTemp)).ToList();
                    var res = await _hub.UserAcceptRequests(new(responses)).ConfigureAwait(false);
                    if (res.ErrorCode is not SundouleiaApiEc.Success || res.Value is null)
                    {
                        _logger.LogWarning($"Failed to accept bulk requests: {res.ErrorCode}");
                        if (res.ErrorCode is SundouleiaApiEc.AlreadyPaired)
                        {
                            _requests.RemoveRequests(relatedRequests);
                            _radar.RefreshUsers();
                        }
                    }
                    else
                    {
                        _requests.RemoveRequests(relatedRequests);
                        foreach (var addedPair in res.Value)
                        {
                            _sundesmos.AddSundesmo(addedPair.Pair);
                            if (addedPair.OnlineInfo is { } onlineUser)
                                _sundesmos.MarkSundesmoOnline(onlineUser);
                        }
                        // Update radar folders to reflect these changes
                        _radar.RefreshUsers();
                    }
                }
                else
                {
                    var res = await _hub.UserRejectRequests(new(relatedRequests.Select(x => new UserData(x.SenderUID)).ToList()));
                    if (res.ErrorCode is SundouleiaApiEc.Success)
                    {
                        _requests.RemoveRequests(relatedRequests);
                        _radar.RefreshUsers();
                    }
                    else
                        _logger.LogWarning($"Failed to bulk cancel outgoing requests: {res.ErrorCode}");
                }
            }
        });
    }

    private void HandleGroupLogic(ParsedCommand parsed)
    {
        Svc.Chat.PrintError(new SeStringBuilder().AddText("Sundouleia", 527, true).AddText(" Logic not implemented yet.").BuiltString);
    }

    private void HandleFolderLogic(ParsedCommand parsed)
    {
        Svc.Chat.PrintError(new SeStringBuilder().AddText("Sundouleia", 527, true).AddText(" Logic not implemented yet.").BuiltString);
    }

    #region Parsing and Help
    private void ShowSundCmdHelp(string arguments, ParseResult res)
    {
        var split = arguments.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string? entity = split.Length > 0 ? split[0] : null;
        string? action = split.Length > 1 ? split[1] : null;

        if (string.IsNullOrEmpty(entity) && string.IsNullOrEmpty(action))
        {
            Svc.Chat.Print(new SeStringBuilder().AddText("Sundouleia", 527, true).AddText(" Valid args for ").AddText("/sund ", 527).AddText("are:").BuiltString);
            Svc.Chat.Print(new SeStringBuilder().AddCommand("request", "automates the process of requests.").BuiltString);
            Svc.Chat.Print(new SeStringBuilder().AddCommand("group", "automates various interactions with groups.").BuiltString);
            Svc.Chat.Print(new SeStringBuilder().AddCommand("folder", "automates interactions with GroupFolders.").BuiltString);
            return;
        }

        // Switch based on the entity type to show entity-level or action-level help.
        switch  (entity?.ToLowerInvariant())
        {
            case "request": ShowRequestHelp(action, res.ErrorMsg);break;
            case "group":   ShowGroupHelp(action, res.ErrorMsg);  break;
            case "folder":  ShowFolderHelp(action, res.ErrorMsg); break;
            default:
                // Unknown entity → show main help with entity highlighted as invalid
                if (!string.IsNullOrEmpty(entity))
                    Svc.Chat.PrintError(new SeStringBuilder().AddText("Sundouleia", 527, true).AddText("Invalid Entity: ").AddRed(entity, true).BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddText("Sundouleia", 527, true).AddText(" Valid args for ").AddText("/sund ", 527).AddText("are:").BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddCommand("request", "automates the process of requests.").BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddCommand("group", "automates various interactions with groups.").BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddCommand("folder", "automates interactions with GroupFolders.").BuiltString);
                break;
        }
    }

    private void ShowRequestHelp(string? action, string? badArg)
    {
        // For generic help
        if (string.IsNullOrEmpty(action))
        {
            Svc.Chat.Print(new SeStringBuilder()
                .AddText("Sundouleia", 527, true)
                .AddText(" Template: ")
                .AddText("/sund request ", 527).AddYellow("<action> ").AddGreen("<target> ").AddBlue("[params]").AddText("[Flags]", 537)
                .BuiltString);
            // Show possible actions
            Svc.Chat.Print(new SeStringBuilder().AddYellow("    》 Actions: ")
                .AddText("send").AddText(", ", 527).AddText("accept").AddText(", ", 527).AddText("reject").BuiltString);
            // Show possible paramaters.
            Svc.Chat.Print(new SeStringBuilder().AddGreen("    》 Targets: ")
                .AddText("t").AddText("/", 527).AddText("target").AddText(", ", 527)
                .AddText("ft").AddText("/", 527).AddText("focustarget").AddText(", ", 527)
                .AddText("nearby").AddText(", ", 527)
                .AddText("area").AddText(", ", 527)
                .AddText("world").AddText(", ", 527)
                .AddText("all")
                .BuiltString);
            Svc.Chat.Print(new SeStringBuilder().AddBlue("    》 Params: ")
                .AddText("-astemp").AddText(", ", 527).AddText("-aspermanent").BuiltString);
            Svc.Chat.Print(new SeStringBuilder().AddBlue("    》 Flags: ")
                .AddText("-astemp").AddText(", ", 527).AddText("-aspermanent").BuiltString);
            return;
        }

        // May need to refine this further as time goes on to give more detailed errors.
        if (!string.IsNullOrEmpty(badArg))
            Svc.Chat.PrintError(new SeStringBuilder().AddText("Request command error: ").AddRed(badArg, true).BuiltString);

        switch (action.ToLowerInvariant())
        {
            case "send":
                Svc.Chat.Print(new SeStringBuilder().AddText("Sundouleia", 527, true).AddText(" Command ")
                    .AddText("send ", 527).AddText("is missing args  》").AddGreen("<target> ").AddBlue("[params] ").AddText("[Flags]", 537).BuiltString);

                Svc.Chat.Print(new SeStringBuilder().AddGreen("    》 Targets: ")
                    .AddText("t").AddText("/", 527).AddText("target").AddText(", ", 527)
                    .AddText("ft").AddText("/", 527).AddText("focustarget").AddText(", ", 527)
                    .AddText("nearby").AddText(", ", 527)
                    .AddText("area").AddText(", ", 527)
                    .AddText("world").AddText(", ", 527)
                    .AddText("all")
                    .BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddBlue("    》 Params: ")
                    .AddText("-msg").AddText("/", 527).AddText("-message").BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddBlue("    》 Flags: ")
                    .AddText("-astemp").AddText(", ", 527).AddText("-aspermanent").BuiltString);
                break;

            case "accept":
            case "reject":
                Svc.Chat.Print(new SeStringBuilder().AddText("Sundouleia", 527, true).AddText(" Command ")
                    .AddText($"{action} ", 527).AddText("is missing args  》").AddGreen("<target> ").AddText("[flags]", 537).BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddGreen("    》 Targets: ")
                    .AddText("area").AddText(", ", 527).AddText("world").AddText(", ", 527).AddText("all").BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddText("    》 Flags: ", 537)
                    .AddText("-astemp").AddText(", ", 527).AddText("-aspermanent").BuiltString);
                break;
        }
    }

    private void ShowGroupHelp(string? action, string? badArg)
    {
        // For generic help
        if (string.IsNullOrEmpty(action))
        {
            Svc.Chat.Print(new SeStringBuilder()
                .AddText("Sundouleia", 527, true)
                .AddText(" Generic Template: ")
                .AddText("/sund group ", 527).AddYellow("<action> ").AddGreen("<Name>").AddBlue("[params]").AddText("[Flags]", 537)
                .BuiltString);
            // Show possible actions
            Svc.Chat.Print(new SeStringBuilder().AddYellow("    》 Actions: ")
                .AddText("create add remove rename move merge delete(TBD)").BuiltString);

            // Show possible paramaters.
            Svc.Chat.Print(new SeStringBuilder().AddText("    》 Names, Params, Flags: ", 527).AddText("See Action Helps for info").BuiltString);
            return;
        }

        // May need to refine this further as time goes on to give more detailed errors.
        if (!string.IsNullOrEmpty(badArg))
            Svc.Chat.PrintError(new SeStringBuilder().AddText("Group command error: ").AddRed(badArg, true).BuiltString);

        switch (action.ToLowerInvariant())
        {
            case "create":
                Svc.Chat.Print(new SeStringBuilder().AddText("Sundouleia", 527, true).AddText(" Command ")
                    .AddText("/sund group create ", 527).AddText(" missing args 》").AddGreen("<Name>").AddBlue("[params]").AddText("[Flags]", 537).BuiltString);

                Svc.Chat.Print(new SeStringBuilder().AddGreen("    》 Name: ")
                    .AddText("The name of the new group")
                    .BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddBlue("    》 Optional Params: ")
                    .AddText("-parent, -linkloc, -linklocation, -dc, -world, -territory, -intendeduse, -district, " +
                        "-ward, -plot, -indoor, -icon, -iconcol, -labelcol, -framecol, -gradientcol")
                    .BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddText("    》 Optional Flags: ", 537)
                    .AddText("-showoffline -ensurefolder -indoor")
                    .BuiltString);
                break;

            case "add":
            case "remove":
                Svc.Chat.Print(new SeStringBuilder().AddText("Sundouleia", 527, true).AddText(" Command ")
                    .AddText($"/sund group {action} ", 527).AddText(" missing args 》").AddGreen("<Name> ").AddBlue("[params]").BuiltString);
                
                Svc.Chat.Print(new SeStringBuilder().AddGreen("    》 Name: ").AddText("The name of the new group").BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddBlue("    》 Params: ").AddText("-user, -users").BuiltString);
                break;

            case "move":
                Svc.Chat.Print(new SeStringBuilder().AddGreen("    》 Name(s): ").AddText("The Group(s) being moved").BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddBlue("    》 Params: ").AddText("-target").BuiltString);
                break;
            
            case "merge":
                Svc.Chat.Print(new SeStringBuilder().AddGreen("    》 Name(s): ").AddText("The Group(s) being merged").BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddBlue("    》 Params: ").AddText("-target").BuiltString);
                break;
        }
    }

    private void ShowFolderHelp(string? action, string? badArg)
    {
        // For generic help
        if (string.IsNullOrEmpty(action))
        {
            Svc.Chat.Print(new SeStringBuilder()
                .AddText("Sundouleia", 527, true)
                .AddText(" Generic Template: ")
                .AddText("/sund group ", 527).AddYellow("<action> ").AddGreen("<Name>").AddBlue("[params]").AddText("[Flags]", 537)
                .BuiltString);
            // Show possible actions
            Svc.Chat.Print(new SeStringBuilder().AddYellow("    》 Actions: ")
                .AddText("create add remove rename move merge delete(TBD)").BuiltString);

            // Show possible paramaters.
            Svc.Chat.Print(new SeStringBuilder().AddText("    》 Names, Params, Flags: ", 527).AddText("See Action Helps for info").BuiltString);
            return;
        }

        // May need to refine this further as time goes on to give more detailed errors.
        if (!string.IsNullOrEmpty(badArg))
            Svc.Chat.PrintError(new SeStringBuilder().AddText("Group command error: ").AddRed(badArg, true).BuiltString);

        switch (action.ToLowerInvariant())
        {
            case "create":
                Svc.Chat.Print(new SeStringBuilder().AddText("Sundouleia", 527, true).AddText(" Command ")
                    .AddText("/sund folder create ", 527).AddText(" missing args 》").AddGreen("<Name>").AddBlue("[params]").AddText("[Flags]", 537).BuiltString);

                Svc.Chat.Print(new SeStringBuilder().AddGreen("    》 Name: ").AddText("The name of the new folder").BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddBlue("    》 Params: ").AddText("-parent").BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddText("    》 Flags: ", 537).AddText("-ensurefolder").BuiltString);
                break;

            case "add":
            case "remove":
                Svc.Chat.Print(new SeStringBuilder().AddText("Sundouleia", 527, true).AddText(" Command ")
                    .AddText($"/sund folder {action} ", 527).AddText(" missing args 》").AddGreen("<Name> ").BuiltString);

                Svc.Chat.Print(new SeStringBuilder().AddGreen("    》 Name: ").AddText("The name of the new folder").BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddBlue("    》 Params: ").AddText("-parent").BuiltString);
                break;

            case "move":
                Svc.Chat.Print(new SeStringBuilder().AddGreen("    》 Name(s): ").AddText("The Folder(s) being moved").BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddBlue("    》 Params: ").AddText("-target").BuiltString);
                break;

            case "merge":
                Svc.Chat.Print(new SeStringBuilder().AddGreen("    》 Name: ").AddText("The Group(s) being merged").BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddBlue("    》 Params: ").AddText("-target").BuiltString);
                break;
        }
    }

    private Dictionary<string, CommandDefinition> InitDefinitions()
        => new Dictionary<string, CommandDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["request:send"] = new CommandDefinition
            {
                Entity = "request",
                Action = ["send"],
                PositionalArgs = ["t", "target", "ft", "focustarget", "nearby"],
                Parameters = new HashSet<string> { "-msg", "-message" },
                Flags = new HashSet<string> { "-astemp", "-aspermanent" }
            },
            ["request:accept"] = new CommandDefinition
            {
                Entity = "request",
                Action = [ "accept" ],
                PositionalArgs = [ "area", "world", "all" ],
                Flags = new HashSet<string> { "-astemp", "-aspermanent" },
            },
            ["request:reject"] = new CommandDefinition
            {
                Entity = "request",
                Action = [ "reject" ],
                PositionalArgs = [ "area", "world", "all" ],
                Flags = new HashSet<string> { "-astemp", "-aspermanent" },
            },
            ["group:create"] = new CommandDefinition
            {
                Entity = "group",
                Action = ["create"],
                // name is free-form, handled separately
                Parameters = new HashSet<string>
                {
                    "-parent",
                    "-linkloc", "-linklocation",
                    "-dc",
                    "-world",
                    "-territory",
                    "-intendeduse",
                    "-district",
                    "-ward",
                    "-plot",
                    "-indoor",
                    "-icon",
                    "-iconcol",
                    "-labelcol",
                    "-framecol",
                    "-gradientcol"
                },
                Flags = new HashSet<string> { "-showOffline", "-ensurefolder", "-indoor" }
            },
            ["group:add"] = new CommandDefinition
            {
                Entity = "group",
                Action = ["add", "remove"],
                Parameters = new HashSet<string> { "-user", "-users" }
            },
            ["group:remove"] = new CommandDefinition
            {
                Entity = "group",
                Action = ["add", "remove"],
                Parameters = new HashSet<string> { "-user", "-users" }
            },
            ["group:move"] = new CommandDefinition
            {
                Entity = "group",
                Action = ["move"],
                Parameters = new HashSet<string> { "-target" }
            },
            ["group:merge"] = new CommandDefinition
            {
                Entity = "group",
                Action = ["merge"],
                Parameters = new HashSet<string> { "-target" }
            },
            ["folder:create"] = new CommandDefinition
            {
                Entity = "folder",
                Action = ["create"],
                Parameters = new HashSet<string> { "-parent", },
            },
            ["folder:add"] = new CommandDefinition
            {
                Entity = "folder",
                Action = ["add", "remove"],
                Parameters = new HashSet<string> { "-child", "-children" },
            },
            ["folder:remove"] = new CommandDefinition
            {
                Entity = "folder",
                Action = ["add", "remove"],
                Parameters = new HashSet<string> { "-child", "-children" },
                Flags = new HashSet<string> { "-dissolve" }
            },
            ["folder:move"] = new CommandDefinition
            {
                Entity = "folder",
                Action = ["move"],
                Parameters = new HashSet<string> { "-target" }
            },
            ["folder:merge"] = new CommandDefinition
            {
                Entity = "folder",
                Action = ["merge"],
                // one or more folders
                Parameters = new HashSet<string> { "-target" }
            },
        };
    #endregion Parsing and Help
}

