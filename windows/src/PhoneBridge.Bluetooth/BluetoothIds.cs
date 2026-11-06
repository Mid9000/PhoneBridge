namespace PhoneBridge.Bluetooth;

/// <summary>Protocol identifiers. Sources and verification status: docs/ancs-notes.md, docs/map-notes.md.</summary>
public static class BluetoothIds
{
    // Apple ANCS spec (developer.apple.com, "Apple Notification Center Service"), verified 2026-11-06.
    public static readonly Guid AncsService = new("7905F431-B5CE-4E99-A40F-4B1E122D00D0");
    public static readonly Guid AncsNotificationSource = new("9FBF120D-6301-42D9-8C58-25E699A21DBD");
    public static readonly Guid AncsControlPoint = new("69D1D8F3-45E1-49A8-9821-9BBDFDAAD9D9");
    public static readonly Guid AncsDataSource = new("22EAC6E9-24D6-4BB5-BE44-B36ACE7C7BFB");

    // Bluetooth SIG assigned numbers (16-bit service class UUIDs).
    public const ushort MapMasShortId = 0x1132;
    public const ushort MapMnsShortId = 0x1133;
    public const ushort MapProfileShortId = 0x1134;
    public const ushort PbapPceShortId = 0x112E;
    public const ushort PbapPseShortId = 0x112F;
    public const ushort PbapProfileShortId = 0x1130;

    // OBEX Target header values (MAP 1.4 §6.3 / PBAP 1.2 §6.4), sent as 16 big-endian bytes.
    public static readonly byte[] MasTarget = ObexUuid("BB582B40-420C-11DB-B0DE-0800200C9A66");
    public static readonly byte[] MnsTarget = ObexUuid("BB582B41-420C-11DB-B0DE-0800200C9A66");
    public static readonly byte[] PbapTarget = ObexUuid("796135F0-F0C5-11D8-0966-0800200C9A66");

    /// <summary>UUID string to its 16 network-order bytes (Guid.ToByteArray is mixed-endian, so not usable here).</summary>
    public static byte[] ObexUuid(string uuid) => Convert.FromHexString(uuid.Replace("-", ""));
}
