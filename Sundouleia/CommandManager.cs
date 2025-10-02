using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using OtterGui.Classes;
using Sundouleia.Gui;
using Sundouleia.Gui.MainWindow;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;

namespace Sundouleia;

public sealed class CommandManager : IDisposable
{
    private const string MainCommand = "/sundouleia";
    private readonly SundouleiaMediator _mediator;
    private readonly MainConfig _config;
    private readonly AccountConfig _accountConfig;
    public CommandManager(SundouleiaMediator mediator, MainConfig config, AccountConfig accountConfig)
    {
        _mediator = mediator;
        _config = config;
        _accountConfig = accountConfig;

        // Add handlers to the main commands
        Svc.Commands.AddHandler(MainCommand, new CommandInfo(OnSundouleia)
        {
            HelpMessage = "Toggles the UI. Use with 'help' or '?' to view sub-commands.",
            ShowInHelp = true
        });
    }

    public void Dispose()
    {
        // Remove the handlers from the main commands
        Svc.Commands.RemoveHandler(MainCommand);
    }

    private void OnSundouleia(string command, string args)
    {
        var splitArgs = args.ToLowerInvariant().Trim().Split(" ", StringSplitOptions.RemoveEmptyEntries);
        // if no arguments.
        if (splitArgs.Length == 0)
        {
            // Interpret this as toggling the UI
            if (_config.HasValidSetup() && _accountConfig.HasValidSetup())
                _mediator.Publish(new UiToggleMessage(typeof(MainUI)));
            else
                _mediator.Publish(new UiToggleMessage(typeof(IntroUi)));
            return;
        }

        else if (string.Equals(splitArgs[0], "settings", StringComparison.OrdinalIgnoreCase))
        {
            if (_config.HasValidSetup())
                _mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
        }

        // if its help or ?, print help
        else if (string.Equals(splitArgs[0], "help", StringComparison.OrdinalIgnoreCase) || string.Equals(splitArgs[0], "?", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelpToChat();
        }
    }

    private void PrintHelpToChat()
    {
        Svc.Chat.Print(new SeStringBuilder().AddYellow(" -- Sundouleia Commands --").BuiltString);
        Svc.Chat.Print(new SeStringBuilder().AddCommand("/sundouleia", "Toggles the primary UI").BuiltString);
        Svc.Chat.Print(new SeStringBuilder().AddCommand("/sundouleia settings", "Toggles the settings UI window.").BuiltString);
        Svc.Chat.Print(new SeStringBuilder().AddCommand("/sundouleia account", "Toggles the account UI window.").BuiltString);
    }
}

