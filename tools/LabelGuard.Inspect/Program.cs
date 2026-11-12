using System.Diagnostics;
using System.Globalization;
using LabelGuard.Core.Guard;
using LabelGuard.Core.History;
using LabelGuard.Core.Parsing;

// Analisa trabalhos de impressão capturados em arquivo (.prn) e mostra o que o LabelGuard enxerga.
// Com --db, passa os trabalhos pelo motor de decisão usando um histórico salvo em arquivo.
// Uso: LabelGuard.Inspect <arquivo.prn>... [--pattern <regex>] [--codes] [--db <historico.db> [--saida <liberadas.prn>]]

var files = new List<string>();
var pattern = IdExtractor.DefaultPattern;
var showCodes = false;
string? dbPath = null;
string? outputPath = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--pattern" when i + 1 < args.Length:
            pattern = args[++i];
            break;
        case "--codes":
            showCodes = true;
            break;
        case "--db" when i + 1 < args.Length:
            dbPath = args[++i];
            break;
        case "--saida" when i + 1 < args.Length:
            outputPath = args[++i];
            break;
        default:
            files.Add(args[i]);
            break;
    }
}

if (files.Count == 0)
{
    Console.WriteLine("Uso: LabelGuard.Inspect <arquivo.prn>... [--pattern <regex>] [--codes] [--db <historico.db> [--saida <liberadas.prn>]]");
    Console.WriteLine("  --pattern  expressão regular do ID com grupo (?<id>...), padrão: " + IdExtractor.DefaultPattern);
    Console.WriteLine("  --codes    mostra o conteúdo dos QR codes/códigos de barras lidos");
    Console.WriteLine("  --db       simula o LabelGuard: decide imprimir/bloquear e grava o histórico nesse arquivo");
    Console.WriteLine("  --saida    com --db, grava as etiquetas liberadas nesse arquivo (o que iria para a impressora)");
    return 1;
}

var extractor = new IdExtractor(pattern);
var existing = files.Where(File.Exists).ToList();
foreach (var missing in files.Except(existing))
    Console.WriteLine($"{missing}: arquivo não encontrado");

if (dbPath is not null)
    return await SimulateAsync(dbPath, outputPath);

var seen = new Dictionary<string, string>(); // ID -> onde apareceu primeiro

foreach (var file in existing)
{
    var job = File.ReadAllBytes(file);
    var sw = Stopwatch.StartNew();
    var labels = LabelSplitter.Split(job);
    Console.WriteLine($"{file}: {job.Length:N0} bytes, {labels.Count} etiqueta(s)");

    for (var n = 0; n < labels.Count; n++)
    {
        var label = labels[n];
        var where = $"{Path.GetFileName(file)} #{n + 1}";
        var id = extractor.Extract(label);
        var status = id is null ? "SEM ID"
            : seen.TryGetValue(id, out var first) ? $"REPETIDA (igual a {first})"
            : "nova";
        if (id is not null)
            seen.TryAdd(id, where);

        var copies = label.Copies > 1 ? $"  cópias={label.Copies}" : "";
        Console.WriteLine($"  #{n + 1,-4} {LabelSplitter.Detect(label.Text),-7} id={id ?? "-",-10} {status}{copies}");
        if (showCodes)
            foreach (var code in BarcodeScanner.Read(label))
                Console.WriteLine($"        código: {code}");
    }

    Console.WriteLine($"  tempo: {sw.ElapsedMilliseconds} ms");
}

return 0;

async Task<int> SimulateAsync(string db, string? output)
{
    using var history = new LabelHistory(db);
    var guard = new PrintGuard(history, extractor);
    var sender = new FileSender(output);
    Console.WriteLine($"Histórico: {Path.GetFullPath(db)}");

    foreach (var file in existing)
    {
        var result = await guard.ProcessAsync(File.ReadAllBytes(file), Path.GetFileName(file), sender);
        Console.WriteLine($"{file}: {result.Labels.Count} etiqueta(s)");
        foreach (var l in result.Labels)
            Console.WriteLine($"  #{l.Index,-4} id={l.Id ?? "-",-10} {Describe(l)}");

        var printed = result.Labels.Count(l => l.Status == LabelStatus.Printed);
        var blocked = result.Blocked.Count();
        Console.WriteLine($"  impressas: {printed}  bloqueadas: {blocked}");
    }

    if (output is not null)
        Console.WriteLine($"Etiquetas liberadas gravadas em: {Path.GetFullPath(output)}");
    return 0;
}

static string Describe(LabelOutcome l) => l.Status switch
{
    LabelStatus.Printed => "IMPRESSA",
    LabelStatus.Duplicate when l.PreviousPrints.Count > 0 =>
        $"BLOQUEADA: já impressa em {l.PreviousPrints[0].PrintedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)} ({l.PreviousPrints[0].Job})",
    LabelStatus.Duplicate => "BLOQUEADA: já impressa",
    LabelStatus.MultipleCopies => $"BLOQUEADA: pede {l.Label.Copies} cópias",
    LabelStatus.NoId => "BLOQUEADA: ID não encontrado",
    LabelStatus.Failed => $"FALHA: {l.Error}",
    _ => "não enviada",
};

/// <summary>Em vez da impressora, grava as etiquetas liberadas em arquivo (ou descarta, sem --saida).</summary>
sealed class FileSender(string? path) : ILabelSender
{
    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (path is null)
            return;
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write);
        await stream.WriteAsync(data, cancellationToken);
    }
}
