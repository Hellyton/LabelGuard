using System.Text.RegularExpressions;

namespace LabelGuard.Core.Parsing;

/// <summary>Encontra o número identificador no texto de uma etiqueta usando uma expressão regular configurável.</summary>
public sealed class IdExtractor
{
    /// <summary>Padrão inicial: 7 dígitos isolados (ex.: 1196924). Ajustar com uma amostra real do Papersystem.</summary>
    public const string DefaultPattern = @"(?<!\d)(?<id>\d{7})(?!\d)";

    private const string IdGroup = "id";

    private readonly Regex _regex;

    /// <param name="pattern">Expressão regular com um grupo nomeado <c>id</c>, ex.: <c>(?&lt;id&gt;\d+)</c>.</param>
    public IdExtractor(string pattern = DefaultPattern)
    {
        _regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (Array.IndexOf(_regex.GetGroupNames(), IdGroup) < 0)
            throw new ArgumentException(
                $"O padrão precisa ter um grupo nomeado '{IdGroup}', ex.: (?<id>\\d+)", nameof(pattern));
    }

    public string Pattern => _regex.ToString();

    /// <summary>Retorna o primeiro ID encontrado, ou null se a etiqueta não tiver ID.</summary>
    public string? Extract(string labelText)
    {
        var m = _regex.Match(labelText);
        return m.Success ? m.Groups[IdGroup].Value : null;
    }
}
