using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace LabelGuard.Core.Receiving;

/// <summary>Um trabalho de impressão recebido da fila protegida do Windows.</summary>
public sealed record ReceivedJob(string Name, byte[] Data, DateTimeOffset ReceivedAt);

/// <param name="IdleTimeout">Sem dados por esse tempo, o trabalho é considerado completo e a conexão é fechada.</param>
/// <param name="MaxJobBytes">Trabalhos maiores são descartados.</param>
public sealed record ReceiverOptions(TimeSpan IdleTimeout, long MaxJobBytes)
{
    public static ReceiverOptions Default { get; } = new(TimeSpan.FromSeconds(60), 512L * 1024 * 1024);
}

/// <summary>
/// Servidor RAW (porta 9100) para a porta TCP/IP padrão do Windows: cada conexão é um trabalho,
/// que termina quando o Windows fecha a conexão. Os trabalhos são entregues um de cada vez, na ordem
/// em que terminam de chegar, para as etiquetas saírem na impressora na mesma ordem.
/// </summary>
public sealed class TcpJobReceiver
{
    private readonly IPEndPoint _endpoint;
    private readonly Func<ReceivedJob, CancellationToken, Task> _handler;
    private readonly Action<string> _log;
    private readonly ReceiverOptions _options;
    private readonly SemaphoreSlim _sequential = new(1, 1);
    private readonly TimeProvider _clock;
    private int _sequence;
    private TcpListener? _listener;

    /// <param name="handler">Processa um trabalho; exceções são registradas em <paramref name="log"/> e não param o servidor.</param>
    public TcpJobReceiver(
        IPEndPoint endpoint,
        Func<ReceivedJob, CancellationToken, Task> handler,
        Action<string> log,
        ReceiverOptions? options = null,
        TimeProvider? clock = null)
    {
        _endpoint = endpoint;
        _handler = handler;
        _log = log;
        _options = options ?? ReceiverOptions.Default;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Endereço em uso (útil com porta 0, que escolhe uma porta livre). Disponível depois de <see cref="Start"/>.</summary>
    public IPEndPoint LocalEndpoint =>
        (IPEndPoint)(_listener ?? throw new InvalidOperationException("Receptor não iniciado.")).LocalEndpoint;

    /// <summary>Abre a porta. Lança <see cref="SocketException"/> se ela já estiver em uso.</summary>
    public void Start()
    {
        _listener = new TcpListener(_endpoint);
        _listener.Start();
    }

    /// <summary>Aceita conexões até o cancelamento.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_listener is null)
            Start();
        var connections = new List<Task>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                connections.RemoveAll(t => t.IsCompleted);
                connections.Add(HandleConnectionAsync(client, cancellationToken));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _listener!.Stop();
            await Task.WhenAll(connections);
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var name = string.Create(
            CultureInfo.InvariantCulture,
            $"{_clock.GetLocalNow():yyyyMMdd-HHmmss}-{Interlocked.Increment(ref _sequence)}");
        try
        {
            byte[]? data;
            using (client)
                data = await ReadJobAsync(client, name, cancellationToken);
            if (data is null || data.Length == 0)
                return;

            await _sequential.WaitAsync(cancellationToken);
            try
            {
                await _handler(new ReceivedJob(name, data, _clock.GetUtcNow()), cancellationToken);
            }
            finally
            {
                _sequential.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log($"Trabalho {name}: erro: {ex.Message}");
        }
    }

    /// <summary>Lê até o Windows fechar a conexão. Retorna null se o trabalho passar do limite.</summary>
    private async Task<byte[]?> ReadJobAsync(TcpClient client, string name, CancellationToken cancellationToken)
    {
        var stream = client.GetStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            idle.CancelAfter(_options.IdleTimeout);
            int read;
            try
            {
                read = await stream.ReadAsync(chunk, idle.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _log($"Trabalho {name}: sem dados por {_options.IdleTimeout.TotalSeconds:0} s, processando o que chegou");
                break;
            }

            if (read == 0)
                break;
            if (buffer.Length + read > _options.MaxJobBytes)
            {
                _log($"Trabalho {name}: maior que {_options.MaxJobBytes:N0} bytes, descartado");
                return null;
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
