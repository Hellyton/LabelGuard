using System.Text;
using LabelGuard.Core.Guard;

namespace LabelGuard.Tests.Guard;

public class CommandPrefixSenderTests
{
    [Fact]
    public async Task Prefix_IsSentBeforeEachLabel()
    {
        var inner = new RecordingSender();
        var sender = new CommandPrefixSender(inner, CommandPrefixSender.TsplReprintOff);

        await sender.SendAsync(Encoding.ASCII.GetBytes("CLS\r\nPRINT 1,1\r\n"));
        await sender.SendAsync(Encoding.ASCII.GetBytes("CLS\r\nPRINT 1,1\r\n"));

        Assert.Equal(2, inner.Sent.Count);
        Assert.All(inner.Sent, s => Assert.Equal("SET REPRINT OFF\r\nCLS\r\nPRINT 1,1\r\n", s));
    }

    private sealed class RecordingSender : ILabelSender
    {
        public List<string> Sent { get; } = new();

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            Sent.Add(Encoding.ASCII.GetString(data.Span));
            return Task.CompletedTask;
        }
    }
}
