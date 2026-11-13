using System.Media;
using LabelGuard.Core.Config;
using LabelGuard.Core.Guard;
using LabelGuard.Core.History;
using LabelGuard.Core.Hosting;

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
    private EventsForm? _events;
    private int _printed;
    private int _blocked;

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
    }

    public void ShowStarted() =>
        ShowBalloon($"Protegendo as etiquetas. Liberadas vão para '{_config.OutputPrinter}'.", ToolTipIcon.Info);

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
            AppLog.Write($"Falha ao enviar ({result.Job}): {failed.Error}");
            ShowBalloon(
                $"Falha ao enviar para a impressora: {failed.Error}\nAs etiquetas que não saíram não foram registradas; imprima de novo.",
                ToolTipIcon.Error);
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
        var previous = _config.SupervisorPasswordHash;
        _config.SupervisorPasswordHash = SupervisorPassword.Hash(password);
        try
        {
            _config.Save(_configPath);
            Dialogs.Info("Senha alterada.");
        }
        catch (Exception ex)
        {
            _config.SupervisorPasswordHash = previous;
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
            _tray.Visible = false;
            _tray.Dispose();
            _alert.Dispose();
            _events?.Dispose();
        }
        base.Dispose(disposing);
    }
}
