using System.Net;
using System.Net.Sockets;
using LabelGuard.Core.Guard;
using LabelGuard.Core.History;
using LabelGuard.Core.Hosting;
using LabelGuard.Core.Parsing;
using LabelGuard.Windows;

// Recebe os trabalhos da fila protegida (porta TCP local), bloqueia etiquetas repetidas
// e envia as liberadas para a impressora real em modo RAW. Ctrl+C para parar.

string? printer = null;
var port = 9100;
var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LabelGuard", "historico.db");
var pattern = IdExtractor.DefaultPattern;
var options = new GuardOptions();
string? saveDir = null;
var reprintOff = true;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--impressora" when i + 1 < args.Length:
            printer = args[++i];
            break;
        case "--porta" when i + 1 < args.Length && int.TryParse(args[i + 1], out var p):
            port = p;
            i++;
            break;
        case "--db" when i + 1 < args.Length:
            dbPath = args[++i];
            break;
        case "--pattern" when i + 1 < args.Length:
            pattern = args[++i];
            break;
        case "--salvar" when i + 1 < args.Length:
            saveDir = args[++i];
            break;
        case "--manter-reimpressao":
            reprintOff = false;
            break;
        case "--imprimir-sem-id":
            options = options with { PrintWithoutId = true };
            break;
        case "--permitir-copias":
            options = options with { AllowMultipleCopies = true };
            break;
        default:
            Console.WriteLine($"Opção desconhecida: {args[i]}");
            printer = null;
            i = args.Length;
            break;
    }
}

if (printer is null)
{
    Console.WriteLine("Uso: LabelGuard.Server --impressora <fila real> [--porta 9100] [--db <arquivo>] [--pattern <regex>]");
    Console.WriteLine("                       [--imprimir-sem-id] [--permitir-copias] [--salvar <pasta>] [--manter-reimpressao]");
    Console.WriteLine("  --impressora  fila do Windows ligada à impressora de verdade (ex.: \"LabelGuard - NAO USAR\")");
    Console.WriteLine("  --db          histórico, padrão: " + dbPath);
    Console.WriteLine("  --salvar      grava cada trabalho recebido nessa pasta (.prn), para diagnóstico");
    Console.WriteLine("  --manter-reimpressao  não envia SET REPRINT OFF (TSPL) antes de cada etiqueta");
    return 1;
}

try
{
    RawPrinterSender.EnsurePrinterExists(printer);
}
catch (Exception ex)
{
    Console.WriteLine(ex.Message);
    return 1;
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
using var history = new LabelHistory(dbPath);
var guard = new PrintGuard(history, new IdExtractor(pattern), options);
ILabelSender sender = new RawPrinterSender(printer);
if (reprintOff)
    sender = new CommandPrefixSender(sender, CommandPrefixSender.TsplReprintOff);
var consoleLock = new object();

await using var host = new GuardHost(guard, sender, new IPEndPoint(IPAddress.Loopback, port), saveDir);
host.JobProcessed += ShowResult;
host.Message += Log;
try
{
    host.Start();
}
catch (SocketException ex)
{
    Console.WriteLine($"Não foi possível abrir a porta {port}: {ex.Message}");
    return 1;
}

var stop = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.TrySetResult();
};

Log($"LabelGuard ouvindo em {host.LocalEndpoint}, enviando para '{printer}'");
Log($"Histórico: {Path.GetFullPath(dbPath)}");
if (saveDir is not null)
    Log($"Trabalhos recebidos gravados em: {Path.GetFullPath(saveDir)}");
if (reprintOff)
    Log("Reimpressão automática da impressora desligada (SET REPRINT OFF antes de cada etiqueta)");
Log("Ctrl+C para parar.");
await stop.Task;
await host.StopAsync();
Log("Parado.");
return 0;

void ShowResult(JobResult result)
{
    lock (consoleLock)
    {
        var bytes = result.Labels.Sum(l => (long)l.Label.RawData.Length);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Trabalho {result.Job}: {bytes:N0} bytes, {result.Labels.Count} etiqueta(s)");
        foreach (var label in result.Labels)
        {
            Console.ForegroundColor = label.Status == LabelStatus.Printed ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine($"  #{label.Index,-4} id={label.Id ?? "-",-10} {label.Describe()}");
            Console.ResetColor();
        }

        var blocked = result.Blocked.Count();
        if (blocked > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  >>> {blocked} etiqueta(s) bloqueada(s). Não cole etiquetas repetidas.");
            Console.ResetColor();
            Console.Beep();
        }
    }
}

void Log(string message)
{
    lock (consoleLock)
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
}
