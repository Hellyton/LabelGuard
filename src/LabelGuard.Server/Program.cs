using System.Net;
using System.Net.Sockets;
using LabelGuard.Core.Guard;
using LabelGuard.Core.History;
using LabelGuard.Core.Parsing;
using LabelGuard.Core.Receiving;
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
    Console.WriteLine("                       [--imprimir-sem-id] [--permitir-copias] [--salvar <pasta>]");
    Console.WriteLine("  --impressora  fila do Windows ligada à impressora de verdade (ex.: \"LABEL (Saida USB)\")");
    Console.WriteLine("  --db          histórico, padrão: " + dbPath);
    Console.WriteLine("  --salvar      grava cada trabalho recebido nessa pasta (.prn), para diagnóstico");
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
if (saveDir is not null)
    Directory.CreateDirectory(saveDir);
using var history = new LabelHistory(dbPath);
var guard = new PrintGuard(history, new IdExtractor(pattern), options);
var sender = new RawPrinterSender(printer);
var consoleLock = new object();

var receiver = new TcpJobReceiver(new IPEndPoint(IPAddress.Loopback, port), HandleJobAsync, Log);
try
{
    receiver.Start();
}
catch (SocketException ex)
{
    Console.WriteLine($"Não foi possível abrir a porta {port}: {ex.Message}");
    return 1;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Log($"LabelGuard ouvindo em {receiver.LocalEndpoint}, enviando para '{printer}'");
Log($"Histórico: {Path.GetFullPath(dbPath)}");
if (saveDir is not null)
    Log($"Trabalhos recebidos gravados em: {Path.GetFullPath(saveDir)}");
Log("Ctrl+C para parar.");
await receiver.RunAsync(cts.Token);
Log("Parado.");
return 0;

async Task HandleJobAsync(ReceivedJob job, CancellationToken cancellationToken)
{
    if (saveDir is not null)
        await File.WriteAllBytesAsync(Path.Combine(saveDir, job.Name + ".prn"), job.Data, cancellationToken);
    var result = await guard.ProcessAsync(job.Data, job.Name, sender, cancellationToken);
    lock (consoleLock)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Trabalho {job.Name}: {job.Data.Length:N0} bytes, {result.Labels.Count} etiqueta(s)");
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
