namespace PhoneBridge.Core.Protocol;

// Wire DTOs for docs/protocol.md. Serialized with System.Text.Json web defaults (camelCase).

public sealed record ErrorResponse(string Error, string Message);

public sealed record PairingQrPayload(int V, string Host, int Port, string CertSha256, string PairingSecret, string DeviceId, string Name);

public sealed record PairRequest(string? PairingSecret, string? DeviceName, string? DeviceId);

public sealed record PairResponse(string Token, string ServerName, int HttpPort);

public sealed record StatusFeatures(bool ShortcutsHttp, string Bluetooth);

public sealed record StatusResponse(string Name, string Version, StatusFeatures? Features = null, DateTimeOffset? Time = null, BatteryState? Battery = null);

public sealed record FileUploadResponse(string SavedAs, long Size, string Sha256);

public sealed record OutboxListResponse(IReadOnlyList<OutboxItem> Items);

public sealed record LinkRequest(string? Url, string? Title);

public sealed record ClipboardPayload(string? Kind, string? Text, string? ImageBase64, DateTimeOffset? UpdatedAt = null);

public sealed record MessageIn(string? Sender, string? Body, string? Service, DateTimeOffset? ReceivedAt);

public sealed record BatteryIn(int? Level, bool? Charging, string? Source);

public sealed record OutboxEvent(string Type, int Count);
