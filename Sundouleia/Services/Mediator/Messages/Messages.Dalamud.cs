namespace Sundouleia.Services.Mediator;

/// <summary> Invoked upon Client Player Login. </summary>
public record DalamudLoginMessage : MessageBase;

/// <summary> Every Game Framework Update, this fires. </summary>
public record FrameworkUpdateMessage : SameThreadMessage;

/// <summary> Every Second, on the next Framework Update, this fires. </summary>
public record DelayedFrameworkUpdateMessage : SameThreadMessage;

// Could help with sync services, but what do I know lol.
public record GPoseStartMessage : MessageBase;
public record GPoseEndMessage : MessageBase;
public record CutsceneBeginMessage : MessageBase;
public record CutsceneSkippedMessage : MessageBase;
public record ClientPlayerInCutscene : MessageBase;
public record CutsceneEndMessage : MessageBase;

// Should probably process this in a zone service or something.
public record ZoneSwitchStartMessage(uint prevZone) : MessageBase;
public record ZoneSwitchEndMessage : MessageBase;