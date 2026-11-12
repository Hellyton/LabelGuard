namespace LabelGuard.Core.Parsing;

/// <summary>Uma etiqueta extraída de um trabalho de impressão.</summary>
/// <param name="RawData">Bytes originais da etiqueta, para reenviar à impressora sem alteração.</param>
/// <param name="Text">
/// Os mesmos bytes decodificados em Latin-1 (1 byte = 1 caractere), com os dados binários de BITMAP trocados por '\0'.
/// Os índices continuam iguais aos de <paramref name="RawData"/>.
/// </param>
/// <param name="Copies">Quantidade de cópias pedida pelo próprio comando (^PQ no ZPL, PRINT m,n no TSPL).</param>
public sealed record Label(byte[] RawData, string Text, int Copies);
