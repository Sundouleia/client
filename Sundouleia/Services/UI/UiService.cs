using Dalamud.Interface.Windowing;
using Sundouleia.Gui;
using Sundouleia.Gui.MainWindow;
using Sundouleia.Gui.Profiles;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Services;

/// <summary> A sealed class dictating the UI service for the plugin. </summary>
public sealed class UiService : DisposableMediatorSubscriberBase
{
    // Created windows for pop-up profile displays.
    private static readonly List<WindowMediatorSubscriberBase> _createdWindows = [];

    private readonly MainConfig _config;
    private readonly AccountConfig _accountConfig;
    private readonly UiFactory _uiFactory;
    private readonly WindowSystem _windowSystem;
    private readonly UiFileDialogService _fileService;

    // The universal UiBlocking interaction task.
    public static Task? UiTask { get; private set; }
    public static bool DisableUI => UiTask is not null && !UiTask.IsCompleted;

    // Never directly called yet, but we can process it via the Hoster using GetServices<WindowMediatorSubscriberBase>() to load all windows.
    public UiService(ILogger<UiService> logger, SundouleiaMediator mediator, MainConfig config,
        AccountConfig serverConfig, WindowSystem windowSystem, IEnumerable<WindowMediatorSubscriberBase> windows,
        UiFactory uiFactory, UiFileDialogService fileDialog) : base(logger, mediator)
    {
        _config = config;
        _accountConfig = serverConfig;
        _windowSystem = windowSystem;
        _uiFactory = uiFactory;
        _fileService = fileDialog;

        // disable the UI builder while in g-pose 
        Svc.PluginInterface.UiBuilder.DisableGposeUiHide = true;
        // add the event handlers for the UI builder's draw event
        Svc.PluginInterface.UiBuilder.Draw += Draw;
        // subscribe to the UI builder's open config UI event
        Svc.PluginInterface.UiBuilder.OpenConfigUi += ToggleUi;
        // subscribe to the UI builder's open main UI event
        Svc.PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        // for each window in the collection of window mediator subscribers
        foreach (var window in windows)
            _windowSystem.AddWindow(window);

        /* ---------- The following subscribers are for factory made windows, meant to be unique to each pair ---------- */
        Mediator.Subscribe<ProfileOpenMessage>(this, (msg) =>
        {
            if (_createdWindows.FirstOrDefault(p => p is ProfileUI ui && ui.User == msg.UserData) is { } match)
                match.Toggle();
            else
            {
                var window = _uiFactory.CreateStandaloneProfileUi(msg.UserData);
                _createdWindows.Add(window);
                _windowSystem.AddWindow(window);
            }
        });
    }

    /// <summary>
    ///     Offloads a UI task to the thread pool to not halt ImGui. 
    ///     When the task is finished DisableUI will be set to false.
    /// </summary>
    public static void SetUITask(Task task)
    {
        if (DisableUI)
        {
            Svc.Logger.Warning("Attempted to assign a new UI blocking task while one is already running.", LoggerType.UIManagement);
            return;
        }

        UiTask = task;
        Svc.Logger.Verbose("Assigned new UI blocking task: " + task, LoggerType.UIManagement);
    }

    /// <summary>
    ///     Offloads a UI task to the thread pool to not halt ImGui. 
    ///     When the task is finished DisableUI will be set to false.
    /// </summary>
    public static void SetUITask(Func<Task> asyncAction)
    {
        if (DisableUI)
        {
            Svc.Logger.Warning("Attempted to assign a new UI blocking task while one is already running.", LoggerType.UIManagement);
            return;
        }

        UiTask = Task.Run(asyncAction);
        Svc.Logger.Verbose("Assigned new UI blocking task.", LoggerType.UIManagement);
    }

    /// <summary>
    ///     Offloads a UI Task to the thread pool so ImGui is not halted. It
    ///     contains an inner task function that can return <typeparamref name="T"/>.
    /// </summary>
    /// <returns> A task that can be awaited, returning a value of type <typeparamref name="T"/>. </returns>
    public static async Task<T> SetUITaskWithReturn<T>(Func<Task<T>> asyncTask)
    {
        if (DisableUI)
        {
            Svc.Logger.Warning("Attempted to assign a new UI blocking task while one is already running.", LoggerType.UIManagement);
            return default(T)!;
        }

        var taskToRun = Task.Run(asyncTask);
        UiTask = taskToRun;
        Svc.Logger.Verbose("Assigned new UI blocking task.", LoggerType.UIManagement);
        return await taskToRun.ConfigureAwait(false);
    }

    /// <summary> 
    ///     Sanity check to validate if the Client has a registered account yet or not.
    ///</summary>
    public void ToggleMainUi()
    {
        if (_config.HasValidSetup() && _accountConfig.HasValidSetup())
            Mediator.Publish(new UiToggleMessage(typeof(MainUI)));
        else
            Mediator.Publish(new UiToggleMessage(typeof(IntroUi)));
    }

    /// <summary>
    ///     Runs whenever the [Config] button in the dalamud plugin list installer. <para />
    ///     Ensures we treat interacting with that button the same as the above method.
    /// </summary>
    public void ToggleUi()
    {
        if (_config.HasValidSetup() && _accountConfig.HasValidSetup())
            Mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
        else
            Mediator.Publish(new UiToggleMessage(typeof(IntroUi)));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        Logger.LogTrace("Disposing "+GetType().Name, LoggerType.UIManagement);
        _windowSystem.RemoveAllWindows();
        // Created Profile UIs need to be disposed of manually here.
        foreach (var window in _createdWindows)
            window.Dispose();

        // unsubscribe from the draw, open config UI, and main UI
        Svc.PluginInterface.UiBuilder.Draw -= Draw;
        Svc.PluginInterface.UiBuilder.OpenConfigUi -= ToggleUi;
        Svc.PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
    }

    /// <summary>
    ///     Draw the windows system and file dialogue managers
    /// </summary>
    private void Draw()
    {
        _windowSystem.Draw();
        _fileService.Draw();
    }
}
