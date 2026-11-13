using System.Net;
using LabelGuard.Core.Guard;
using LabelGuard.Core.Receiving;

namespace LabelGuard.Core.Hosting;

/// <summary>
/// Junta receptor, motor de decisão e envio: recebe os trabalhos da fila protegida, decide e envia.
/// Usado pelo aplicativo da bandeja e pelo console. Os eventos são disparados em threads de fundo.
/// </summary>
public sealed class GuardHost : IAsyncDisposable
{
    private readonly PrintGuard _guard;
    private readonly ILabelSender _sender;
    private readonly string? _saveJobsTo;
    private readonly TcpJobReceiver _receiver;
    private readonly CancellationTokenSource _cts = new();
    private Task _running = Task.CompletedTask;

    public GuardHost(PrintGuard guard, ILabelSender sender, IPEndPoint endpoint, string? saveJobsTo = null)
    {
        _guard = guard;
        _sender = sender;
        _saveJobsTo = string.IsNullOrWhiteSpace(saveJobsTo) ? null : saveJobsTo;
        _receiver = new TcpJobReceiver(endpoint, HandleJobAsync, Notify);
    }

    /// <summary>Um trabalho foi processado (com etiquetas impressas, bloqueadas ou com falha).</summary>
    public event Action<JobResult>? JobProcessed;

    /// <summary>Avisos do receptor (erros, trabalhos descartados).</summary>
    public event Action<string>? Message;

    public IPEndPoint LocalEndpoint => _receiver.LocalEndpoint;

    /// <summary>Abre a porta e começa a receber. Lança <see cref="System.Net.Sockets.SocketException"/> se a porta estiver em uso.</summary>
    public void Start()
    {
        if (_saveJobsTo is not null)
            Directory.CreateDirectory(_saveJobsTo);
        _receiver.Start();
        // Em segundo plano: chamado da thread da tela, o processamento (leitura de QR) não pode travá-la.
        _running = Task.Run(() => _receiver.RunAsync(_cts.Token));
    }

    /// <summary>Libera uma etiqueta bloqueada com autorização do supervisor e a envia pelo mesmo caminho.</summary>
    public Task<LabelOutcome> AuthorizeAsync(LabelOutcome blocked, string supervisor, CancellationToken cancellationToken = default) =>
        _guard.AuthorizeAsync(blocked, supervisor, _sender, cancellationToken);

    /// <summary>Reimprime pelo número a etiqueta guardada, com autorização do supervisor.</summary>
    public Task<ReprintResult> ReprintAsync(string labelId, string supervisor, CancellationToken cancellationToken = default) =>
        _guard.ReprintAsync(labelId, supervisor, _sender, cancellationToken);

    public async Task StopAsync()
    {
        _cts.Cancel();
        await _running;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts.Dispose();
    }

    private async Task HandleJobAsync(ReceivedJob job, CancellationToken cancellationToken)
    {
        if (_saveJobsTo is not null)
            await File.WriteAllBytesAsync(Path.Combine(_saveJobsTo, job.Name + ".prn"), job.Data, cancellationToken);

        var result = await _guard.ProcessAsync(job.Data, job.Name, _sender, cancellationToken);

        // Um erro em quem está ouvindo não pode parar o processamento das etiquetas.
        try
        {
            JobProcessed?.Invoke(result);
        }
        catch (Exception ex)
        {
            Notify($"Erro ao mostrar o resultado do trabalho {job.Name}: {ex.Message}");
        }
    }

    private void Notify(string message)
    {
        try
        {
            Message?.Invoke(message);
        }
        catch
        {
            // Sem ter onde avisar; o histórico continua registrando as decisões.
        }
    }
}
