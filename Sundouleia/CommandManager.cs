using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using OtterGui.Classes;
using Sundouleia.Gui;
using Sundouleia.Gui.MainWindow;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using System.Globalization;

namespace Sundouleia;

// Helps define the outline for commands.
public sealed class CommandDefinition
{
    public string Entity { get; init; } = string.Empty; // "group", "folder", "request"
    public IReadOnlyList<string> Action { get; init; } = Array.Empty<string>(); // "create", "add", "move", etc.
    public IReadOnlyList<string> PositionalArgs { get; init; } = Array.Empty<string>();
    public IReadOnlySet<string> Parameters { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> Flags { get; init; } = new HashSet<string>();
}

// Stores the data from parsed command strings.
public sealed class ParsedCommand
{
    // What definition it pulled from.
    public CommandDefinition Definition { get; init; }

    public string MatchedAction { get; set; } = string.Empty;
    public List<string> Positionals { get; } = new();
    public Dictionary<string, List<string>> Parameters { get; } = new();
    public HashSet<string> Flags { get; } = new();
}

public sealed class CommandManager : IDisposable
{
    private const string MainCommand = "/sundouleia";
    private const string ActionCommand = "/sund";
    private const string ChatCommand = "/schat";

    private Dictionary<string, CommandDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);

    private readonly SundouleiaMediator _mediator;
    private readonly MainConfig _config;
    private readonly AccountConfig _accountConfig;
    public CommandManager(SundouleiaMediator mediator, MainConfig config, AccountConfig accountConfig)
    {
        _mediator = mediator;
        _config = config;
        _accountConfig = accountConfig;

        // Build definitions
        BuildCommandDefinitions();

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
            if (_config.HasValidSetup() && _accountConfig.IsConfigValid())
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

    private void OnSund(string command, string arguments)
    {
        // Pass this immidiately off to the parser.
        if (ParseArguments(arguments) is not { } parsedCommand)
        {
            Svc.Logger.Error($"Error Executing Command");
            return;
        }

        // Perform logic based on the associated parsed command!
        Svc.Logger.Warning("We successfully parsed the command!");
    }

    // If we did /sund request add _____, the unparsedArguments would be 'request add ____'
    // If the returned value is null, help was already displayed, so dont bother displaying it again.
    private ParsedCommand? ParseArguments(string commandArgs)
    {
        // Split the input into a maximum of 3 parts. This gives us the entity and the action.
        // ex: "group add My Group -user John" -> ["group", "add", "My Group -user John" ]
        var split = commandArgs.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // If the split is less than 3  we should show the appropriate help text.        
        if (split.Length < 3)
        {
            Svc.Logger.Information($"Had less than 3 splits");
            switch (split.Length)
            {
                case 1: ShowHelp(split[0], string.Empty);       break;
                case 2: ShowHelp(split[0], split[1]);           break;
                default:ShowHelp(string.Empty, string.Empty);   break;
            }
            return null;
        }

        Svc.Logger.Information("had valid splits");
        var entity = split[0]; // request, group, or folder
        var action = split[1]; // send, accept, reject, create, add, remove, move, merge
        var untokenizedArgs = split[2];

        // Try find the definition so we can associate it
        var lookupKey = $"{entity}:{action}";
        if (!_definitions.TryGetValue(lookupKey, out var definition))
        {
            ShowHelp(entity, action);
            return null;
        }

        // Create the base parsed command
        var parsed = new ParsedCommand
        {
            Definition = definition,
            MatchedAction = action
        };

        // Tokenize remainder (with quotes support)
        var tokens = TokenizeCommand(untokenizedArgs);
        Svc.Logger.Information($"Tokenized Commands:\n{string.Join(" | ", tokens)}");

        int i = 0;
        while (i < tokens.Count)
        {
            // Get the correct token.
            var token = tokens[i];
            // If it was a flag, add the flag without worrying about anything else following it.
            if (definition.Flags.Contains(token))
            {
                parsed.Flags.Add(token);
                Svc.Logger.Information($"Adding Flag: {token}");
                i++;
            }
            // If a paramater, we need to wait until we fine another flag, token, or reach the end of the tokens.
            else if (definition.Parameters.Contains(token))
            {
                i++;
                var values = new List<string>();
                // Continue until we reach one of the above mentioned points, adding tokens into the values until we do.
                while (i < tokens.Count && !definition.Flags.Contains(tokens[i]) && !definition.Parameters.Contains(tokens[i]))
                {
                    values.Add(tokens[i]);
                    i++;
                }

                Svc.Logger.Information($"Applying Paramater: {string.Join(", ", values)}");
                // Insert the values of the token.
                parsed.Parameters[token] = values;
            }
            // Only add to the positional args if they exist and are contained.
            else
            {
                if (definition.PositionalArgs.Count > 0 && !definition.PositionalArgs.Contains(token, StringComparer.OrdinalIgnoreCase))
                {
                    ShowHelp(split[0], split[1], "Bad Target Type");
                    return null;
                }

                Svc.Logger.Information($"Added PositionalArg: {token}");
                parsed.Positionals.Add(token);
                i++;
            }
        }

        Svc.Logger.Information($"Finished Parse!");
        return parsed;
    }

    private void OnChat(string command, string arguments)
    {
        Svc.Chat.PrintError(string.Format(CultureInfo.InvariantCulture, "[Sundouleia] Chat Functionality not yet added."));
    }

    private void BuildCommandDefinitions()
    {
        _definitions = new Dictionary<string, CommandDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["request:send"] = new CommandDefinition
            {
                Entity = "request",
                Action = ["send"],
                PositionalArgs = ["t", "target", "ft", "focustarget", "nearby"],
                Parameters = new HashSet<string> { "-astemp", "-aspermanent", "-msg", "-message" }
            },
            ["request:accept"] = new CommandDefinition
            {
                Entity = "request",
                Action = new[] { "accept" },
                PositionalArgs = new[] { "area", "world", "all" },
                Parameters = new HashSet<string> { "-astemp", "-aspermanent" },
                Flags = new HashSet<string>()
            },
            ["request:reject"] = new CommandDefinition
            {
                Entity = "request",
                Action = new[] { "reject" },
                PositionalArgs = new[] { "area", "world", "all" },
                Parameters = new HashSet<string> { "-astemp", "-aspermanent" },
                Flags = new HashSet<string>()
            }
        };

        var groupCreate = new CommandDefinition
        {
            Entity = "group",
            Action = [ "create" ],
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
            Flags = new HashSet<string>
            {
                "-showOffline",
                "-ensurefolder",
                "-indoor"
            }
        };

        // Group Add / Remove
        var groupAddRemove = new CommandDefinition
        {
            Entity = "group",
            Action = [ "add", "remove" ],
            // group name is free-form, handled separately
            Parameters = new HashSet<string>
            {
                "-user",
                "-users"
            }
        };

        // Group Move
        var groupMove = new CommandDefinition
        {
            Entity = "group",
            Action = [ "move" ],
            // one or more group names
            Parameters = new HashSet<string> { "-target" }
        };

        // Group Merge
        var groupMerge = new CommandDefinition
        {
            Entity = "group",
            Action = [ "merge" ],
            // one or more source groups
            Parameters = new HashSet<string> { "-target" }
        };

        _definitions["group:create"] = groupCreate;
        _definitions["group:add"] = groupAddRemove;
        _definitions["group:remove"] = groupAddRemove;
        _definitions["group:move"] = groupMove;
        _definitions["group:merge"] = groupMerge;
        
        // --- Folders ----

        // Folder Create
        var folderCreate = new CommandDefinition
        {
            Entity = "folder",
            Action = [ "create" ],
            // name is free-form
            Parameters = new HashSet<string> { "-parent", },
        };

        // Folder Add / Remove
        var folderAddRemove = new CommandDefinition
        {
            Entity = "folder",
            Action = [ "add", "remove" ],
            // folder name is free-form
            Parameters = new HashSet<string>
            {
                "-child",
                "-children"
            },
            Flags = new HashSet<string>
            {
                "-dissolve" // only applies on remove
            }
        };

        // Folder Move
        var folderMove = new CommandDefinition
        {
            Entity = "folder",
            Action = [ "move" ],
            // one or more folder names
            Parameters = new HashSet<string> { "-target" }
        };

        // Folder Merge
        var folderMerge = new CommandDefinition
        {
            Entity = "folder",
            Action = [ "merge" ],
            // one or more folders
            Parameters = new HashSet<string> { "-target" }
        };

        _definitions["folder:create"] = folderCreate;
        _definitions["folder:add"] = folderAddRemove;
        _definitions["folder:remove"] = folderAddRemove;
        _definitions["folder:move"] = folderMove;
        _definitions["folder:merge"] = folderMerge;
    }

    // tokenize correctly.
    List<string> TokenizeCommand(string input)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue; // skip quote character
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    private void ShowHelp(string? entity = null, string? action = null, string? badArg = null)
    {
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
            case "request": ShowRequestHelp(action, badArg);break;
            case "group":   ShowGroupHelp(action, badArg);  break;
            case "folder":  ShowFolderHelp(action, badArg); break;
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
                .AddText("/sund request ", 527).AddYellow("<action> ").AddGreen("<target> ").AddBlue("[params]")
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
                .AddText("-astemp").AddText(", ", 527)
                .AddText("-aspermanent").AddText(", ", 527)
                .AddText("-msg").AddText("/", 527).AddText("-message")
                .BuiltString);
            return;
        }

        // May need to refine this further as time goes on to give more detailed errors.
        if (!string.IsNullOrEmpty(badArg))
            Svc.Chat.PrintError(new SeStringBuilder().AddText("Request command error: ").AddRed(badArg, true).BuiltString);

        switch (action.ToLowerInvariant())
        {
            case "send":
                Svc.Chat.Print(new SeStringBuilder().AddText("Sundouleia", 527, true).AddText(" Command ")
                    .AddText("/sund request send ", 527).AddText(" missing args 》").AddGreen("<target> ").AddBlue("[params]").BuiltString);

                Svc.Chat.Print(new SeStringBuilder().AddGreen("    》 Targets: ")
                    .AddText("t").AddText("/", 527).AddText("target").AddText(", ", 527)
                    .AddText("ft").AddText("/", 527).AddText("focustarget").AddText(", ", 527)
                    .AddText("nearby").AddText(", ", 527)
                    .AddText("area").AddText(", ", 527)
                    .AddText("world").AddText(", ", 527)
                    .AddText("all")
                    .BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddBlue("    》 Params: ")
                    .AddText("-astemp").AddText(", ", 527)
                    .AddText("-aspermanent").AddText(", ", 527)
                    .AddText("-msg").AddText("/", 527).AddText("-message")
                    .BuiltString);
                break;

            case "accept":
            case "reject":
                Svc.Chat.Print(new SeStringBuilder().AddText("Sundouleia", 527, true).AddText(" Command ")
                    .AddText($"/sund request {action} ", 527).AddText(" missing args 》").AddGreen("<target> ").AddBlue("[params]").BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddGreen("    》 Targets: ")
                    .AddText("area").AddText(", ", 527)
                    .AddText("world").AddText(", ", 527)
                    .AddText("all")
                    .BuiltString);
                Svc.Chat.Print(new SeStringBuilder().AddBlue("    》 Params: ")
                    .AddText("-astemp").AddText(", ", 527)
                    .AddText("-aspermanent").AddText(", ", 527)
                    .BuiltString);
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
}

