using System.Text.Encodings.Web;
using System.Text.Json;
using LabelGuard.Core.Guard;
using LabelGuard.Core.Parsing;

namespace LabelGuard.Core.Config;

/// <summary>Configuração do LabelGuard, gravada em JSON (padrão: C:\ProgramData\LabelGuard\config.json).</summary>
public sealed class LabelGuardConfig
{
    public const string FileName = "config.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Arquivo local editado à mão: grava "&", "<" e "+" sem escapar.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LabelGuard");

    public static string DefaultPath { get; } = Path.Combine(DefaultDirectory, FileName);

    /// <summary>Fila em que o Papersystem imprime (porta do LabelGuard); vigiada para não ficar "offline".</summary>
    public string InputPrinter { get; set; } = "LABEL";

    /// <summary>Fila do Windows ligada à impressora de verdade, que recebe as etiquetas liberadas em RAW.</summary>
    public string OutputPrinter { get; set; } = "LabelGuard - NAO USAR";

    /// <summary>Porta TCP local da fila protegida.</summary>
    public int Port { get; set; } = 9100;

    /// <summary>Expressão regular do ID, com grupo nomeado <c>id</c>; vazio = padrão do LabelGuard.</summary>
    public string? IdPattern { get; set; }

    /// <summary><see cref="IdPattern"/>, ou o padrão do LabelGuard se estiver vazio.</summary>
    public string EffectiveIdPattern => string.IsNullOrWhiteSpace(IdPattern) ? IdExtractor.DefaultPattern : IdPattern;

    public bool PrintWithoutId { get; set; }

    public bool AllowMultipleCopies { get; set; }

    /// <summary>Envia SET REPRINT OFF (TSPL) antes de cada etiqueta.</summary>
    public bool PrinterReprintOff { get; set; } = true;

    /// <summary>Arquivo do histórico; vazio = historico.db na pasta da configuração.</summary>
    public string? DatabasePath { get; set; }

    /// <summary>Por quantos dias guardar cada etiqueta impressa para "Reimprimir pallet" (mínimo 1).</summary>
    public int KeepLabelImagesDays { get; set; } = 30;

    /// <summary>Pasta onde gravar cada trabalho recebido, para diagnóstico; vazio = não grava.</summary>
    public string? SaveJobsTo { get; set; }

    /// <summary>Hash da senha do supervisor (<see cref="SupervisorPassword"/>); vazio = ainda não definida.</summary>
    public string? SupervisorPasswordHash { get; set; }

    /// <summary>Lê a configuração; se o arquivo não existir, retorna os valores padrão.</summary>
    public static LabelGuardConfig Load(string path)
    {
        if (!File.Exists(path))
            return new LabelGuardConfig();
        try
        {
            return JsonSerializer.Deserialize<LabelGuardConfig>(File.ReadAllText(path), Json) ?? new LabelGuardConfig();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Configuração inválida em {path}: {ex.Message}", ex);
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, path, overwrite: true);
    }

    public GuardOptions ToGuardOptions() => new(PrintWithoutId, AllowMultipleCopies);

    /// <summary>Caminho do histórico; relativo à pasta do arquivo de configuração.</summary>
    public string ResolveDatabasePath(string configPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        return string.IsNullOrWhiteSpace(DatabasePath)
            ? Path.Combine(directory, "historico.db")
            : Path.GetFullPath(DatabasePath, directory);
    }
}
