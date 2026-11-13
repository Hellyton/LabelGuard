using System.Text;
using LabelGuard.Core.Guard;
using LabelGuard.Core.History;
using LabelGuard.Core.Parsing;
using LabelGuard.Tests.Parsing;

namespace LabelGuard.Tests.Guard;

public class PrintGuardTests : IDisposable
{
    private readonly LabelHistory _history = new(":memory:");
    private readonly FakeSender _sender = new();

    public void Dispose() => _history.Dispose();

    private PrintGuard Guard(GuardOptions? options = null) => new(_history, new IdExtractor(), options);

    private static string Zpl(string pallet, string extra = "") =>
        $"^XA^FDhttps://app.papersystem.com.br/d/etiqueta?pallet={pallet}^FS{extra}^XZ";

    private static byte[] Job(params string[] labels) => Encoding.Latin1.GetBytes(string.Concat(labels));

    [Fact]
    public async Task NewLabels_ArePrintedInOrder()
    {
        var result = await Guard().ProcessAsync(Job(Zpl("3931"), Zpl("3932")), "job-1", _sender);

        Assert.All(result.Labels, l => Assert.Equal(LabelStatus.Printed, l.Status));
        Assert.Equal(new[] { Zpl("3931"), Zpl("3932") }, _sender.Sent);
        Assert.True(_history.WasPrinted("3931"));
        Assert.True(_history.WasPrinted("3932"));
        Assert.False(result.HasBlocked);
    }

    [Fact]
    public async Task RepeatedJob_IsBlocked()
    {
        var guard = Guard();
        await guard.ProcessAsync(Job(Zpl("3931")), "job-1", _sender);

        var result = await guard.ProcessAsync(Job(Zpl("3931")), "job-2", _sender);

        var label = Assert.Single(result.Labels);
        Assert.Equal(LabelStatus.Duplicate, label.Status);
        Assert.Equal("job-1", Assert.Single(label.PreviousPrints).Job);
        Assert.Single(_sender.Sent);
        Assert.Equal(EventType.Blocked, _history.GetRecentEvents(1)[0].Type);
    }

    [Fact]
    public async Task ReprintedBatch_BlocksOnlyLabelsAlreadyPrinted()
    {
        // Bobina acabou depois da etiqueta 2 e o Windows reenviou o lote inteiro.
        var guard = Guard();
        await guard.ProcessAsync(Job(Zpl("3931"), Zpl("3932")), "job-1", _sender);
        _sender.Sent.Clear();

        var result = await guard.ProcessAsync(Job(Zpl("3931"), Zpl("3932"), Zpl("3933"), Zpl("3934")), "job-2", _sender);

        Assert.Equal(
            new[] { LabelStatus.Duplicate, LabelStatus.Duplicate, LabelStatus.Printed, LabelStatus.Printed },
            result.Labels.Select(l => l.Status));
        Assert.Equal(new[] { Zpl("3933"), Zpl("3934") }, _sender.Sent);
    }

    [Fact]
    public async Task SameIdTwiceInOneJob_BlocksSecond()
    {
        var result = await Guard().ProcessAsync(Job(Zpl("3931"), Zpl("3931")), "job-1", _sender);

        Assert.Equal(new[] { LabelStatus.Printed, LabelStatus.Duplicate }, result.Labels.Select(l => l.Status));
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task LabelWithoutId_IsBlockedByDefault()
    {
        var result = await Guard().ProcessAsync(Job("^XA^FDsem id^FS^XZ"), "job-1", _sender);

        Assert.Equal(LabelStatus.NoId, Assert.Single(result.Labels).Status);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task LabelWithoutId_CanBePrintedByOption()
    {
        var result = await Guard(new GuardOptions(PrintWithoutId: true))
            .ProcessAsync(Job("^XA^FDsem id^FS^XZ"), "job-1", _sender);

        Assert.Equal(LabelStatus.Printed, Assert.Single(result.Labels).Status);
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task MultipleCopies_AreBlockedByDefault()
    {
        var result = await Guard().ProcessAsync(Job(Zpl("3931", "^PQ2")), "job-1", _sender);

        Assert.Equal(LabelStatus.MultipleCopies, Assert.Single(result.Labels).Status);
        Assert.Empty(_sender.Sent);
        Assert.False(_history.WasPrinted("3931"));
    }

    [Fact]
    public async Task MultipleCopies_CanBeAllowedByOption()
    {
        var result = await Guard(new GuardOptions(AllowMultipleCopies: true))
            .ProcessAsync(Job(Zpl("3931", "^PQ2")), "job-1", _sender);

        Assert.Equal(LabelStatus.Printed, Assert.Single(result.Labels).Status);
    }

    [Fact]
    public async Task SendFailure_ReleasesIdAndStopsJob()
    {
        _sender.FailOnCall = 2;

        var result = await Guard().ProcessAsync(Job(Zpl("3931"), Zpl("3932"), Zpl("3933")), "job-1", _sender);

        Assert.Equal(
            new[] { LabelStatus.Printed, LabelStatus.Failed, LabelStatus.NotSent },
            result.Labels.Select(l => l.Status));
        Assert.Equal("impressora offline", result.Labels[1].Error);
        Assert.True(result.HasFailure);
        Assert.True(_history.WasPrinted("3931"));
        Assert.False(_history.WasPrinted("3932"));
        Assert.False(_history.WasPrinted("3933"));
        Assert.Contains(_history.GetRecentEvents(), e => e.Type == EventType.Error && e.LabelId == "3932");
    }

    [Fact]
    public async Task AfterFailure_ResendingJobPrintsTheRest()
    {
        var guard = Guard();
        _sender.FailOnCall = 2;
        await guard.ProcessAsync(Job(Zpl("3931"), Zpl("3932")), "job-1", _sender);
        _sender.FailOnCall = null;

        var result = await guard.ProcessAsync(Job(Zpl("3931"), Zpl("3932")), "job-2", _sender);

        Assert.Equal(new[] { LabelStatus.Duplicate, LabelStatus.Printed }, result.Labels.Select(l => l.Status));
    }

    [Fact]
    public async Task Cancellation_ReleasesId()
    {
        _sender.CancelOnCall = 1;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Guard().ProcessAsync(Job(Zpl("3931")), "job-1", _sender));

        Assert.False(_history.WasPrinted("3931"));
    }

    [Fact]
    public async Task Authorize_Duplicate_SendsAndRecordsSupervisor()
    {
        var guard = Guard();
        await guard.ProcessAsync(Job(Zpl("3931")), "job-1", _sender);
        var blocked = Assert.Single((await guard.ProcessAsync(Job(Zpl("3931")), "job-2", _sender)).Blocked);

        var outcome = await guard.AuthorizeAsync(blocked, "supervisor", _sender);

        Assert.Equal(LabelStatus.Printed, outcome.Status);
        Assert.Equal(2, _sender.Sent.Count);
        var prints = _history.GetPrints("3931");
        Assert.Equal(2, prints.Count);
        Assert.Equal("supervisor", prints[1].AuthorizedBy);
        Assert.Equal("job-2", prints[1].Job);
        Assert.Equal(EventType.ReprintAuthorized, _history.GetRecentEvents(1)[0].Type);
    }

    [Fact]
    public async Task Authorize_MultipleCopies_RecordsFirstPrint()
    {
        var guard = Guard();
        var blocked = Assert.Single((await guard.ProcessAsync(Job(Zpl("3931", "^PQ2")), "job-1", _sender)).Blocked);

        await guard.AuthorizeAsync(blocked, "supervisor", _sender);

        Assert.Null(Assert.Single(_history.GetPrints("3931")).AuthorizedBy);
        Assert.Equal(Zpl("3931", "^PQ2"), Assert.Single(_sender.Sent));
    }

    [Fact]
    public async Task Authorize_SendFailure_KeepsLabelBlocked()
    {
        var guard = Guard();
        await guard.ProcessAsync(Job(Zpl("3931")), "job-1", _sender);
        var blocked = Assert.Single((await guard.ProcessAsync(Job(Zpl("3931")), "job-2", _sender)).Blocked);
        _sender.FailOnCall = 2;

        var outcome = await guard.AuthorizeAsync(blocked, "supervisor", _sender);

        Assert.True(outcome.IsBlocked);
        Assert.Equal("impressora offline", outcome.Error);
        Assert.Single(_history.GetPrints("3931"));

        _sender.FailOnCall = null;
        Assert.Equal(LabelStatus.Printed, (await guard.AuthorizeAsync(outcome, "supervisor", _sender)).Status);
    }

    [Fact]
    public async Task Authorize_LabelNotBlocked_Throws()
    {
        var guard = Guard();
        var printed = Assert.Single((await guard.ProcessAsync(Job(Zpl("3931")), "job-1", _sender)).Labels);

        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.AuthorizeAsync(printed, "supervisor", _sender));
    }

    [Fact]
    public async Task PapersystemBitmapJob_IsBlockedOnSecondPrint()
    {
        var job = TsplJob.WithQrCode("https://app.papersystem.com.br/d/etiqueta?pallet=3931&t=1");
        var guard = Guard();

        var first = await guard.ProcessAsync(job, "job-1", _sender);
        var second = await guard.ProcessAsync(job, "job-2", _sender);

        Assert.Equal(LabelStatus.Printed, Assert.Single(first.Labels).Status);
        var blocked = Assert.Single(second.Labels);
        Assert.Equal(LabelStatus.Duplicate, blocked.Status);
        Assert.Equal("3931", blocked.Id);
    }

    [Fact]
    public async Task PrintedLabel_IsStoredForReprint()
    {
        await Guard().ProcessAsync(Job(Zpl("3931"), Zpl("3932")), "job-1", _sender);

        Assert.Equal(Zpl("3931"), Encoding.Latin1.GetString(_history.GetLabelImage("3931")!.Data));
        Assert.Equal("job-1", _history.GetLabelImage("3932")!.Job);
    }

    [Fact]
    public async Task BlockedOrFailedLabel_IsNotStored()
    {
        _sender.FailOnCall = 1;

        await Guard().ProcessAsync(Job(Zpl("3931"), "^XA^FDsem id^FS^XZ"), "job-1", _sender);

        Assert.Null(_history.GetLabelImage("3931"));
    }

    [Fact]
    public async Task Reprint_SendsStoredLabelAndRecordsSupervisor()
    {
        var guard = Guard();
        await guard.ProcessAsync(Job(Zpl("3931")), "job-1", _sender);

        var result = await guard.ReprintAsync("3931", "supervisor", _sender);

        Assert.Equal(ReprintStatus.Printed, result.Status);
        Assert.Equal(new[] { Zpl("3931"), Zpl("3931") }, _sender.Sent);
        var prints = _history.GetPrints("3931");
        Assert.Equal(2, prints.Count);
        Assert.Equal("supervisor", prints[1].AuthorizedBy);
        Assert.Equal(EventType.ReprintAuthorized, _history.GetRecentEvents(1)[0].Type);
    }

    [Fact]
    public async Task Reprint_UnknownPallet_SendsNothing()
    {
        var result = await Guard().ReprintAsync("9999", "supervisor", _sender);

        Assert.Equal(ReprintStatus.NotFound, result.Status);
        Assert.Empty(_sender.Sent);
        Assert.Empty(_history.GetPrints("9999"));
    }

    [Fact]
    public async Task Reprint_SendFailure_RecordsNothing()
    {
        var guard = Guard();
        await guard.ProcessAsync(Job(Zpl("3931")), "job-1", _sender);
        _sender.FailOnCall = 2;

        var result = await guard.ReprintAsync("3931", "supervisor", _sender);

        Assert.Equal(ReprintStatus.Failed, result.Status);
        Assert.Equal("impressora offline", result.Error);
        Assert.Single(_history.GetPrints("3931"));
    }

    [Fact]
    public async Task Authorize_StoresLabelForReprint()
    {
        var guard = Guard();
        var blocked = Assert.Single((await guard.ProcessAsync(Job(Zpl("3931", "^PQ2")), "job-1", _sender)).Blocked);

        await guard.AuthorizeAsync(blocked, "supervisor", _sender);

        Assert.NotNull(_history.GetLabelImage("3931"));
    }

    private sealed class FakeSender : ILabelSender
    {
        private int _calls;

        public List<string> Sent { get; } = new();

        public int? FailOnCall { get; set; }

        public int? CancelOnCall { get; set; }

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            _calls++;
            if (_calls == CancelOnCall)
                throw new OperationCanceledException();
            if (_calls == FailOnCall)
                throw new IOException("impressora offline");
            Sent.Add(Encoding.Latin1.GetString(data.Span));
            return Task.CompletedTask;
        }
    }
}
