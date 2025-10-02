using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.ImGuiNotification;
using Sundouleia.Services.Mediator;
using Microsoft.Extensions.Hosting;
using Sundouleia.PlayerClient;

namespace Sundouleia.Services;

public enum NotificationLocation
{
    Nowhere,
    Chat,
    Toast,
    Both
}

/// <summary>
///     Service responsible for displaying any sent notifications out to the user. <para />
///     If possible to make this static, please do so. Also, toast variants would be nice!
/// </summary>
public class NotificationService : DisposableMediatorSubscriberBase, IHostedService
{
    private readonly MainConfig _mainConfig;
    public NotificationService(ILogger<NotificationService> logger, SundouleiaMediator mediator, MainConfig mainConfig) 
        : base(logger, mediator)
    {
        _mainConfig = mainConfig;

        Mediator.Subscribe<NotificationMessage>(this, ShowNotification);
    }


    private void PrintErrorChat(string? message)
    {
        var se = new SeStringBuilder().AddText("[Sundouleia] Error: " + message);
        Svc.Chat.PrintError(se.BuiltString);
    }

    private void PrintInfoChat(string? message)
    {
        var se = new SeStringBuilder().AddText("[Sundouleia] Info: ").AddItalics(message ?? string.Empty);
        Svc.Chat.Print(se.BuiltString);
    }

    private void PrintWarnChat(string? message)
    {
        var se = new SeStringBuilder().AddText("[Sundouleia] ").AddUiForeground("Warning: " + (message ?? string.Empty), 31).AddUiForegroundOff();
        Svc.Chat.Print(se.BuiltString);
    }

    private void ShowChat(NotificationMessage msg)
    {
        switch (msg.Type)
        {
            case NotificationType.Info:
            case NotificationType.Success:
            case NotificationType.None:
                PrintInfoChat(msg.Message);
                break;

            case NotificationType.Warning:
                PrintWarnChat(msg.Message);
                break;

            case NotificationType.Error:
                PrintErrorChat(msg.Message);
                break;
        }
    }

    private void ShowNotification(NotificationMessage msg)
    {
        Logger.LogInformation(msg.ToString());

        switch (msg.Type)
        {
            case NotificationType.Info:
            case NotificationType.Success:
            case NotificationType.None:
                ShowNotificationLocationBased(msg, _mainConfig.Current.InfoNotification);
                break;

            case NotificationType.Warning:
                ShowNotificationLocationBased(msg, _mainConfig.Current.WarningNotification);
                break;

            case NotificationType.Error:
                ShowNotificationLocationBased(msg, _mainConfig.Current.ErrorNotification);
                break;
        }
    }

    private void ShowNotificationLocationBased(NotificationMessage msg, NotificationLocation location)
    {
        switch (location)
        {
            case NotificationLocation.Toast:
                ShowToast(msg);
                break;

            case NotificationLocation.Chat:
                ShowChat(msg);
                break;

            case NotificationLocation.Both:
                ShowToast(msg);
                ShowChat(msg);
                break;

            case NotificationLocation.Nowhere:
                break;
        }
    }

    private void ShowToast(NotificationMessage msg)
    {
        Svc.Notifications.AddNotification(new Notification()
        {
            Content = msg.Message ?? string.Empty,
            Title = msg.Title,
            Type = msg.Type,
            Minimized = false,
            InitialDuration = msg.TimeShownOnScreen ?? TimeSpan.FromSeconds(3)
        });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Notification Service is starting.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Notification Service is stopping.");
        return Task.CompletedTask;
    }
}
