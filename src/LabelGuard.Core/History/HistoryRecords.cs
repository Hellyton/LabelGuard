namespace LabelGuard.Core.History;

/// <summary>Uma impressão registrada. <paramref name="AuthorizedBy"/> é null na primeira impressão do ID.</summary>
public sealed record PrintRecord(string LabelId, DateTimeOffset PrintedAt, string? AuthorizedBy, string? Job);

/// <summary>Bytes de uma etiqueta já enviada, guardados para reimpressão.</summary>
public sealed record StoredLabel(string LabelId, DateTimeOffset SavedAt, string? Job, byte[] Data);

/// <summary>Tipo de evento do log.</summary>
public enum EventType
{
    /// <summary>Etiqueta nova enviada para a impressora.</summary>
    Printed,

    /// <summary>Etiqueta repetida barrada.</summary>
    Blocked,

    /// <summary>Reimpressão liberada com senha de supervisor.</summary>
    ReprintAuthorized,

    /// <summary>Etiqueta sem ID reconhecível.</summary>
    NoId,

    /// <summary>Falha ao processar ou enviar um trabalho.</summary>
    Error,
}

/// <summary>Um evento do log.</summary>
public sealed record EventRecord(
    long Id, DateTimeOffset At, EventType Type, string Message, string? LabelId, string? Job);
