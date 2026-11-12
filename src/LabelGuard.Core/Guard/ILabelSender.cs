namespace LabelGuard.Core.Guard;

/// <summary>Envia os bytes de uma etiqueta para a impressora real (rede ou USB).</summary>
public interface ILabelSender
{
    /// <summary>Deve lançar exceção se a etiqueta não puder ser entregue à impressora.</summary>
    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}
