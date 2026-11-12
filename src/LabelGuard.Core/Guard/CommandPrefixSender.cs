using System.Text;

namespace LabelGuard.Core.Guard;

/// <summary>
/// Envia comandos antes de cada etiqueta. Usado para desligar a reimpressão automática da impressora,
/// que repete sozinha a etiqueta em andamento depois de um erro (fim de bobina) sem passar pelo Windows.
/// </summary>
public sealed class CommandPrefixSender : ILabelSender
{
    /// <summary>TSPL: não reimprimir a etiqueta depois de "sem papel", "sem ribbon" ou "tampa aberta".</summary>
    public static readonly byte[] TsplReprintOff = Encoding.ASCII.GetBytes("SET REPRINT OFF\r\n");

    private readonly ILabelSender _inner;
    private readonly byte[] _prefix;

    public CommandPrefixSender(ILabelSender inner, byte[] prefix)
    {
        _inner = inner;
        _prefix = prefix;
    }

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var combined = new byte[_prefix.Length + data.Length];
        _prefix.CopyTo(combined, 0);
        data.Span.CopyTo(combined.AsSpan(_prefix.Length));
        return _inner.SendAsync(combined, cancellationToken);
    }
}
