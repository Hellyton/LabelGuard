using System.Text.RegularExpressions;

namespace LabelGuard.Core.Parsing;

/// <summary>Encontra o número identificador de uma etiqueta usando uma expressão regular configurável.</summary>
public sealed class IdExtractor
{
    /// <summary>
    /// Padrão inicial: número do pallet no QR code do Papersystem,
    /// ex.: https://app.papersystem.com.br/d/etiqueta?pallet=3931&amp;...
    /// </summary>
    public const string DefaultPattern = @"[?&]pallet=(?<id>\d+)";

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

    /// <summary>Procura o ID primeiro nos códigos de barras/QR da etiqueta e depois nos comandos de texto.</summary>
    public string? Extract(Label label) =>
        BarcodeScanner.Read(label).Select(Extract).FirstOrDefault(id => id is not null)
        ?? Extract(label.Text);

    /// <summary>Retorna o primeiro ID encontrado, ou null se o texto não tiver ID.</summary>
    public string? Extract(string text)
    {
        var m = _regex.Match(text);
        return m.Success ? m.Groups[IdGroup].Value : null;
    }
}
