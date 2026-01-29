using Dalamud.Interface.ImGuiNotification;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Events;

namespace Sundouleia.Services.Mediator;

/// <summary>
///     Every time we need to compose a message for the notification message, this is fired. <para />
///     Would personally prefer to handle this statically via a CkCommons extension but whatever.
/// </summary>
public record NotificationMessage(string Title, string Message, NotificationType Type, TimeSpan? TimeShownOnScreen = null) : MessageBase;

/// <summary> When an exchange of data occurs from a sundesmo or radar user or permission change. </summary>
public record EventMessage(DataEvent Event) : MessageBase;

/// <summary> The ConnectionKind was updated to reflect a new type. </summary>
public record ConnectionKindChanged(ConnectionKind PrevState, ConnectionKind NewState) : MessageBase;

/// <summary> Fires whenever the client is disconnected from the Sundouleia Hub. </summary>
public record DisconnectedMessage(DisconnectIntent Intent) : SameThreadMessage;

/// <summary> Fires whenever the client is attempting to reconnect to the Sundouleia Hub. </summary>
public record ReconnectingMessage(Exception? Exception) : SameThreadMessage;

/// <summary> Fires whenever the client has reconnected to the Sundouleia Hub. </summary>
public record ReconnectedMessage(string? Arg) : SameThreadMessage;

/// <summary> Fires whenever the Sundouleia Hub closes. </summary>
public record ClosedMessage(Exception? Exception) : SameThreadMessage;

/// <summary> Fires whenever the client has connected to the Sundouleia Hub. </summary>
public record ConnectedMessage : MessageBase;