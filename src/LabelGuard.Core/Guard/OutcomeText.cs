using System.Globalization;

namespace LabelGuard.Core.Guard;

/// <summary>Textos em português para mostrar o resultado de uma etiqueta ao operador.</summary>
public static class OutcomeText
{
    public static string Describe(this LabelOutcome outcome) => outcome.Status switch
    {
        LabelStatus.Printed => "IMPRESSA",
        LabelStatus.Duplicate when outcome.PreviousPrints.Count > 0 =>
            $"BLOQUEADA: já impressa em {FormatTime(outcome.PreviousPrints[0].PrintedAt)} ({outcome.PreviousPrints[0].Job})",
        LabelStatus.Duplicate => "BLOQUEADA: já impressa",
        LabelStatus.MultipleCopies => $"BLOQUEADA: pede {outcome.Label.Copies} cópias",
        LabelStatus.NoId => "BLOQUEADA: ID não encontrado",
        LabelStatus.Failed => $"FALHA: {outcome.Error}",
        _ => "não enviada",
    };

    public static string FormatTime(DateTimeOffset time) =>
        time.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
}
