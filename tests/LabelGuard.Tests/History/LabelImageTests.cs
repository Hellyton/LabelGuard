using LabelGuard.Core.History;
using Microsoft.Data.Sqlite;

namespace LabelGuard.Tests.History;

public class LabelImageTests : IDisposable
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly LabelHistory _history;

    public LabelImageTests() => _history = new LabelHistory(":memory:", _clock);

    public void Dispose() => _history.Dispose();

    private static byte[] Bitmap(byte marker)
    {
        // Como uma etiqueta real: quase tudo branco (0xFF).
        var data = Enumerable.Repeat((byte)0xFF, 180_000).ToArray();
        data[1000] = marker;
        return data;
    }

    [Fact]
    public void SavedImage_IsReturnedExactly()
    {
        var data = Bitmap(1);

        _history.SaveLabelImage("3931", "job-1", data);

        var stored = _history.GetLabelImage("3931");
        Assert.NotNull(stored);
        Assert.Equal(data, stored.Data);
        Assert.Equal("job-1", stored.Job);
        Assert.Equal(_clock.Now, stored.SavedAt);
    }

    [Fact]
    public void UnknownId_ReturnsNull()
    {
        Assert.Null(_history.GetLabelImage("3931"));
    }

    [Fact]
    public void SavingAgain_KeepsLatest()
    {
        _history.SaveLabelImage("3931", "job-1", Bitmap(1));
        _clock.Now = _clock.Now.AddMinutes(5);
        _history.SaveLabelImage("3931", "job-2", Bitmap(2));

        var stored = _history.GetLabelImage("3931")!;
        Assert.Equal("job-2", stored.Job);
        Assert.Equal(2, stored.Data[1000]);
    }

    [Fact]
    public void Retention_DeletesOnlyOldImages()
    {
        _history.SaveLabelImage("3931", "job-1", Bitmap(1));
        _history.TryRecordPrint("3931", "job-1");
        _clock.Now = _clock.Now.AddDays(31);
        _history.SaveLabelImage("3932", "job-2", Bitmap(2));

        var deleted = _history.DeleteLabelImagesOlderThan(TimeSpan.FromDays(30));

        Assert.Equal(1, deleted);
        Assert.Null(_history.GetLabelImage("3931"));
        Assert.NotNull(_history.GetLabelImage("3932"));
        Assert.True(_history.WasPrinted("3931"));
    }

    [Fact]
    public void DatabaseFromVersion1_IsMigratedKeepingHistory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"labelguard-v1-{Guid.NewGuid():N}.db");
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
                    PRAGMA user_version = 1;
                    """;
                command.ExecuteNonQuery();
            }

            using (var history = new LabelHistory(path, _clock))
            {
                Assert.True(history.WasPrinted("3931"));
                Assert.False(history.TryRecordPrint("3931"));
                history.SaveLabelImage("3932", "novo", Bitmap(3));
                Assert.NotNull(history.GetLabelImage("3932"));
            }

            using (var reopened = new LabelHistory(path, _clock))
                Assert.NotNull(reopened.GetLabelImage("3932"));
        }
        finally
        {
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                File.Delete(file);
        }
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
