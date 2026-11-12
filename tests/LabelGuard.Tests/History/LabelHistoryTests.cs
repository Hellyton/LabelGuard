using LabelGuard.Core.History;

namespace LabelGuard.Tests.History;

public class LabelHistoryTests : IDisposable
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 12, 15, 2, 0, TimeSpan.Zero));
    private readonly LabelHistory _history;

    public LabelHistoryTests() => _history = new LabelHistory(":memory:", _clock);

    public void Dispose() => _history.Dispose();

    [Fact]
    public void NewId_IsRecorded()
    {
        Assert.False(_history.WasPrinted("3931"));

        Assert.True(_history.TryRecordPrint("3931", "job-1"));

        Assert.True(_history.WasPrinted("3931"));
        var print = Assert.Single(_history.GetPrints("3931"));
        Assert.Equal(new PrintRecord("3931", _clock.Now, null, "job-1"), print);
    }

    [Fact]
    public void RepeatedId_IsRejected()
    {
        _history.TryRecordPrint("3931");

        Assert.False(_history.TryRecordPrint("3931"));
        Assert.Single(_history.GetPrints("3931"));
    }

    [Fact]
    public void DifferentIds_AreIndependent()
    {
        Assert.True(_history.TryRecordPrint("3931"));
        Assert.True(_history.TryRecordPrint("3932"));
        Assert.False(_history.WasPrinted("3933"));
    }

    [Fact]
    public void AuthorizedReprint_IsRecordedWithSupervisor()
    {
        _history.TryRecordPrint("3931");
        _clock.Now = _clock.Now.AddMinutes(10);

        _history.RecordAuthorizedReprint("3931", "supervisor", "job-2");

        var prints = _history.GetPrints("3931");
        Assert.Equal(2, prints.Count);
        Assert.Null(prints[0].AuthorizedBy);
        Assert.Equal("supervisor", prints[1].AuthorizedBy);
        Assert.Equal(_clock.Now, prints[1].PrintedAt);
        Assert.False(_history.TryRecordPrint("3931"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AuthorizedReprint_RequiresSupervisor(string supervisor)
    {
        Assert.Throws<ArgumentException>(() => _history.RecordAuthorizedReprint("3931", supervisor));
    }

    [Fact]
    public void ConcurrentPrints_OnlyOneWins()
    {
        var wins = 0;
        Parallel.For(0, 50, _ =>
        {
            if (_history.TryRecordPrint("3931"))
                Interlocked.Increment(ref wins);
        });

        Assert.Equal(1, wins);
    }

    [Fact]
    public void Events_AreReturnedNewestFirst()
    {
        _history.Log(EventType.Printed, "impressa", "3931", "job-1");
        _clock.Now = _clock.Now.AddSeconds(1);
        _history.Log(EventType.Blocked, "repetida", "3931", "job-2");
        _history.Log(EventType.Error, "impressora offline");

        var events = _history.GetRecentEvents();

        Assert.Equal(new[] { EventType.Error, EventType.Blocked, EventType.Printed }, events.Select(e => e.Type));
        Assert.Null(events[0].LabelId);
        Assert.Null(events[0].Job);
        Assert.Equal("repetida", events[1].Message);
        Assert.Equal("job-2", events[1].Job);
        Assert.Equal(_clock.Now, events[1].At);
        Assert.Single(_history.GetRecentEvents(1));
    }

    [Fact]
    public void Database_PersistsAfterReopen()
    {
        var path = Path.Combine(Path.GetTempPath(), $"labelguard-{Guid.NewGuid():N}.db");
        try
        {
            using (var history = new LabelHistory(path, _clock))
            {
                history.TryRecordPrint("3931");
                history.Log(EventType.Printed, "impressa", "3931");
            }

            using (var reopened = new LabelHistory(path, _clock))
            {
                Assert.True(reopened.WasPrinted("3931"));
                Assert.False(reopened.TryRecordPrint("3931"));
                Assert.Single(reopened.GetRecentEvents());
            }
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
