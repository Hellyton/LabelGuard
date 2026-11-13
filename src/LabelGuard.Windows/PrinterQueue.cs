using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LabelGuard.Windows;

/// <summary>
/// Estado "Usar impressora offline" de uma fila do Windows. Quando o Windows não consegue entregar
/// um trabalho (ex.: LabelGuard fechado), ele pode marcar a fila como offline, e ela para de entregar
/// mesmo depois que o LabelGuard volta.
/// </summary>
public static class PrinterQueue
{
    private const int WorkOffline = 0x400; // PRINTER_ATTRIBUTE_WORK_OFFLINE
    private const int PrinterAccessAdminister = 0x4;
    private const int Level5 = 5;

    // PRINTER_INFO_5: pPrinterName, pPortName, Attributes, DeviceNotSelectedTimeout, TransmissionRetryTimeout
    private static readonly int AttributesOffset = 2 * IntPtr.Size;

    public static bool IsWorkOffline(string printerName)
    {
        var handle = Open(printerName, 0);
        try
        {
            var info = ReadInfo5(handle);
            try
            {
                return (Marshal.ReadInt32(info, AttributesOffset) & WorkOffline) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(info);
            }
        }
        finally
        {
            NativeMethods.ClosePrinter(handle);
        }
    }

    /// <summary>Desmarca "Usar impressora offline". Lança <see cref="Win32Exception"/> se o usuário não tiver permissão.</summary>
    public static void SetOnline(string printerName)
    {
        var handle = Open(printerName, PrinterAccessAdminister);
        try
        {
            var info = ReadInfo5(handle);
            try
            {
                var attributes = Marshal.ReadInt32(info, AttributesOffset);
                if ((attributes & WorkOffline) == 0)
                    return;
                Marshal.WriteInt32(info, AttributesOffset, attributes & ~WorkOffline);
                if (!NativeMethods.SetPrinter(handle, Level5, info, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Não foi possível colocar '{printerName}' online");
            }
            finally
            {
                Marshal.FreeHGlobal(info);
            }
        }
        finally
        {
            NativeMethods.ClosePrinter(handle);
        }
    }

    private static IntPtr Open(string printerName, int access)
    {
        var defaults = new NativeMethods.PrinterDefaults { DesiredAccess = access };
        if (!NativeMethods.OpenPrinter(printerName, out var handle, ref defaults))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Impressora '{printerName}' não encontrada ou sem permissão");
        return handle;
    }

    /// <summary>Lê PRINTER_INFO_5 num buffer que quem chama deve liberar com <see cref="Marshal.FreeHGlobal"/>.</summary>
    private static IntPtr ReadInfo5(IntPtr handle)
    {
        NativeMethods.GetPrinter(handle, Level5, IntPtr.Zero, 0, out var needed);
        var buffer = Marshal.AllocHGlobal(needed);
        if (NativeMethods.GetPrinter(handle, Level5, buffer, needed, out _))
            return buffer;
        var error = Marshal.GetLastWin32Error();
        Marshal.FreeHGlobal(buffer);
        throw new Win32Exception(error);
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PrinterDefaults
        {
            public string? Datatype;
            public IntPtr DevMode;
            public int DesiredAccess;
        }

        [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool OpenPrinter(string printerName, out IntPtr printer, ref PrinterDefaults defaults);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool ClosePrinter(IntPtr printer);

        [DllImport("winspool.drv", EntryPoint = "GetPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool GetPrinter(IntPtr printer, int level, IntPtr buffer, int size, out int needed);

        [DllImport("winspool.drv", EntryPoint = "SetPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetPrinter(IntPtr printer, int level, IntPtr buffer, int command);
    }
}
