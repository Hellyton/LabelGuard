using System.Net;
using System.Net.Sockets;
using LabelGuard.Core.Config;
using LabelGuard.Core.Guard;
using LabelGuard.Core.History;
using LabelGuard.Core.Hosting;
using LabelGuard.Core.Parsing;
using LabelGuard.Windows;

namespace LabelGuard.App;

internal static class Program
{
    /// <summary>Uso: LabelGuard.App.exe [--config &lt;arquivo&gt;]</summary>
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        using var instance = new Mutex(true, @"Local\LabelGuard.App", out var isFirst);
        if (!isFirst)
        {
            Dialogs.Info("O LabelGuard já está aberto. Procure o ícone perto do relógio.");
            return 0;
        }

        var configPath = args.Length == 2 && args[0] == "--config" ? args[1] : LabelGuardConfig.DefaultPath;
        var configDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        Directory.CreateDirectory(configDirectory);
        AppLog.Configure(configDirectory);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            AppLog.Write($"Erro inesperado na tela: {e.Exception}");
            Dialogs.Error($"Erro inesperado: {e.Exception.Message}\n\nO LabelGuard continua rodando. Detalhes em labelguard.log.");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Write($"Erro fatal: {e.ExceptionObject}");
        Microsoft.Win32.SystemEvents.SessionEnding += (_, e) => AppLog.Write($"Windows encerrando a sessão ({e.Reason})");
        AppLog.Write($"Iniciando (pid {Environment.ProcessId})");

        LabelGuardConfig config;
        try
        {
            config = LabelGuardConfig.Load(configPath);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Configuração inválida: {ex.Message}");
            Dialogs.Error(ex.Message);
            return 1;
        }

        if (config.SupervisorPasswordHash is null)
        {
            var password = NewPasswordForm.Ask(null, "Primeiro uso: defina a senha do supervisor.\nEla será pedida para liberar reimpressões e para fechar o LabelGuard.");
            if (password is null)
                return 1;
            config.SupervisorPasswordHash = SupervisorPassword.Hash(password);
            try
            {
                config.Save(configPath);
            }
            catch (Exception ex)
            {
                Dialogs.Error($"Não foi possível gravar a configuração em {configPath}: {ex.Message}");
                return 1;
            }
        }

        IdExtractor extractor;
        try
        {
            RawPrinterSender.EnsurePrinterExists(config.OutputPrinter);
            extractor = new IdExtractor(config.EffectiveIdPattern);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Não iniciou: {ex.Message}");
            Dialogs.Error($"{ex.Message}\n\nConfira a configuração em {configPath}.");
            return 1;
        }

        var databasePath = config.ResolveDatabasePath(configPath);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        using var history = new LabelHistory(databasePath);
        ILabelSender sender = new RawPrinterSender(config.OutputPrinter);
        if (config.PrinterReprintOff)
            sender = new CommandPrefixSender(sender, CommandPrefixSender.TsplReprintOff);
        var host = new GuardHost(
            new PrintGuard(history, extractor, config.ToGuardOptions()),
            sender,
            new IPEndPoint(IPAddress.Loopback, config.Port),
            config.SaveJobsTo);

        using var tray = new TrayContext(config, configPath, history, host);
        try
        {
            host.Start();
        }
        catch (SocketException ex)
        {
            AppLog.Write($"Não iniciou: porta {config.Port}: {ex.Message}");
            Dialogs.Error($"Não foi possível abrir a porta {config.Port}: {ex.Message}\n\nVerifique se o LabelGuard já está rodando em outra janela (console).");
            return 1;
        }

        AppLog.Write($"Protegendo: porta {config.Port} -> '{config.OutputPrinter}'");
        tray.ShowStarted();
        Application.Run(tray);
        AppLog.Write("Encerrado");

        // Fora da thread da tela, para não travar esperando por ela.
        Task.Run(host.StopAsync).GetAwaiter().GetResult();
        return 0;
    }
}
