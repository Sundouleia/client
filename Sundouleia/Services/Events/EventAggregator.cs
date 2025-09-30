using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;
using Microsoft.Extensions.Hosting;
using Sundouleia.Utils;
using Sundouleia.WebAPI;
using Sundouleia.Services.Configs;

namespace Sundouleia.Services.Events;

/// <summary>
///     Reports updates to our Sundesmos visible state.
/// </summary>
public class EventAggregator : MediatorSubscriberBase, IHostedService
{
    private readonly SundesmoManager _pairs;

    private readonly RollingList<DataEvent> _events = new(500);
    private readonly SemaphoreSlim _lock = new(1);
    private string CurrentLogName => $"{DateTime.Now:yyyy-MM-dd}-sundesmo_events.log";
    private DateTime _currentTime;

    public EventAggregator(ILogger<EventAggregator> logger, SundouleiaMediator mediator, SundesmoManager pairs) 
        : base(logger, mediator)
    {
        _pairs = pairs;
        // Create a new event list
        EventList = CreateEventLazy();
        _currentTime = DateTime.Now - TimeSpan.FromDays(1);

        // Collect any events sent out.
        Mediator.Subscribe<EventMessage>(this, (msg) =>
        {
            _lock.Wait();
            try
            {
                Logger.LogTrace("Received Event: " + msg.Event.ToString(), LoggerType.PairDataTransfer);
                _events.Add(msg.Event);
                WriteToFile(msg.Event);
            }
            finally
            {
                _lock.Release();
            }

            RecreateLazy();
        });
    }

    /// <summary>
    ///     Displayed event list so the UI can display it.
    /// </summary>
    public Lazy<List<DataEvent>> EventList { get; private set; }

    /// <summary>
    ///     Take the rolling list of events and put them into a lazy list for the UI to consume.
    /// </summary>
    private void RecreateLazy()
    {
        if (!EventList.IsValueCreated) 
            return;

        EventList = CreateEventLazy();
    }

    /// <summary>
    ///     Create a new lazy event list with all the private event data inside.
    /// </summary>
    private Lazy<List<DataEvent>> CreateEventLazy()
    {
        return new Lazy<List<DataEvent>>(() =>
        {
            _lock.Wait();
            try
            {
                return [.. _events];
            }
            finally
            {
                _lock.Release();
            }
        });
    }

    /// <summary>
    ///     Write the event data into the event log file.
    /// </summary>
    private void WriteToFile(DataEvent receivedEvent)
    {
        if (DateTime.Now.Day != _currentTime.Day)
        {
            try
            {
                _currentTime = DateTime.Now;
                var filesInDirectory = Directory.EnumerateFiles(ConfigFileProvider.EventDirectory, "*.log");
                if (filesInDirectory.Skip(10).Any())
                {
                    File.Delete(filesInDirectory.OrderBy(f => new FileInfo(f).LastWriteTimeUtc).First());
                }
            }
            catch (Bagagwa ex)
            {
                Logger.LogWarning(ex, "Could not delete last events");
            }
        }

        var eventLogFile = Path.Combine(ConfigFileProvider.EventDirectory, CurrentLogName);
        try
        {
            if (!Directory.Exists(ConfigFileProvider.EventDirectory)) Directory.CreateDirectory(ConfigFileProvider.EventDirectory);
            File.AppendAllLines(eventLogFile, [receivedEvent.ToString()]);
        }
        catch (Bagagwa ex)
        {
            Logger.LogWarning(ex, $"Could not write to event file {eventLogFile}");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Started Interaction EventAggregator");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

