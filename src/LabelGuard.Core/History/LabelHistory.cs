using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LabelGuard.Core.History;

/// <summary>
/// Histórico de etiquetas impressas e log de eventos em SQLite.
/// O banco garante que cada ID tenha no máximo uma impressão sem autorização,
/// então duas threads não conseguem liberar o mesmo ID ao mesmo tempo.
/// </summary>
public sealed class LabelHistory : IDisposable
{
    private const int SchemaVersion = 1;

    private readonly SqliteConnection _connection;
    private readonly TimeProvider _clock;
    private readonly object _lock = new();

    /// <param name="databasePath">Arquivo do banco; criado se não existir. Use ":memory:" para um banco temporário.</param>
    public LabelHistory(string databasePath, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        // Sem pool para o arquivo ser liberado no Dispose.
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false };
        _connection = new SqliteConnection(connectionString.ToString());
        _connection.Open();
        CreateSchema();
    }

    private void CreateSchema()
    {
        Execute("PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000;");
        using var versionCommand = Command("PRAGMA user_version");
        var version = Convert.ToInt32(versionCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (version >= SchemaVersion)
            return;

        Execute($"""
            CREATE TABLE IF NOT EXISTS prints (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                label_id      TEXT NOT NULL,
                printed_at    TEXT NOT NULL,
                authorized_by TEXT NULL,
                job           TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_prints_label ON prints(label_id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_prints_first ON prints(label_id) WHERE authorized_by IS NULL;

            CREATE TABLE IF NOT EXISTS events (
                id       INTEGER PRIMARY KEY AUTOINCREMENT,
                at       TEXT NOT NULL,
                type     TEXT NOT NULL,
                message  TEXT NOT NULL,
                label_id TEXT NULL,
                job      TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_events_label ON events(label_id);

            PRAGMA user_version = {SchemaVersion};
            """);
    }

    /// <summary>Registra a primeira impressão do ID. Retorna false se ele já foi impresso antes.</summary>
    public bool TryRecordPrint(string labelId, string? job = null)
    {
        lock (_lock)
        {
            using var command = Command(
                "INSERT OR IGNORE INTO prints (label_id, printed_at, authorized_by, job) VALUES ($id, $at, NULL, $job)",
                ("$id", labelId), ("$at", Now()), ("$job", job));
            return command.ExecuteNonQuery() == 1;
        }
    }

    /// <summary>
    /// Desfaz a primeira impressão registrada por <see cref="TryRecordPrint"/> quando o envio para a impressora falhou.
    /// Reimpressões autorizadas não são afetadas.
    /// </summary>
    public bool ReleasePrint(string labelId)
    {
        lock (_lock)
        {
            using var command = Command(
                "DELETE FROM prints WHERE label_id = $id AND authorized_by IS NULL", ("$id", labelId));
            return command.ExecuteNonQuery() == 1;
        }
    }

    /// <summary>Registra uma reimpressão liberada por um supervisor.</summary>
    public void RecordAuthorizedReprint(string labelId, string supervisor, string? job = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(supervisor);
        lock (_lock)
        {
            using var command = Command(
                "INSERT INTO prints (label_id, printed_at, authorized_by, job) VALUES ($id, $at, $by, $job)",
                ("$id", labelId), ("$at", Now()), ("$by", supervisor), ("$job", job));
            command.ExecuteNonQuery();
        }
    }

    public bool WasPrinted(string labelId)
    {
        lock (_lock)
        {
            using var command = Command("SELECT EXISTS (SELECT 1 FROM prints WHERE label_id = $id)", ("$id", labelId));
            return command.ExecuteScalar() is 1L;
        }
    }

    /// <summary>Todas as impressões do ID, da mais antiga para a mais recente.</summary>
    public IReadOnlyList<PrintRecord> GetPrints(string labelId)
    {
        lock (_lock)
        {
            using var command = Command(
                "SELECT label_id, printed_at, authorized_by, job FROM prints WHERE label_id = $id ORDER BY id",
                ("$id", labelId));
            using var reader = command.ExecuteReader();
            var prints = new List<PrintRecord>();
            while (reader.Read())
                prints.Add(new PrintRecord(
                    reader.GetString(0), ParseTime(reader.GetString(1)), NullableString(reader, 2), NullableString(reader, 3)));
            return prints;
        }
    }

    public void Log(EventType type, string message, string? labelId = null, string? job = null)
    {
        lock (_lock)
        {
            using var command = Command(
                "INSERT INTO events (at, type, message, label_id, job) VALUES ($at, $type, $message, $id, $job)",
                ("$at", Now()), ("$type", type.ToString()), ("$message", message), ("$id", labelId), ("$job", job));
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Eventos mais recentes primeiro.</summary>
    public IReadOnlyList<EventRecord> GetRecentEvents(int limit = 100)
    {
        lock (_lock)
        {
            using var command = Command(
                "SELECT id, at, type, message, label_id, job FROM events ORDER BY id DESC LIMIT $limit",
                ("$limit", limit));
            using var reader = command.ExecuteReader();
            var events = new List<EventRecord>();
            while (reader.Read())
                events.Add(new EventRecord(
                    reader.GetInt64(0),
                    ParseTime(reader.GetString(1)),
                    Enum.Parse<EventType>(reader.GetString(2)),
                    reader.GetString(3),
                    NullableString(reader, 4),
                    NullableString(reader, 5)));
            return events;
        }
    }

    public void Dispose() => _connection.Dispose();

    private string Now() => _clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private void Execute(string sql)
    {
        using var command = Command(sql);
        command.ExecuteNonQuery();
    }

    private SqliteCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
}
