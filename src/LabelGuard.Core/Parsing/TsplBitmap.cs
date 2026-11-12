using System.Text.RegularExpressions;

namespace LabelGuard.Core.Parsing;

/// <summary>
/// Comando TSPL <c>BITMAP x,y,largura,altura,modo,dados</c>: largura em bytes, seguido de largura × altura bytes
/// binários (1 bit por ponto, bit 1 = branco, bit 0 = preto). É assim que o driver Windows envia a etiqueta inteira.
/// </summary>
public readonly record struct TsplBitmap(int DataStart, int WidthBytes, int Height)
{
    private static readonly Regex Header = new(
        @"BITMAP[ \t]+-?\d+[ \t]*,[ \t]*-?\d+[ \t]*,[ \t]*(?<w>\d+)[ \t]*,[ \t]*(?<h>\d+)[ \t]*,[ \t]*\d+[ \t]*,",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public int DataLength => WidthBytes * Height;

    public int WidthDots => WidthBytes * 8;

    /// <summary>Localiza os comandos BITMAP completos em um texto Latin-1, pulando os dados binários de cada um.</summary>
    public static IEnumerable<TsplBitmap> FindAll(string text)
    {
        var position = 0;
        while (position < text.Length)
        {
            var m = Header.Match(text, position);
            if (!m.Success)
                yield break;

            var bitmap = new TsplBitmap(
                m.Index + m.Length, int.Parse(m.Groups["w"].Value), int.Parse(m.Groups["h"].Value));
            if (bitmap.DataLength <= 0 || bitmap.DataStart + bitmap.DataLength > text.Length)
                yield break;

            yield return bitmap;
            position = bitmap.DataStart + bitmap.DataLength;
        }
    }

    /// <summary>Converte os dados em tons de cinza (1 byte por ponto: 0 = preto, 255 = branco).</summary>
    public byte[] ToGray8(ReadOnlySpan<byte> source)
    {
        var data = source.Slice(DataStart, DataLength);
        var width = WidthDots;
        var gray = new byte[width * Height];
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < width; x++)
                gray[y * width + x] = ((data[y * WidthBytes + x / 8] >> (7 - x % 8)) & 1) == 1 ? (byte)255 : (byte)0;
        return gray;
    }
}
