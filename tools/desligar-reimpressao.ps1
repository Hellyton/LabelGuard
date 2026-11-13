# Desliga a reimpressão automática da impressora de etiquetas (TSPL), que repete sozinha
# a etiqueta em andamento depois de um erro (fim de bobina, tampa aberta).
# Não precisa do LabelGuard: envia só o comando, direto para a fila da impressora.
#
# Uso:  clique com o botão direito > "Executar com o PowerShell" (usa a fila "LABEL"), ou
#       powershell -ExecutionPolicy Bypass -File desligar-reimpressao.ps1 -Impressora "LABEL"
#       powershell -ExecutionPolicy Bypass -File desligar-reimpressao.ps1 -Impressora "LABEL" -Ligar   (desfaz)
# -SemPausa fecha sem esperar Enter (para rodar automaticamente).
param(
    [string]$Impressora = "LABEL",
    [switch]$Ligar,
    [switch]$SemPausa
)

Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class LabelGuardRawPrint
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DocInfo1 { public string DocName; public string OutputFile; public string Datatype; }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool OpenPrinter(string name, out IntPtr printer, IntPtr defaults);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool ClosePrinter(IntPtr printer);
    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern int StartDocPrinter(IntPtr printer, int level, ref DocInfo1 docInfo);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool EndDocPrinter(IntPtr printer);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool StartPagePrinter(IntPtr printer);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool EndPagePrinter(IntPtr printer);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool WritePrinter(IntPtr printer, byte[] data, int count, out int written);

    public static void Send(string printerName, byte[] data)
    {
        IntPtr printer;
        if (!OpenPrinter(printerName, out printer, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Impressora nao encontrada: " + printerName);
        try
        {
            var doc = new DocInfo1 { DocName = "LabelGuard - configuracao", OutputFile = null, Datatype = "RAW" };
            if (StartDocPrinter(printer, 1, ref doc) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                StartPagePrinter(printer);
                int written;
                if (!WritePrinter(printer, data, data.Length, out written) || written != data.Length)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                EndPagePrinter(printer);
            }
            finally
            {
                EndDocPrinter(printer);
            }
        }
        finally
        {
            ClosePrinter(printer);
        }
    }
}
"@

$valor = if ($Ligar) { 'ON' } else { 'OFF' }
$codigo = 0
try {
    [LabelGuardRawPrint]::Send($Impressora, [Text.Encoding]::ASCII.GetBytes("SET REPRINT $valor`r`n"))
    Write-Host "OK: enviado para '$Impressora': SET REPRINT $valor" -ForegroundColor Green
    if (-not $Ligar) {
        Write-Host "A impressora não vai mais repetir sozinha a etiqueta depois de fim de bobina ou tampa aberta."
    }
}
catch {
    $codigo = 1
    Write-Host "ERRO: $($_.Exception.InnerException.Message)" -ForegroundColor Red
    Write-Host "Impressoras instaladas:"
    Get-Printer | ForEach-Object { Write-Host "  $($_.Name)" }
    Write-Host 'Rode de novo com -Impressora "<nome da fila>".'
}

if (-not $SemPausa) {
    Read-Host "Pressione Enter para fechar" | Out-Null
}
exit $codigo
