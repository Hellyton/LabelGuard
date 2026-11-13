using LabelGuard.Core.History;
using LabelGuard.Core.Parsing;

namespace LabelGuard.Core.Guard;

/// <summary>Regras configuráveis do motor de decisão.</summary>
/// <param name="PrintWithoutId">Imprime etiquetas sem ID legível em vez de bloquear.</param>
/// <param name="AllowMultipleCopies">Aceita etiquetas que pedem mais de uma cópia (^PQ, PRINT m,n).</param>
public sealed record GuardOptions(bool PrintWithoutId = false, bool AllowMultipleCopies = false);

public enum LabelStatus
{
    /// <summary>Enviada para a impressora.</summary>
    Printed,

    /// <summary>Bloqueada: o ID já foi impresso (antes ou no mesmo trabalho).</summary>
    Duplicate,

    /// <summary>Bloqueada: a etiqueta pede mais de uma cópia.</summary>
    MultipleCopies,

    /// <summary>Bloqueada: nenhum ID encontrado.</summary>
    NoId,

    /// <summary>O envio para a impressora falhou; o ID não ficou registrado.</summary>
    Failed,

    /// <summary>Não processada porque uma etiqueta anterior do trabalho falhou no envio.</summary>
    NotSent,
}

/// <summary>O que aconteceu com uma etiqueta de um trabalho.</summary>
/// <param name="Index">Posição da etiqueta no trabalho, a partir de 1.</param>
/// <param name="PreviousPrints">Impressões anteriores do ID, para o alerta mostrar quando já saiu.</param>
public sealed record LabelOutcome(
    string Job,
    int Index,
    Label Label,
    string? Id,
    LabelStatus Status,
    IReadOnlyList<PrintRecord> PreviousPrints,
    string? Error = null)
{
    /// <summary>Bloqueada e esperando liberação do supervisor.</summary>
    public bool IsBlocked => Status is LabelStatus.Duplicate or LabelStatus.MultipleCopies or LabelStatus.NoId;
}

public enum ReprintStatus
{
    /// <summary>Etiqueta guardada enviada de novo.</summary>
    Printed,

    /// <summary>Nenhuma etiqueta guardada para esse ID (nunca impressa ou apagada pela retenção).</summary>
    NotFound,

    /// <summary>O envio para a impressora falhou; nada foi registrado.</summary>
    Failed,
}

/// <summary>Resultado de <see cref="PrintGuard.ReprintAsync"/>.</summary>
public sealed record ReprintResult(ReprintStatus Status, StoredLabel? Label, string? Error);

public sealed record JobResult(string Job, IReadOnlyList<LabelOutcome> Labels)
{
    public IEnumerable<LabelOutcome> Blocked => Labels.Where(l => l.IsBlocked);

    public bool HasBlocked => Labels.Any(l => l.IsBlocked);

    public bool HasFailure => Labels.Any(l => l.Status == LabelStatus.Failed);
}
