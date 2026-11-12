using System.Diagnostics;
using LabelGuard.Core.Parsing;

// Analisa trabalhos de impressão capturados em arquivo (.prn) e mostra o que o LabelGuard enxerga.
// Uso: LabelGuard.Inspect <arquivo.prn>... [--pattern <regex>] [--codes]

var files = new List<string>();
var pattern = IdExtractor.DefaultPattern;
var showCodes = false;
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
        default:
            files.Add(args[i]);
            break;
    }
}

if (files.Count == 0)
{
    Console.WriteLine("Uso: LabelGuard.Inspect <arquivo.prn>... [--pattern <regex>] [--codes]");
    Console.WriteLine("  --pattern  expressão regular do ID com grupo (?<id>...), padrão: " + IdExtractor.DefaultPattern);
    Console.WriteLine("  --codes    mostra o conteúdo dos QR codes/códigos de barras lidos");
    return 1;
}

var extractor = new IdExtractor(pattern);
var seen = new Dictionary<string, string>(); // ID -> onde apareceu primeiro

foreach (var file in files)
{
    if (!File.Exists(file))
    {
        Console.WriteLine($"{file}: arquivo não encontrado");
        continue;
    }

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
