using System.ComponentModel;
using System.Runtime.InteropServices;
using LabelGuard.Core.Guard;

namespace LabelGuard.Windows;

/// <summary>
/// Envia etiquetas para uma fila de impressão do Windows em modo RAW (bytes sem alteração pelo driver).
/// Cada etiqueta vira um documento separado no spooler: se o Windows repetir um documento depois de um
/// erro na impressora, no máximo uma etiqueta é afetada.
/// </summary>
public sealed class RawPrinterSender : ILabelSender
{
    private readonly string _printerName;
    private readonly string _documentName;

    public RawPrinterSender(string printerName, string documentName = "LabelGuard")
    {
        _printerName = printerName;
        _documentName = documentName;
    }

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
        Task.Run(() => Write(data.ToArray()), cancellationToken);

    /// <summary>Lança <see cref="Win32Exception"/> se a fila não existir ou recusar o documento.</summary>
    public static void EnsurePrinterExists(string printerName)
    {
        if (!NativeMethods.OpenPrinter(printerName, out var handle, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Impressora '{printerName}' não encontrada");
        NativeMethods.ClosePrinter(handle);
    }

    private void Write(byte[] data)
    {
        if (!NativeMethods.OpenPrinter(_printerName, out var printer, IntPtr.Zero))
            throw Error($"abrir a impressora '{_printerName}'");
        try
        {
            var doc = new NativeMethods.DocInfo1 { DocName = _documentName, OutputFile = null, Datatype = "RAW" };
            if (NativeMethods.StartDocPrinter(printer, 1, ref doc) == 0)
                throw Error("iniciar o documento");
            try
            {
                if (!NativeMethods.StartPagePrinter(printer))
                    throw Error("iniciar a página");

                var offset = 0;
                while (offset < data.Length)
                {
                    if (!NativeMethods.WritePrinter(printer, data[offset..], data.Length - offset, out var written) || written <= 0)
                        throw Error("enviar os dados");
                    offset += written;
                }

                NativeMethods.EndPagePrinter(printer);
            }
            finally
            {
                NativeMethods.EndDocPrinter(printer);
            }
        }
        finally
        {
            NativeMethods.ClosePrinter(printer);
        }
    }

    private static Win32Exception Error(string action)
    {
        var code = Marshal.GetLastWin32Error();
        return new Win32Exception(code, $"Falha ao {action}: {new Win32Exception(code).Message}");
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DocInfo1
        {
            public string DocName;
            public string? OutputFile;
            public string Datatype;
        }

        [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool OpenPrinter(string printerName, out IntPtr printer, IntPtr defaults);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool ClosePrinter(IntPtr printer);

        [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int StartDocPrinter(IntPtr printer, int level, ref DocInfo1 docInfo);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool EndDocPrinter(IntPtr printer);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool StartPagePrinter(IntPtr printer);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool EndPagePrinter(IntPtr printer);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool WritePrinter(IntPtr printer, byte[] data, int count, out int written);
    }
}
