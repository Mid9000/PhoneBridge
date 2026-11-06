using PhoneBridge.Bluetooth.Ancs;
using PhoneBridge.Bluetooth.Map;
using PhoneBridge.Bluetooth.Pbap;

namespace PhoneBridge.Bluetooth;

public enum BluetoothConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    /// <summary>The iPhone refused access: a toggle in Settings > Bluetooth > (i) is off (OBEX Forbidden / GATT access denied).</summary>
    PermissionDenied,
    /// <summary>Service not found or another client (Phone Link) holds it.</summary>
    Unavailable,
}

/// <summary>A notification from ANCS. Content fields are user data: never log them.</summary>
public sealed record AncsNotification
{
    public required uint Uid { get; init; }
    public required AncsCategory Category { get; init; }
    public required AncsEventFlags Flags { get; init; }
    public bool IsModification { get; init; }
    public string AppIdentifier { get; init; } = "";
    public string? AppDisplayName { get; init; }
    public string? Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Message { get; init; }
    public DateTime? Date { get; init; }
    public string? PositiveActionLabel { get; init; }
    public string? NegativeActionLabel { get; init; }

    public bool IsPreExisting => Flags.HasFlag(AncsEventFlags.PreExisting);
}

/// <summary>A message received over MAP. Body/sender are user data: never log them.</summary>
public sealed record MapMessage
{
    public required string Handle { get; init; }
    public string? SenderName { get; init; }
    public string? SenderAddress { get; init; }
    public string? Body { get; init; }
    public DateTimeOffset? DateTime { get; init; }
    public bool Read { get; init; }
    /// <summary>iOS reports "SMS_GSM" for both SMS and iMessage; do not infer the transport from it.</summary>
    public string? Type { get; init; }
}

public interface IAncsClient : IAsyncDisposable
{
    BluetoothConnectionState State { get; }
    event EventHandler<AncsNotification>? NotificationReceived;
    event EventHandler<uint>? NotificationRemoved;
    /// <summary>Raised in addition to NotificationReceived for CategoryIncomingCall additions.</summary>
    event EventHandler<AncsNotification>? IncomingCall;
    event EventHandler<BluetoothConnectionState>? ConnectionStateChanged;

    Task StartAsync(CancellationToken ct = default);
    /// <summary>Positive/negative action, e.g. answer/decline a call. Only valid if the notification flags allow it.</summary>
    Task PerformActionAsync(uint notificationUid, AncsAction action, CancellationToken ct = default);
}

public interface IMapClient : IAsyncDisposable
{
    BluetoothConnectionState State { get; }
    event EventHandler<MapMessage>? MessageReceived;
    event EventHandler<BluetoothConnectionState>? ConnectionStateChanged;

    Task ConnectAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MapFolder>> GetFolderListingAsync(string path, CancellationToken ct = default);
    Task<IReadOnlyList<MapMessageEntry>> GetMessagesListingAsync(string folder = "inbox", int maxCount = 10, int subjectLength = 255, CancellationToken ct = default);
    Task<BMessage> GetMessageAsync(string handle, CancellationToken ct = default);
    /// <summary>Sends a message; returns the MSE handle when the iPhone gives one.</summary>
    Task<string?> PushMessageAsync(string recipient, string text, CancellationToken ct = default);
    Task SetReadStatusAsync(string handle, bool read, CancellationToken ct = default);
    /// <summary>Requires a published MNS service (see MnsServiceHost) before turning on.</summary>
    Task SetNotificationRegistrationAsync(bool enabled, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
}

public interface IPbapClient : IAsyncDisposable
{
    BluetoothConnectionState State { get; }
    Task ConnectAsync(CancellationToken ct = default);
    Task<int?> GetPhonebookSizeAsync(string path = "telecom/pb.vcf", CancellationToken ct = default);
    Task<IReadOnlyList<VCard>> PullPhonebookAsync(string path = "telecom/pb.vcf", int? maxCount = null, int offset = 0, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
}
