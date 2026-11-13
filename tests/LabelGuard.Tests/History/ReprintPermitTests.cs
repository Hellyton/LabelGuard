using System.Text;
using LabelGuard.Core.Guard;
using LabelGuard.Core.History;
using LabelGuard.Core.Parsing;
using Microsoft.Data.Sqlite;

namespace LabelGuard.Tests.History;

public class ReprintPermitTests : IDisposable
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly LabelHistory _history;
    private readonly RecordingSender _sender = new();

    public ReprintPermitTests() => _history = new LabelHistory(":memory:", _clock);

    public void Dispose() => _history.Dispose();

    private PrintGuard Guard() => new(_history, new IdExtractor());

    private static byte[] Job(string pallet, string extra = "") =>
        Encoding.Latin1.GetBytes($"^XA^FDhttps://app.papersystem.com.br/d/etiqueta?pallet={pallet}^FS{extra}^XZ");

    [Fact]
    public void Permit_IsValidUntilExpiry()
    {
        var permit = _history.GrantReprint("3931", "supervisor", TimeSpan.FromMinutes(60));

        Assert.Equal(_clock.Now.AddMinutes(60), permit.ExpiresAt);
        Assert.Equal("supervisor", _history.GetReprintPermit("3931")?.GrantedBy);

        _clock.Now = _clock.Now.AddMinutes(61);
        Assert.Null(_history.GetReprintPermit("3931"));
    }

    [Fact]
    public void Permit_IsConsumedOnce()
    {
        _history.GrantReprint("3931", "supervisor", TimeSpan.FromMinutes(60));

        _history.ConsumeReprintPermit("3931");

        Assert.Null(_history.GetReprintPermit("3931"));
    }

    [Fact]
    public void NewPermit_ReplacesOld()
    {
        _history.GrantReprint("3931", "supervisor", TimeSpan.FromMinutes(5));
        _clock.Now = _clock.Now.AddMinutes(4);
        _history.GrantReprint("3931", "supervisor", TimeSpan.FromMinutes(60));
        _clock.Now = _clock.Now.AddMinutes(10);

        Assert.NotNull(_history.GetReprintPermit("3931"));
    }

    [Fact]
    public async Task Permit_LetsNextPrintPassOnce()
    {
        var guard = Guard();
        await guard.ProcessAsync(Job("3931"), "job-1", _sender);
        guard.GrantReprint("3931", "supervisor", TimeSpan.FromMinutes(60));

        var reprint = await guard.ProcessAsync(Job("3931"), "job-2", _sender);
        var again = await guard.ProcessAsync(Job("3931"), "job-3", _sender);

        Assert.Equal(LabelStatus.Printed, Assert.Single(reprint.Labels).Status);
        Assert.Equal(LabelStatus.Duplicate, Assert.Single(again.Labels).Status);
        Assert.Equal(2, _sender.Sent.Count);
        var prints = _history.GetPrints("3931");
        Assert.Equal(2, prints.Count);
        Assert.Equal("supervisor", prints[1].AuthorizedBy);
        Assert.Equal("job-2", prints[1].Job);
    }

    [Fact]
    public async Task ExpiredPermit_DoesNotLetPrintPass()
    {
        var guard = Guard();
        await guard.ProcessAsync(Job("3931"), "job-1", _sender);
        guard.GrantReprint("3931", "supervisor", TimeSpan.FromMinutes(60));
        _clock.Now = _clock.Now.AddMinutes(90);

        var result = await guard.ProcessAsync(Job("3931"), "job-2", _sender);

        Assert.Equal(LabelStatus.Duplicate, Assert.Single(result.Labels).Status);
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task Permit_IsKeptWhenSendFails()
    {
        var guard = Guard();
        await guard.ProcessAsync(Job("3931"), "job-1", _sender);
        guard.GrantReprint("3931", "supervisor", TimeSpan.FromMinutes(60));
        _sender.Fail = true;

        var failed = await guard.ProcessAsync(Job("3931"), "job-2", _sender);

        Assert.Equal(LabelStatus.Failed, Assert.Single(failed.Labels).Status);
        Assert.NotNull(_history.GetReprintPermit("3931"));
        Assert.Single(_history.GetPrints("3931"));

        _sender.Fail = false;
        var retry = await guard.ProcessAsync(Job("3931"), "job-3", _sender);
        Assert.Equal(LabelStatus.Printed, Assert.Single(retry.Labels).Status);
    }

    [Fact]
    public async Task Permit_DoesNotAllowMultipleCopies()
    {
        var guard = Guard();
        await guard.ProcessAsync(Job("3931"), "job-1", _sender);
        guard.GrantReprint("3931", "supervisor", TimeSpan.FromMinutes(60));

        var result = await guard.ProcessAsync(Job("3931", "^PQ2"), "job-2", _sender);

        Assert.Equal(LabelStatus.Duplicate, Assert.Single(result.Labels).Status);
        Assert.NotNull(_history.GetReprintPermit("3931"));
    }

    [Fact]
    public void Grant_IsLogged()
    {
        Guard().GrantReprint("3931", "supervisor", TimeSpan.FromMinutes(60));

        var logged = _history.GetRecentEvents(1)[0];
        Assert.Equal(EventType.ReprintAuthorized, logged.Type);
        Assert.Equal("3931", logged.LabelId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OlderDatabase_IsMigratedKeepingHistory(int version)
    {
        var path = Path.Combine(Path.GetTempPath(), $"labelguard-v{version}-{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE prints (id INTEGER PRIMARY KEY AUTOINCREMENT, label_id TEXT NOT NULL,
                        printed_at TEXT NOT NULL, authorized_by TEXT NULL, job TEXT NULL);
                    CREATE UNIQUE INDEX ux_prints_first ON prints(label_id) WHERE authorized_by IS NULL;
                    CREATE TABLE events (id INTEGER PRIMARY KEY AUTOINCREMENT, at TEXT NOT NULL, type TEXT NOT NULL,
                        message TEXT NOT NULL, label_id TEXT NULL, job TEXT NULL);
                    INSERT INTO prints (label_id, printed_at, job) VALUES ('3931', '2026-11-12T15:00:00.0000000+00:00', 'antigo');
                    """ + (version == 2
                        ? "CREATE TABLE label_images (label_id TEXT PRIMARY KEY, saved_at TEXT NOT NULL, job TEXT NULL, data BLOB NOT NULL);"
                        : "") + $"PRAGMA user_version = {version};";
                command.ExecuteNonQuery();
            }

            using (var history = new LabelHistory(path, _clock))
            {
                Assert.True(history.WasPrinted("3931"));
                Assert.False(history.TryRecordPrint("3931"));
                history.GrantReprint("3931", "supervisor", TimeSpan.FromMinutes(60));
                Assert.NotNull(history.GetReprintPermit("3931"));
            }

            using var check = new SqliteConnection($"Data Source={path};Pooling=False");
            check.Open();
            using var query = check.CreateCommand();
            query.CommandText = "SELECT count(*) FROM sqlite_master WHERE name = 'label_images'";
            Assert.Equal(0L, query.ExecuteScalar());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                File.Delete(file);
        }
    }

    private sealed class RecordingSender : ILabelSender
    {
        public List<string> Sent { get; } = new();

        public bool Fail { get; set; }

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            if (Fail)
                throw new IOException("impressora offline");
            Sent.Add(Encoding.Latin1.GetString(data.Span));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
