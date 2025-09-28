namespace Sundouleia.Utils;

// Move over to API for server deserialization.
public struct SerializableChatLog
{
    public ushort WorldId { get; set; }
    public ushort TerritoryId { get; set; }
    public DateTime DateStarted { get; set; }
    public List<RadarCkChatMessage> Messages { get; set; }

    public SerializableChatLog(ushort world, ushort territory, DateTime started, List<RadarCkChatMessage> messages)
    {
        WorldId = world;
        TerritoryId = territory;
        DateStarted = started;
        Messages = messages;
    }
}
