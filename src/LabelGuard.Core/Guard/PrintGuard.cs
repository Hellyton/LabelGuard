using System.Globalization;
using LabelGuard.Core.History;
using LabelGuard.Core.Parsing;

namespace LabelGuard.Core.Guard;

/// <summary>
/// Motor de decisão: separa o trabalho em etiquetas e, para cada uma, imprime ou bloqueia.
/// O ID é reservado no histórico antes do envio e liberado se o envio falhar, então uma etiqueta
/// que não saiu não fica marcada como impressa e dois trabalhos simultâneos não liberam o mesmo ID.
/// </summary>
public sealed class PrintGuard
{
    private readonly LabelHistory _history;
    private readonly IdExtractor _extractor;
    private readonly GuardOptions _options;

    public PrintGuard(LabelHistory history, IdExtractor extractor, GuardOptions? options = null)
    {
        _history = history;
        _extractor = extractor;
        _options = options ?? new GuardOptions();
    }

    /// <summary>
    /// Processa um trabalho na ordem das etiquetas. Se um envio falhar, as etiquetas seguintes
    /// não são enviadas (<see cref="LabelStatus.NotSent"/>), para não imprimir fora de ordem.
    /// </summary>
    public async Task<JobResult> ProcessAsync(
        byte[] job, string jobName, ILabelSender sender, CancellationToken cancellationToken = default)
    {
        var labels = LabelSplitter.Split(job);
        var outcomes = new List<LabelOutcome>(labels.Count);
        var failed = false;

        for (var i = 0; i < labels.Count; i++)
        {
            var outcome = failed
                ? new LabelOutcome(jobName, i + 1, labels[i], null, LabelStatus.NotSent, Array.Empty<PrintRecord>())
                : await ProcessLabelAsync(jobName, i + 1, labels[i], sender, cancellationToken);
            failed |= outcome.Status == LabelStatus.Failed;
            outcomes.Add(outcome);
        }

        return new JobResult(jobName, outcomes);
    }

    /// <summary>
    /// Libera uma etiqueta bloqueada com autorização do supervisor e a envia.
    /// Se o envio falhar, devolve a etiqueta ainda bloqueada com <see cref="LabelOutcome.Error"/> preenchido.
    /// </summary>
    public async Task<LabelOutcome> AuthorizeAsync(
        LabelOutcome blocked, string supervisor, ILabelSender sender, CancellationToken cancellationToken = default)
    {
        if (!blocked.IsBlocked)
            throw new InvalidOperationException("A etiqueta não está bloqueada.");
        ArgumentException.ThrowIfNullOrWhiteSpace(supervisor);

        var error = await TrySendAsync(blocked.Label.RawData, sender, cancellationToken);
        if (error is not null)
        {
            _history.Log(EventType.Error, $"Falha ao enviar {Name(blocked.Id, blocked.Index)}: {error}", blocked.Id, blocked.Job);
            return blocked with { Error = error };
        }

        if (blocked.Id is not null)
        {
            if (!_history.TryRecordPrint(blocked.Id, blocked.Job))
                _history.RecordAuthorizedReprint(blocked.Id, supervisor, blocked.Job);
            SaveImage(blocked.Id, blocked.Job, blocked.Label);
        }
        _history.Log(
            EventType.ReprintAuthorized,
            $"{Name(blocked.Id, blocked.Index)} liberada por {supervisor}",
            blocked.Id,
            blocked.Job);
        return blocked with { Status = LabelStatus.Printed, Error = null };
    }

    /// <summary>
    /// Reimprime pelo número, sem o Papersystem: envia a etiqueta guardada na última impressão do ID
    /// e registra a reimpressão em nome do supervisor.
    /// </summary>
    public async Task<ReprintResult> ReprintAsync(
        string labelId, string supervisor, ILabelSender sender, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(supervisor);
        var stored = _history.GetLabelImage(labelId);
        if (stored is null)
            return new ReprintResult(ReprintStatus.NotFound, null, null);

        var error = await TrySendAsync(stored.Data, sender, cancellationToken);
        if (error is not null)
        {
            _history.Log(EventType.Error, $"Falha ao reimprimir etiqueta {labelId}: {error}", labelId, stored.Job);
            return new ReprintResult(ReprintStatus.Failed, stored, error);
        }

        _history.RecordAuthorizedReprint(labelId, supervisor, stored.Job);
        _history.Log(EventType.ReprintAuthorized, $"Etiqueta {labelId} reimpressa pelo menu, liberada por {supervisor}", labelId, stored.Job);
        return new ReprintResult(ReprintStatus.Printed, stored, null);
    }

    private async Task<LabelOutcome> ProcessLabelAsync(
        string job, int index, Label label, ILabelSender sender, CancellationToken cancellationToken)
    {
        var id = _extractor.Extract(label);
        var none = Array.Empty<PrintRecord>();

        if (id is null)
        {
            if (!_options.PrintWithoutId)
            {
                _history.Log(EventType.NoId, $"Etiqueta #{index} bloqueada: ID não encontrado", null, job);
                return new LabelOutcome(job, index, label, null, LabelStatus.NoId, none);
            }
            return await SendAsync(job, index, label, null, sender, cancellationToken);
        }

        if (_history.WasPrinted(id))
            return Duplicate(job, index, label, id);

        if (label.Copies > 1 && !_options.AllowMultipleCopies)
        {
            _history.Log(EventType.Blocked, $"Etiqueta {id} bloqueada: pede {label.Copies} cópias", id, job);
            return new LabelOutcome(job, index, label, id, LabelStatus.MultipleCopies, none);
        }

        // Reserva atômica: se outro trabalho registrou o mesmo ID entre a consulta e aqui, bloqueia.
        if (!_history.TryRecordPrint(id, job))
            return Duplicate(job, index, label, id);

        try
        {
            return await SendAsync(job, index, label, id, sender, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _history.ReleasePrint(id);
            throw;
        }
    }

    private LabelOutcome Duplicate(string job, int index, Label label, string id)
    {
        var previous = _history.GetPrints(id);
        var when = previous.Count > 0
            ? previous[0].PrintedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
            : "?";
        _history.Log(EventType.Blocked, $"Etiqueta {id} bloqueada: já impressa em {when}", id, job);
        return new LabelOutcome(job, index, label, id, LabelStatus.Duplicate, previous);
    }

    private async Task<LabelOutcome> SendAsync(
        string job, int index, Label label, string? id, ILabelSender sender, CancellationToken cancellationToken)
    {
        var error = await TrySendAsync(label.RawData, sender, cancellationToken);
        if (error is null)
        {
            if (id is not null)
                SaveImage(id, job, label);
            _history.Log(EventType.Printed, $"{Name(id, index)} impressa", id, job);
            return new LabelOutcome(job, index, label, id, LabelStatus.Printed, Array.Empty<PrintRecord>());
        }

        if (id is not null)
            _history.ReleasePrint(id);
        _history.Log(EventType.Error, $"Falha ao enviar {Name(id, index)}: {error}", id, job);
        return new LabelOutcome(job, index, label, id, LabelStatus.Failed, Array.Empty<PrintRecord>(), error);
    }

    // A etiqueta já saiu: um problema ao guardar a cópia não pode parar o resto do trabalho.
    private void SaveImage(string id, string job, Label label)
    {
        try
        {
            _history.SaveLabelImage(id, job, label.RawData);
        }
        catch (Exception ex)
        {
            _history.Log(EventType.Error, $"Etiqueta {id} impressa, mas não foi guardada para reimpressão: {ex.Message}", id, job);
        }
    }

    /// <summary>Retorna null se enviou, ou a mensagem de erro.</summary>
    private static async Task<string?> TrySendAsync(ReadOnlyMemory<byte> data, ILabelSender sender, CancellationToken cancellationToken)
    {
        try
        {
            await sender.SendAsync(data, cancellationToken);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ex.Message;
        }
    }

    private static string Name(string? id, int index) => id is null ? $"Etiqueta #{index}" : $"Etiqueta {id}";
}
