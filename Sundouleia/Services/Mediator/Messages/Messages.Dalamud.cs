namespace Sundouleia.Services.Mediator;

/// <summary> Invoked upon Client Player Login. </summary>
public record DalamudLoginMessage : MessageBase;

/// <summary> Every Second, on the next Framework Update, this fires. </summary>
public record DelayedFrameworkUpdateMessage : SameThreadMessage;

// Could help with sync services, but what do I know lol.
public record GPoseStartMessage : MessageBase;
public record GPoseEndMessage : MessageBase;
public record CutsceneBeginMessage : MessageBase;
public record CutsceneSkippedMessage : MessageBase;
public record ClientPlayerInCutscene : MessageBase;
public record CutsceneEndMessage : MessageBase;

public record TerritoryChanged(ushort PrevTerritory, ushort NewTerritory) : MessageBase;