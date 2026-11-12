namespace LabelGuard.Core.Parsing;

/// <summary>Linguagem de comandos detectada em um trabalho de impressão.</summary>
public enum PrinterLanguage
{
    /// <summary>Não reconhecida: o trabalho inteiro é tratado como uma etiqueta.</summary>
    Unknown,

    /// <summary>Zebra ZPL (^XA ... ^XZ).</summary>
    Zpl,

    /// <summary>TSC TSPL (SIZE, GAP, CLS, TEXT, BARCODE, PRINT), comum em impressoras genéricas.</summary>
    Tspl,
}
