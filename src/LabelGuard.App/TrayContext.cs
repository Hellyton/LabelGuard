using System.Media;
using LabelGuard.Core.Config;
using LabelGuard.Core.Guard;
using LabelGuard.Core.History;
using LabelGuard.Core.Hosting;
using LabelGuard.Windows;

namespace LabelGuard.App;

/// <summary>Ícone na bandeja: mostra o alerta de bloqueio, os eventos e protege a saída com a senha.</summary>
internal sealed class TrayContext : ApplicationContext
{
    /// <summary>Nome registrado no histórico para liberações feitas com a senha única.</summary>
    private const string Supervisor = "supervisor";

    private readonly LabelGuardConfig _config;
    private readonly string _configPath;
    private readonly LabelHistory _history;
    private readonly GuardHost _host;
    private readonly SynchronizationContext _ui;
    private readonly NotifyIcon _tray;
    private readonly AlertForm _alert;
    private readonly System.Windows.Forms.Timer _queueCheck = new() { Interval = 30_000 };
    private readonly System.Windows.Forms.Timer _cleanup = new() { Interval = 6 * 60 * 60 * 1000 };
    private EventsForm? _events;
    private int _printed;
    private int _blocked;
    private DateTime _lastOfflineWarning = DateTime.MinValue;
    private bool _queueCheckFailed;

    public TrayContext(LabelGuardConfig config, string configPath, LabelHistory history, GuardHost host)
    {
        _config = config;
        _configPath = configPath;
        _history = history;
        _host = host;
        _ui = new WindowsFormsSynchronizationContext();
        _alert = new AlertForm(CheckPassword, outcome => _host.AuthorizeAsync(outcome, Supervisor));

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"Protegendo: fila → '{config.OutputPrinter}'") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Etiquetas bloqueadas...", null, (_, _) => _alert.ShowAlert());
        menu.Items.Add("Reimprimir pallet...", null, async (_, _) => await ReprintAsync());
        menu.Items.Add("Eventos...", null, (_, _) => ShowEvents());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Alterar senha do supervisor...", null, (_, _) => ChangePassword());
        menu.Items.Add("Sair...", null, async (_, _) => await ExitAsync());

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowEvents();
        UpdateTooltip();

        _host.JobProcessed += result => _ui.Post(_ => OnJob(result), null);
        _host.Message += message =>
        {
            AppLog.Write(message);
            _ui.Post(_ => ShowBalloon(message, ToolTipIcon.Warning), null);
        };

        _queueCheck.Tick += (_, _) => CheckInputQueue();
        _cleanup.Tick += (_, _) => CleanupStoredLabels();
    }

    public void ShowStarted()
    {
        ShowBalloon($"Protegendo as etiquetas. Liberadas vão para '{_config.OutputPrinter}'.", ToolTipIcon.Info);
        CheckInputQueue();
        _queueCheck.Start();
        CleanupStoredLabels();
        _cleanup.Start();
    }

    /// <summary>
    /// Se o Windows marcou a fila do Papersystem como "offline" (acontece quando o LabelGuard esteve fechado),
    /// ela não entrega mais nada. Tenta desmarcar; sem permissão de administrador, avisa a cada 5 minutos.
    /// </summary>
    private void CheckInputQueue()
    {
        var queue = _config.InputPrinter;
        bool offline;
        try
        {
            offline = PrinterQueue.IsWorkOffline(queue);
        }
        catch (Exception ex)
        {
            if (!_queueCheckFailed)
                AppLog.Write($"Não foi possível verificar a fila '{queue}': {ex.Message}");
            _queueCheckFailed = true;
            return;
        }

        _queueCheckFailed = false;
        if (!offline)
            return;

        try
        {
            PrinterQueue.SetOnline(queue);
            AppLog.Write($"Fila '{queue}' estava offline; colocada online");
            ShowBalloon($"A fila '{queue}' estava offline e foi reativada.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            if (DateTime.Now - _lastOfflineWarning < TimeSpan.FromMinutes(5))
                return;
            _lastOfflineWarning = DateTime.Now;
            AppLog.Write($"Fila '{queue}' offline e sem permissão para reativar: {ex.Message}");
            SystemSounds.Hand.Play();
            ShowBalloon(
                $"A fila '{queue}' está OFFLINE e as etiquetas não saem.\nConfigurações > Impressoras > {queue} > Abrir fila > menu Impressora > desmarcar 'Usar Impressora Offline'.",
                ToolTipIcon.Error);
        }
    }

    private void OnJob(JobResult result)
    {
        _printed += result.Labels.Count(l => l.Status == LabelStatus.Printed);
        var blocked = result.Blocked.ToList();
        _blocked += blocked.Count;
        UpdateTooltip();
        _events?.Reload();

        if (blocked.Count > 0)
        {
            _alert.Add(blocked);
            _alert.ShowAlert();
            SystemSounds.Exclamation.Play();
        }

        var failed = result.Labels.FirstOrDefault(l => l.Status == LabelStatus.Failed);
        if (failed is not null)
        {
            var printed = result.Labels.Count(l => l.Status == LabelStatus.Printed);
            var missing = result.Labels.Count(l => l.Status is LabelStatus.Failed or LabelStatus.NotSent);
            AppLog.Write($"Falha ao enviar ({result.Job}): {failed.Error}; {missing} etiqueta(s) não saíram");
            SystemSounds.Hand.Play();
            Dialogs.Urgent(
                $"FALHA AO ENVIAR PARA A IMPRESSORA\n\n{failed.Error}\n\n" +
                $"Saíram {printed} etiqueta(s). NÃO saíram {missing}, a partir da #{failed.Index}" +
                (failed.Id is null ? "." : $" (pallet {failed.Id}).") +
                "\n\nConfira a impressora e mande o lote de novo pelo Papersystem: as que já saíram serão barradas e só as que faltam vão sair.");
        }
    }

    /// <summary>Reimprime pelo número do pallet a etiqueta guardada, sem passar pelo Papersystem.</summary>
    private async Task ReprintAsync()
    {
        var id = PasswordPrompt.AskText(null, "Reimprimir pallet", "Número do pallet:");
        if (id is null)
            return;

        var prints = _history.GetPrints(id);
        if (_history.GetLabelImage(id) is null)
        {
            Dialogs.Error(prints.Count == 0
                ? $"O pallet {id} ainda não foi impresso pelo LabelGuard."
                : $"A etiqueta do pallet {id} não está mais guardada (ficam {_config.KeepLabelImagesDays} dias).\n\nReimprima pelo Papersystem: ela será barrada e o supervisor libera no alerta.");
            return;
        }

        var first = prints.Count > 0 ? OutcomeText.FormatTime(prints[0].PrintedAt) : "?";
        var password = PasswordPrompt.Ask(
            null,
            "Reimprimir pallet",
            $"Pallet {id}\nPrimeira impressão: {first}\nVezes impresso: {prints.Count}\n\nSenha do supervisor:");
        if (password is null)
            return;
        if (!CheckPassword(password))
        {
            Dialogs.Error("Senha incorreta.");
            return;
        }

        var result = await _host.ReprintAsync(id, Supervisor);
        _events?.Reload();
        if (result.Status == ReprintStatus.Printed)
            ShowBalloon($"Pallet {id} reimpresso.", ToolTipIcon.Info);
        else
            Dialogs.Error($"Não foi possível reimprimir o pallet {id}: {result.Error ?? "etiqueta não encontrada"}");
    }

    private void CleanupStoredLabels()
    {
        var days = Math.Max(1, _config.KeepLabelImagesDays);
        try
        {
            var deleted = _history.DeleteLabelImagesOlderThan(TimeSpan.FromDays(days));
            if (deleted > 0)
                AppLog.Write($"{deleted} etiqueta(s) guardada(s) há mais de {days} dias apagada(s)");
        }
        catch (Exception ex)
        {
            AppLog.Write($"Falha ao apagar etiquetas guardadas antigas: {ex.Message}");
        }
    }

    private bool CheckPassword(string password) => SupervisorPassword.Verify(password, _config.SupervisorPasswordHash);

    private void ShowEvents()
    {
        if (_events is null || _events.IsDisposed)
            _events = new EventsForm(_history);
        _events.Reload();
        _events.Show();
        _events.Activate();
    }

    private void ChangePassword()
    {
        var current = PasswordPrompt.Ask(null, "Alterar senha", "Senha atual do supervisor:");
        if (current is null)
            return;
        if (!CheckPassword(current))
        {
            Dialogs.Error("Senha incorreta.");
            return;
        }

        var password = NewPasswordForm.Ask(null, "Nova senha do supervisor:");
        if (password is null)
            return;
        var hash = SupervisorPassword.Hash(password);
        try
        {
            // Relê o arquivo: mudanças feitas à mão com o LabelGuard aberto não podem ser perdidas.
            var onDisk = LabelGuardConfig.Load(_configPath);
            onDisk.SupervisorPasswordHash = hash;
            onDisk.Save(_configPath);
            _config.SupervisorPasswordHash = hash;
            AppLog.Write("Senha do supervisor alterada");
            Dialogs.Info("Senha alterada.");
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Não foi possível gravar a nova senha: {ex.Message}");
        }
    }

    private async Task ExitAsync()
    {
        var password = PasswordPrompt.Ask(
            null,
            "Fechar o LabelGuard",
            "Com o LabelGuard fechado, as etiquetas ficam paradas na fila e não imprimem.\n\nSenha do supervisor:");
        if (password is null)
            return;
        if (!CheckPassword(password))
        {
            Dialogs.Error("Senha incorreta.");
            return;
        }

        AppLog.Write("Fechado pelo menu Sair (senha do supervisor)");
        await _host.StopAsync();
        _tray.Visible = false;
        ExitThread();
    }

    private void UpdateTooltip() => _tray.Text = $"LabelGuard: {_printed} impressas, {_blocked} bloqueadas";

    private void ShowBalloon(string text, ToolTipIcon icon) => _tray.ShowBalloonTip(5000, "LabelGuard", text, icon);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _queueCheck.Dispose();
            _cleanup.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _alert.Dispose();
            _events?.Dispose();
        }
        base.Dispose(disposing);
    }
}
