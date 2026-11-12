using LabelGuard.Core.History;

namespace LabelGuard.Tests.History;

public class ReleasePrintTests : IDisposable
{
    private readonly LabelHistory _history = new(":memory:");

    public void Dispose() => _history.Dispose();

    [Fact]
    public void Release_AllowsIdToBePrintedAgain()
    {
        _history.TryRecordPrint("3931");

        Assert.True(_history.ReleasePrint("3931"));

        Assert.False(_history.WasPrinted("3931"));
        Assert.True(_history.TryRecordPrint("3931"));
    }

    [Fact]
    public void Release_KeepsAuthorizedReprints()
    {
        _history.TryRecordPrint("3931");
        _history.RecordAuthorizedReprint("3931", "supervisor");

        _history.ReleasePrint("3931");

        Assert.Equal("supervisor", Assert.Single(_history.GetPrints("3931")).AuthorizedBy);
    }

    [Fact]
    public void Release_UnknownId_ReturnsFalse()
    {
        Assert.False(_history.ReleasePrint("3931"));
    }
}
