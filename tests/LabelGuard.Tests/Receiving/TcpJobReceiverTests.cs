using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using LabelGuard.Core.Receiving;

namespace LabelGuard.Tests.Receiving;

public class TcpJobReceiverTests : IAsyncLifetime
{
    private readonly ConcurrentQueue<ReceivedJob> _jobs = new();
    private readonly ConcurrentQueue<string> _log = new();
    private CancellationTokenSource _cts = new();
    private TcpJobReceiver _receiver = null!;
    private Task _running = Task.CompletedTask;
    private Func<ReceivedJob, Task> _onJob = _ => Task.CompletedTask;

    public Task InitializeAsync() => StartAsync(ReceiverOptions.Default);

    private Task StartAsync(ReceiverOptions options)
    {
        _receiver = new TcpJobReceiver(
            new IPEndPoint(IPAddress.Loopback, 0),
            async (job, _) =>
            {
                await _onJob(job);
                _jobs.Enqueue(job);
            },
            _log.Enqueue,
            options);
        _receiver.Start();
        _running = _receiver.RunAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _cts.Cancel();
        await _running;
    }

    private async Task SendAsync(byte[] data, bool close = true)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(_receiver.LocalEndpoint);
        var stream = client.GetStream();
        await stream.WriteAsync(data);
        if (close)
            client.Client.Shutdown(SocketShutdown.Send);
        // Espera o servidor terminar de ler e fechar a conexão.
        await stream.ReadAsync(new byte[1]);
    }

    private async Task<ReceivedJob[]> WaitForJobsAsync(int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (_jobs.Count < count && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        return _jobs.ToArray();
    }

    [Fact]
    public async Task Connection_IsDeliveredAsOneJob()
    {
        var data = Enumerable.Range(0, 300_000).Select(i => (byte)i).ToArray();

        await SendAsync(data);

        var job = Assert.Single(await WaitForJobsAsync(1));
        Assert.Equal(data, job.Data);
        Assert.False(string.IsNullOrEmpty(job.Name));
    }

    [Fact]
    public async Task Jobs_AreDeliveredOneAtATimeInOrder()
    {
        var running = 0;
        var maxRunning = 0;
        _onJob = async _ =>
        {
            var now = Interlocked.Increment(ref running);
            maxRunning = Math.Max(maxRunning, now);
            await Task.Delay(50);
            Interlocked.Decrement(ref running);
        };

        await SendAsync(new byte[] { 1 });
        await SendAsync(new byte[] { 2 });
        await SendAsync(new byte[] { 3 });

        var jobs = await WaitForJobsAsync(3);
        Assert.Equal(new byte[] { 1, 2, 3 }, jobs.Select(j => j.Data[0]));
        Assert.Equal(1, maxRunning);
        Assert.Equal(3, jobs.Select(j => j.Name).Distinct().Count());
    }

    [Fact]
    public async Task EmptyConnection_IsIgnored()
    {
        await SendAsync(Array.Empty<byte>());
        await SendAsync(new byte[] { 9 });

        var job = Assert.Single(await WaitForJobsAsync(1));
        Assert.Equal(new byte[] { 9 }, job.Data);
    }

    [Fact]
    public async Task HandlerError_IsLoggedAndServerKeepsRunning()
    {
        _onJob = job => job.Data[0] == 1 ? throw new InvalidOperationException("falhou") : Task.CompletedTask;

        await SendAsync(new byte[] { 1 });
        await SendAsync(new byte[] { 2 });

        Assert.Equal(new byte[] { 2 }, Assert.Single(await WaitForJobsAsync(1)).Data);
        Assert.Contains(_log, l => l.Contains("falhou"));
    }

    [Fact]
    public async Task IdleConnection_IsProcessedAfterTimeout()
    {
        await DisposeAsync();
        _cts = new CancellationTokenSource();
        await StartAsync(ReceiverOptions.Default with { IdleTimeout = TimeSpan.FromMilliseconds(200) });

        await SendAsync(new byte[] { 7 }, close: false);

        Assert.Equal(new byte[] { 7 }, Assert.Single(await WaitForJobsAsync(1)).Data);
        Assert.Contains(_log, l => l.Contains("sem dados"));
    }

    [Fact]
    public async Task OversizedJob_IsDiscarded()
    {
        await DisposeAsync();
        _cts = new CancellationTokenSource();
        await StartAsync(ReceiverOptions.Default with { MaxJobBytes = 10 });

        await SendAsync(new byte[100]);
        await SendAsync(new byte[] { 5 });

        Assert.Equal(new byte[] { 5 }, Assert.Single(await WaitForJobsAsync(1)).Data);
        Assert.Contains(_log, l => l.Contains("descartado"));
    }
}
