using LabelGuard.Core.Config;
using LabelGuard.Core.Guard;
using LabelGuard.Core.History;

namespace LabelGuard.App;

internal static class Dialogs
{
    public static void Info(string text, IWin32Window? owner = null) =>
        MessageBox.Show(owner, text, "LabelGuard", MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static void Error(string text, IWin32Window? owner = null) =>
        MessageBox.Show(owner, text, "LabelGuard", MessageBoxButtons.OK, MessageBoxIcon.Error);

    /// <summary>Erro por cima de todas as janelas, para não passar despercebido.</summary>
    public static void Urgent(string text) =>
        MessageBox.Show(text, "LabelGuard", MessageBoxButtons.OK, MessageBoxIcon.Error,
            MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);

    public static bool Confirm(string text, IWin32Window? owner = null) =>
        MessageBox.Show(owner, text, "LabelGuard", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
}

/// <summary>Pede uma senha (ou um texto, com <see cref="AskText"/>).</summary>
internal sealed class PasswordPrompt : Form
{
    private readonly TextBox _password = new() { UseSystemPasswordChar = true, Width = 300 };

    private PasswordPrompt(string title, string message, bool hidden = true)
    {
        _password.UseSystemPasswordChar = hidden;
        Text = title;
        Icon = SystemIcons.Shield;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel };
        AcceptButton = ok;
        CancelButton = cancel;

        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(12) };
        layout.Controls.Add(new Label { Text = message, AutoSize = true, MaximumSize = new Size(320, 0) });
        layout.Controls.Add(_password);
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
    }

    /// <summary>Retorna a senha digitada, ou null se cancelou.</summary>
    public static string? Ask(IWin32Window? owner, string title, string message)
    {
        using var form = new PasswordPrompt(title, message);
        return form.ShowDialog(owner) == DialogResult.OK ? form._password.Text : null;
    }

    /// <summary>Retorna o texto digitado (sem espaços nas pontas), ou null se cancelou ou deixou vazio.</summary>
    public static string? AskText(IWin32Window? owner, string title, string message)
    {
        using var form = new PasswordPrompt(title, message, hidden: false);
        var text = form.ShowDialog(owner) == DialogResult.OK ? form._password.Text.Trim() : "";
        return text.Length > 0 ? text : null;
    }
}

/// <summary>Define uma senha nova (digitada duas vezes).</summary>
internal sealed class NewPasswordForm : Form
{
    private readonly TextBox _first = new() { UseSystemPasswordChar = true, Width = 300 };
    private readonly TextBox _second = new() { UseSystemPasswordChar = true, Width = 300 };

    private NewPasswordForm(string message)
    {
        Text = "LabelGuard - senha do supervisor";
        Icon = SystemIcons.Shield;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var ok = new Button { Text = "OK" };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Confirm();
        AcceptButton = ok;
        CancelButton = cancel;

        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(12) };
        layout.Controls.Add(new Label { Text = message, AutoSize = true, MaximumSize = new Size(320, 0) });
        layout.Controls.Add(new Label { Text = $"Nova senha (mínimo {SupervisorPassword.MinLength} caracteres):", AutoSize = true });
        layout.Controls.Add(_first);
        layout.Controls.Add(new Label { Text = "Repita a senha:", AutoSize = true });
        layout.Controls.Add(_second);
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
    }

    private void Confirm()
    {
        if (_first.Text.Length < SupervisorPassword.MinLength)
            Dialogs.Error($"A senha precisa ter pelo menos {SupervisorPassword.MinLength} caracteres.", this);
        else if (_first.Text != _second.Text)
            Dialogs.Error("As senhas não conferem.", this);
        else
            DialogResult = DialogResult.OK;
    }

    /// <summary>Retorna a nova senha, ou null se cancelou.</summary>
    public static string? Ask(IWin32Window? owner, string message)
    {
        using var form = new NewPasswordForm(message);
        return form.ShowDialog(owner) == DialogResult.OK ? form._first.Text : null;
    }
}

/// <summary>Últimos eventos do histórico.</summary>
internal sealed class EventsForm : Form
{
    private readonly LabelHistory _history;
    private readonly ListView _list;

    public EventsForm(LabelHistory history)
    {
        _history = history;
        Text = "LabelGuard - eventos";
        Icon = SystemIcons.Shield;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(820, 480);

        _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
        _list.Columns.Add("Data/hora", 140);
        _list.Columns.Add("Tipo", 90);
        _list.Columns.Add("Mensagem", 420);
        _list.Columns.Add("Trabalho", 140);

        var refresh = new Button { Text = "Atualizar", Dock = DockStyle.Bottom, Height = 32 };
        refresh.Click += (_, _) => Reload();

        Controls.Add(_list);
        Controls.Add(refresh);
    }

    public void Reload()
    {
        if (IsDisposed)
            return;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var e in _history.GetRecentEvents(300))
        {
            var item = new ListViewItem(OutcomeText.FormatTime(e.At));
            item.SubItems.Add(e.Type.Describe());
            item.SubItems.Add(e.Message);
            item.SubItems.Add(e.Job ?? "");
            if (e.Type is EventType.Blocked or EventType.NoId or EventType.Error)
                item.ForeColor = Color.Firebrick;
            _list.Items.Add(item);
        }
        _list.EndUpdate();
    }
}
