using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using LabelGuard.Core.Guard;
using LabelGuard.Core.History;
using LabelGuard.Core.Hosting;
using LabelGuard.Core.Parsing;

namespace LabelGuard.Tests.Hosting;

public class GuardHostTests : IAsyncLifetime
{
    private readonly LabelHistory _history = new(":memory:");
    private readonly RecordingSender _sender = new();
    private readonly ConcurrentQueue<JobResult> _results = new();
    private readonly string _saveDir = Path.Combine(Path.GetTempPath(), $"labelguard-host-{Guid.NewGuid():N}");
    private GuardHost _host = null!;

    public Task InitializeAsync()
    {
        _host = new GuardHost(
            new PrintGuard(_history, new IdExtractor()), _sender, new IPEndPoint(IPAddress.Loopback, 0), _saveDir);
        _host.JobProcessed += _results.Enqueue;
        _host.Start();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        _history.Dispose();
        if (Directory.Exists(_saveDir))
            Directory.Delete(_saveDir, recursive: true);
    }

    private static string Zpl(string pallet) => $"^XA^FDhttps://app.papersystem.com.br/d/etiqueta?pallet={pallet}^FS^XZ";

    private async Task<JobResult> SendAsync(string job)
    {
        var before = _results.Count;
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(_host.LocalEndpoint);
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.Latin1.GetBytes(job));
            client.Client.Shutdown(SocketShutdown.Send);
            await stream.ReadAsync(new byte[1]);
        }

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (_results.Count == before && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        return _results.ToArray()[before];
    }

    [Fact]
    public async Task ReceivedJob_IsProcessedSavedAndReported()
    {
        var result = await SendAsync(Zpl("3931"));

        Assert.Equal(LabelStatus.Printed, Assert.Single(result.Labels).Status);
        Assert.Equal(Zpl("3931"), Assert.Single(_sender.Sent));
        var saved = Assert.Single(Directory.GetFiles(_saveDir, "*.prn"));
        Assert.Equal(Zpl("3931"), File.ReadAllText(saved, Encoding.Latin1));
    }

    [Fact]
    public async Task BlockedLabel_CanBeAuthorized()
    {
        await SendAsync(Zpl("3931"));
        var blocked = Assert.Single((await SendAsync(Zpl("3931"))).Blocked);

        var outcome = await _host.AuthorizeAsync(blocked, "supervisor");

        Assert.Equal(LabelStatus.Printed, outcome.Status);
        Assert.Equal(2, _sender.Sent.Count);
        Assert.Equal("supervisor", _history.GetPrints("3931")[1].AuthorizedBy);
    }

    [Fact]
    public async Task ListenerError_DoesNotStopProcessing()
    {
        var messages = new ConcurrentQueue<string>();
        _host.Message += messages.Enqueue;
        _host.JobProcessed += _ => throw new InvalidOperationException("tela quebrou");

        await SendAsync(Zpl("3931"));
        var second = await SendAsync(Zpl("3932"));

        Assert.Equal(LabelStatus.Printed, Assert.Single(second.Labels).Status);
        Assert.Contains(messages, m => m.Contains("tela quebrou"));
    }

    private sealed class RecordingSender : ILabelSender
    {
        public ConcurrentQueue<string> Queue { get; } = new();

        public List<string> Sent => Queue.ToList();

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            Queue.Enqueue(Encoding.Latin1.GetString(data.Span));
            return Task.CompletedTask;
        }
    }
}
