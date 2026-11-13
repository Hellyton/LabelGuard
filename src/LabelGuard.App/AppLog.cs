namespace LabelGuard.App;

/// <summary>
/// Log em texto ao lado da configuração (labelguard.log): início, encerramento e erros inesperados,
/// para entender depois por que o LabelGuard parou. As decisões sobre etiquetas ficam no histórico.
/// </summary>
internal static class AppLog
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Lock = new();
    private static string? _path;

    public static void Configure(string directory) => _path = Path.Combine(directory, "labelguard.log");

    public static void Write(string message)
    {
        if (_path is null)
            return;
        try
        {
            lock (Lock)
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
                    File.Move(_path, _path + ".old", overwrite: true);
                File.AppendAllText(_path, $"{DateTime.Now:dd/MM/yyyy HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // O log nunca pode derrubar o LabelGuard.
        }
    }
}
