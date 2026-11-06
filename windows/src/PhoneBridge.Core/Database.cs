using Microsoft.Data.Sqlite;

namespace PhoneBridge.Core;

/// <summary>Local SQLite history (SPEC C4). One short-lived pooled connection per call keeps it thread-safe.</summary>
public sealed class Database
{
    readonly string _connectionString;

    public Database(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true }.ToString();
        using var c = Open();
        Exec(c, """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS devices (
                id TEXT PRIMARY KEY, name TEXT NOT NULL, token_hash TEXT NOT NULL UNIQUE,
                paired_at TEXT NOT NULL, last_seen_at TEXT);
            CREATE TABLE IF NOT EXISTS messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT, peer TEXT NOT NULL, body TEXT NOT NULL, service TEXT NOT NULL,
                direction TEXT NOT NULL, at TEXT NOT NULL, status TEXT, via TEXT);
            CREATE INDEX IF NOT EXISTS ix_messages_peer ON messages(peer, at);
            CREATE TABLE IF NOT EXISTS notifications (
                id INTEGER PRIMARY KEY AUTOINCREMENT, app_id TEXT NOT NULL, app_name TEXT, category TEXT,
                title TEXT, body TEXT, at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS transfers (
                id INTEGER PRIMARY KEY AUTOINCREMENT, direction TEXT NOT NULL, name TEXT NOT NULL, size INTEGER NOT NULL,
                sha256 TEXT, path TEXT NOT NULL, status TEXT NOT NULL, at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS clipboard (
                id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, text TEXT, origin TEXT NOT NULL, at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS outbox (
                id TEXT PRIMARY KEY, kind TEXT NOT NULL, name TEXT, size INTEGER, text TEXT, url TEXT, to_handle TEXT,
                path TEXT, message_id INTEGER, transfer_id INTEGER, created_at TEXT NOT NULL, delivered_at TEXT);
            PRAGMA user_version = 1;
            """);
    }

    SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    SqliteCommand Cmd(SqliteConnection c, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    int NonQuery(string sql, params (string, object?)[] args)
    {
        using var c = Open();
        using var cmd = Cmd(c, sql, args);
        return cmd.ExecuteNonQuery();
    }

    long Insert(string sql, params (string, object?)[] args)
    {
        using var c = Open();
        using var cmd = Cmd(c, sql + "; SELECT last_insert_rowid();", args);
        return (long)cmd.ExecuteScalar()!;
    }

    List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string, object?)[] args)
    {
        using var c = Open();
        using var cmd = Cmd(c, sql, args);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    static string Iso(DateTimeOffset t) => t.ToUniversalTime().ToString("O");
    static DateTimeOffset T(SqliteDataReader r, int i) => DateTimeOffset.Parse(r.GetString(i));
    static DateTimeOffset? Tn(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : DateTimeOffset.Parse(r.GetString(i));
    static string? S(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    static long? L(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);

    // Devices
    public void AddDevice(DeviceRecord d, string tokenHash) =>
        NonQuery("INSERT OR REPLACE INTO devices (id, name, token_hash, paired_at, last_seen_at) VALUES ($id, $n, $h, $p, NULL)",
            ("$id", d.Id), ("$n", d.Name), ("$h", tokenHash), ("$p", Iso(d.PairedAt)));

    public DeviceRecord? FindDeviceByTokenHash(string tokenHash) =>
        Query("SELECT id, name, paired_at, last_seen_at FROM devices WHERE token_hash = $h", MapDevice, ("$h", tokenHash)).FirstOrDefault();

    public List<DeviceRecord> ListDevices() =>
        Query("SELECT id, name, paired_at, last_seen_at FROM devices ORDER BY paired_at", MapDevice);

    public bool RevokeDevice(string id) => NonQuery("DELETE FROM devices WHERE id = $id", ("$id", id)) > 0;

    public void TouchDevice(string id, DateTimeOffset at) =>
        NonQuery("UPDATE devices SET last_seen_at = $t WHERE id = $id", ("$t", Iso(at)), ("$id", id));

    static DeviceRecord MapDevice(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), T(r, 2), Tn(r, 3));

    // Messages
    public MessageRecord AddMessage(MessageRecord m) => m with
    {
        Id = Insert("INSERT INTO messages (peer, body, service, direction, at, status, via) VALUES ($p, $b, $s, $d, $a, $st, $v)",
            ("$p", m.Peer), ("$b", m.Body), ("$s", m.Service), ("$d", m.Direction), ("$a", Iso(m.At)), ("$st", m.Status), ("$v", m.Via)),
    };

    public void UpdateMessageStatus(long id, string status, string? via) =>
        NonQuery("UPDATE messages SET status = $s, via = COALESCE($v, via) WHERE id = $id", ("$s", status), ("$v", via), ("$id", id));

    public MessageRecord? GetMessage(long id) =>
        Query("SELECT id, peer, body, service, direction, at, status, via FROM messages WHERE id = $id", MapMessage, ("$id", id)).FirstOrDefault();

    public List<MessageRecord> ListMessages(string peer, int limit = 200) =>
        Query("SELECT * FROM (SELECT id, peer, body, service, direction, at, status, via FROM messages WHERE peer = $p ORDER BY at DESC, id DESC LIMIT $l) ORDER BY at, id",
            MapMessage, ("$p", peer), ("$l", limit));

    /// <summary>Latest message per peer, newest conversation first.</summary>
    public List<MessageRecord> ListConversationHeads(int limit = 200) =>
        Query("""
            SELECT m.id, m.peer, m.body, m.service, m.direction, m.at, m.status, m.via FROM messages m
            WHERE m.id = (SELECT id FROM messages WHERE peer = m.peer ORDER BY at DESC, id DESC LIMIT 1)
            ORDER BY m.at DESC LIMIT $l
            """, MapMessage, ("$l", limit));

    static MessageRecord MapMessage(SqliteDataReader r) =>
        new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), T(r, 5), S(r, 6), S(r, 7));

    // Notifications (filled by the Bluetooth/ANCS module in phase 2)
    public NotificationRecord AddNotification(NotificationRecord n) => n with
    {
        Id = Insert("INSERT INTO notifications (app_id, app_name, category, title, body, at) VALUES ($a, $n, $c, $t, $b, $at)",
            ("$a", n.AppId), ("$n", n.AppName), ("$c", n.Category), ("$t", n.Title), ("$b", n.Body), ("$at", Iso(n.At))),
    };

    public List<NotificationRecord> ListNotifications(int limit = 200) =>
        Query("SELECT id, app_id, app_name, category, title, body, at FROM notifications ORDER BY at DESC LIMIT $l",
            r => new NotificationRecord(r.GetInt64(0), r.GetString(1), S(r, 2), S(r, 3), S(r, 4), S(r, 5), T(r, 6)), ("$l", limit));

    public void DeleteNotification(long id) => NonQuery("DELETE FROM notifications WHERE id = $id", ("$id", id));
    public void ClearNotifications() => NonQuery("DELETE FROM notifications");

    // Transfers
    public TransferRecord AddTransfer(TransferRecord t) => t with
    {
        Id = Insert("INSERT INTO transfers (direction, name, size, sha256, path, status, at) VALUES ($d, $n, $s, $h, $p, $st, $a)",
            ("$d", t.Direction), ("$n", t.Name), ("$s", t.Size), ("$h", t.Sha256), ("$p", t.Path), ("$st", t.Status), ("$a", Iso(t.At))),
    };

    public void UpdateTransferStatus(long id, string status) =>
        NonQuery("UPDATE transfers SET status = $s WHERE id = $id", ("$s", status), ("$id", id));

    public TransferRecord? GetTransfer(long id) =>
        Query("SELECT id, direction, name, size, sha256, path, status, at FROM transfers WHERE id = $id", MapTransfer, ("$id", id)).FirstOrDefault();

    public List<TransferRecord> ListTransfers(int limit = 100) =>
        Query("SELECT id, direction, name, size, sha256, path, status, at FROM transfers ORDER BY at DESC, id DESC LIMIT $l", MapTransfer, ("$l", limit));

    static TransferRecord MapTransfer(SqliteDataReader r) =>
        new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt64(3), S(r, 4), r.GetString(5), r.GetString(6), T(r, 7));

    // Clipboard history (sensitive items must never reach this table)
    public ClipboardRecord AddClipboard(ClipboardRecord c) => c with
    {
        Id = Insert("INSERT INTO clipboard (kind, text, origin, at) VALUES ($k, $t, $o, $a)",
            ("$k", c.Kind), ("$t", c.Text), ("$o", c.Origin), ("$a", Iso(c.At))),
    };

    public List<ClipboardRecord> ListClipboard(int limit = 100) =>
        Query("SELECT id, kind, text, origin, at FROM clipboard ORDER BY at DESC, id DESC LIMIT $l",
            r => new ClipboardRecord(r.GetInt64(0), r.GetString(1), S(r, 2), r.GetString(3), T(r, 4)), ("$l", limit));

    public void DeleteClipboard(long id) => NonQuery("DELETE FROM clipboard WHERE id = $id", ("$id", id));
    public void ClearClipboard() => NonQuery("DELETE FROM clipboard");

    // Outbox (PC -> iPhone)
    public void AddOutbox(OutboxItem o) =>
        NonQuery("""
            INSERT INTO outbox (id, kind, name, size, text, url, to_handle, path, message_id, transfer_id, created_at)
            VALUES ($id, $k, $n, $s, $t, $u, $to, $p, $m, $tr, $c)
            """,
            ("$id", o.Id), ("$k", o.Kind), ("$n", o.Name), ("$s", o.Size), ("$t", o.Text), ("$u", o.Url), ("$to", o.To),
            ("$p", o.Path), ("$m", o.MessageId), ("$tr", o.TransferId), ("$c", Iso(o.CreatedAt)));

    const string OutboxColumns = "id, kind, name, size, text, url, to_handle, created_at, path, message_id, transfer_id";

    public List<OutboxItem> ListPendingOutbox() =>
        Query($"SELECT {OutboxColumns} FROM outbox WHERE delivered_at IS NULL ORDER BY created_at", MapOutbox);

    public OutboxItem? GetOutbox(string id) =>
        Query($"SELECT {OutboxColumns} FROM outbox WHERE id = $id AND delivered_at IS NULL", MapOutbox, ("$id", id)).FirstOrDefault();

    public bool MarkOutboxDelivered(string id, DateTimeOffset at) =>
        NonQuery("UPDATE outbox SET delivered_at = $t WHERE id = $id AND delivered_at IS NULL", ("$t", Iso(at)), ("$id", id)) > 0;

    static OutboxItem MapOutbox(SqliteDataReader r) =>
        new(r.GetString(0), r.GetString(1), S(r, 2), L(r, 3), S(r, 4), S(r, 5), S(r, 6), T(r, 7))
        { Path = S(r, 8), MessageId = L(r, 9), TransferId = L(r, 10) };
}
