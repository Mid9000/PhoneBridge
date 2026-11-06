using System.Text.Json.Serialization;

namespace PhoneBridge.Core;

public sealed record DeviceRecord(string Id, string Name, DateTimeOffset PairedAt, DateTimeOffset? LastSeenAt);

/// <summary>Direction "in" = received on the phone, "out" = sent from the PC.</summary>
public sealed record MessageRecord(
    long Id, string Peer, string Body, string Service, string Direction,
    DateTimeOffset At, string? Status = null, string? Via = null);

public sealed record NotificationRecord(
    long Id, string AppId, string? AppName, string? Category, string? Title, string? Body, DateTimeOffset At);

/// <summary>Direction "toPc" or "toPhone"; Status "active", "done", "failed", "queued".</summary>
public sealed record TransferRecord(
    long Id, string Direction, string Name, long Size, string? Sha256, string Path, string Status, DateTimeOffset At);

/// <summary>Origin "phone" or "pc". Kind "text", "url" or "image".</summary>
public sealed record ClipboardRecord(long Id, string Kind, string? Text, string Origin, DateTimeOffset At);

public sealed record OutboxItem(
    string Id, string Kind, string? Name, long? Size, string? Text, string? Url, string? To, DateTimeOffset CreatedAt)
{
    [JsonIgnore] public string? Path { get; init; }
    [JsonIgnore] public long? MessageId { get; init; }
    [JsonIgnore] public long? TransferId { get; init; }
}

public sealed record BatteryState(int Level, bool Charging, string? Source, DateTimeOffset UpdatedAt);

public static class OutboxKinds
{
    public const string File = "file", Clipboard = "clipboard", Link = "link", Reply = "reply";
}

public static class SendStatus
{
    public const string Sending = "sending", Sent = "sent", Queued = "queued", Failed = "failed";
}
