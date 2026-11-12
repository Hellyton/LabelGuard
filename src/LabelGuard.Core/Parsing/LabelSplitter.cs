using System.Text;
using System.Text.RegularExpressions;

namespace LabelGuard.Core.Parsing;

/// <summary>
/// Separa um trabalho de impressão em etiquetas individuais.
/// A concatenação de <see cref="Label.RawData"/> de todas as etiquetas é sempre igual ao trabalho original:
/// comandos antes da primeira etiqueta ficam com ela, e o que sobra depois da última fica com a última.
/// </summary>
public static class LabelSplitter
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex ZplStart = new(@"\^XA", Options);
    private static readonly Regex ZplEnd = new(@"\^XZ", Options);
    private static readonly Regex ZplQuantity = new(@"\^PQ(?<q>\d+)", Options);

    // PRINT m[,n]: m conjuntos, n cópias de cada. A etiqueta termina no fim dessa linha.
    private static readonly Regex TsplPrint = new(
        @"^[ \t]*PRINT[ \t]+(?<sets>\d+)(?:[ \t]*,[ \t]*(?<copies>\d+))?[ \t]*(?:\r\n|\n|\r|\z)",
        Options | RegexOptions.Multiline);

    public static PrinterLanguage Detect(string text)
    {
        if (ZplStart.IsMatch(text))
            return PrinterLanguage.Zpl;
        if (TsplPrint.IsMatch(text))
            return PrinterLanguage.Tspl;
        return PrinterLanguage.Unknown;
    }

    public static IReadOnlyList<Label> Split(byte[] job)
    {
        if (job.Length == 0)
            return Array.Empty<Label>();

        // Latin-1 mapeia cada byte para um caractere, então índices do texto = índices dos bytes.
        var text = MaskBitmaps(Encoding.Latin1.GetString(job));

        return Detect(text) switch
        {
            PrinterLanguage.Zpl => Cut(job, text, ZplEnd.Matches(text), ZplCopies),
            PrinterLanguage.Tspl => Cut(job, text, TsplPrint.Matches(text), TsplCopies),
            _ => new[] { new Label(job, text, 1) },
        };
    }

    private static List<Label> Cut(byte[] job, string text, MatchCollection ends, Func<string, int> copies)
    {
        var ranges = new List<(int Start, int End)>();
        var start = 0;
        foreach (Match end in ends)
        {
            var stop = end.Index + end.Length;
            ranges.Add((start, stop));
            start = stop;
        }

        if (ranges.Count == 0)
            ranges.Add((0, text.Length));
        else if (start < text.Length)
            ranges[^1] = (ranges[^1].Start, text.Length);

        return ranges
            .Select(r =>
            {
                var segment = text[r.Start..r.End];
                return new Label(job[r.Start..r.End], segment, copies(segment));
            })
            .ToList();
    }

    // Os dados binários de um BITMAP podem conter "^XA" ou "PRINT" por acaso; troca por '\0' sem mudar o tamanho.
    private static string MaskBitmaps(string text)
    {
        char[]? chars = null;
        foreach (var bitmap in TsplBitmap.FindAll(text))
        {
            chars ??= text.ToCharArray();
            Array.Fill(chars, '\0', bitmap.DataStart, bitmap.DataLength);
        }
        return chars is null ? text : new string(chars);
    }

    private static int ZplCopies(string label)
    {
        var m = ZplQuantity.Match(label);
        return m.Success ? Positive(m.Groups["q"].Value) : 1;
    }

    private static int TsplCopies(string label)
    {
        var m = TsplPrint.Match(label);
        if (!m.Success)
            return 1;
        var copies = m.Groups["copies"].Success ? Positive(m.Groups["copies"].Value) : 1;
        return Positive(m.Groups["sets"].Value) * copies;
    }

    private static int Positive(string digits) =>
        int.TryParse(digits, out var n) && n > 0 ? n : 1;
}
