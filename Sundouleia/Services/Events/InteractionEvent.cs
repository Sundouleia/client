namespace Sundouleia.Services.Events;

/// <summary>
///     Outline of a SundesmoEvent to be logged and stored. <para />
///     Helps with informing clients of exchanges between your sundesmos 
///     without digging through your logger too much.
/// </summary>
public record DataEvent
{
    /// <summary> Time of event, local to your time. </summary>
    public DateTime EventTime { get; }

    /// <summary> The Sundesmos Nick, Alias, or UID. </summary>
    public string NickAliasOrUID { get; set; }

    /// <summary> Raw UID. (For Filtering) </summary>
    public string UserUID { get; }

    /// <summary> Event Kind (For Filtering) </summary>
    public DataEventType Type { get; }

    /// <summary> Short, less than 20 word Description of the update. </summary>
    public string DataSummary { get; }

    public DataEvent(string uid, DataEventType type)
        : this(uid, uid, type, string.Empty) { }

    public DataEvent(string uid, DataEventType type, string summary)
        : this(uid, uid, type, summary) { }

    public DataEvent(string labelName, string uid, DataEventType type, string summary)
    {
        EventTime = DateTime.Now;
        NickAliasOrUID = labelName;
        UserUID = uid;
        Type = type;
        DataSummary = summary;
    }

    public override string ToString() => $"[{EventTime:HH:mm}][{NickAliasOrUID}] {Type} - {DataSummary}";
}
