using LabelGuard.Core.Guard;

namespace LabelGuard.App;

/// <summary>Janela de alerta com as etiquetas bloqueadas. O supervisor libera a reimpressão com a senha.</summary>
internal sealed class AlertForm : Form
{
    private const int MaxPending = 500;

    private readonly Func<string, bool> _checkPassword;
    private readonly Func<LabelOutcome, Task<LabelOutcome>> _authorize;
    private readonly ListView _list;
    private readonly FlowLayoutPanel _buttons;

    public AlertForm(Func<string, bool> checkPassword, Func<LabelOutcome, Task<LabelOutcome>> authorize)
    {
        _checkPassword = checkPassword;
        _authorize = authorize;

        Text = "LabelGuard - etiqueta repetida bloqueada";
        Icon = SystemIcons.Shield;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ShowInTaskbar = true;
        Size = new Size(760, 420);
        MinimumSize = new Size(560, 300);

        var header = new Label
        {
            Dock = DockStyle.Top,
            Height = 70,
            BackColor = Color.Firebrick,
            ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 13, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "ETIQUETA REPETIDA BLOQUEADA\nNão cole etiquetas repetidas. Para reimprimir, chame o supervisor.",
        };

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            CheckBoxes = true,
            FullRowSelect = true,
            GridLines = true,
            Font = new Font(Font.FontFamily, 10),
        };
        _list.Columns.Add("Pallet", 90);
        _list.Columns.Add("Motivo", 440);
        _list.Columns.Add("Trabalho", 170);

        _buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
        };
        _buttons.Controls.Add(MakeButton("Fechar", (_, _) => Hide()));
        _buttons.Controls.Add(MakeButton("Descartar marcadas", (_, _) => Discard()));
        _buttons.Controls.Add(MakeButton("Liberar marcadas (supervisor)", async (_, _) => await AuthorizeCheckedAsync()));
        _buttons.Controls.Add(MakeButton("Marcar todas", (_, _) => SetAllChecked(true)));

        Controls.Add(_list);
        Controls.Add(_buttons);
        Controls.Add(header);
    }

    public void Add(IEnumerable<LabelOutcome> blocked)
    {
        _list.BeginUpdate();
        foreach (var outcome in blocked)
        {
            var item = new ListViewItem(outcome.Id ?? $"#{outcome.Index}") { Tag = outcome };
            item.SubItems.Add(outcome.Describe());
            item.SubItems.Add(outcome.Job);
            _list.Items.Add(item);
        }
        while (_list.Items.Count > MaxPending)
            _list.Items.RemoveAt(0);
        _list.EndUpdate();
    }

    public void ShowAlert()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // O X só esconde; a lista continua disponível pelo menu da bandeja.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    private async Task AuthorizeCheckedAsync()
    {
        var items = _list.CheckedItems.Cast<ListViewItem>().ToList();
        if (items.Count == 0)
        {
            Dialogs.Info("Marque as etiquetas que devem ser reimpressas.", this);
            return;
        }

        var password = PasswordPrompt.Ask(this, "Liberar reimpressão", $"Reimprimir {items.Count} etiqueta(s).\n\nSenha do supervisor:");
        if (password is null)
            return;
        if (!_checkPassword(password))
        {
            Dialogs.Error("Senha incorreta.", this);
            return;
        }

        _buttons.Enabled = false;
        try
        {
            foreach (var item in items)
            {
                var result = await _authorize((LabelOutcome)item.Tag!);
                if (result.Status == LabelStatus.Printed)
                {
                    _list.Items.Remove(item);
                }
                else
                {
                    item.Tag = result;
                    item.SubItems[1].Text = $"FALHA ao reimprimir: {result.Error}";
                }
            }
        }
        finally
        {
            _buttons.Enabled = true;
        }

        if (_list.Items.Count == 0)
            Hide();
    }

    private void Discard()
    {
        foreach (var item in _list.CheckedItems.Cast<ListViewItem>().ToList())
            _list.Items.Remove(item);
        if (_list.Items.Count == 0)
            Hide();
    }

    private void SetAllChecked(bool value)
    {
        foreach (ListViewItem item in _list.Items)
            item.Checked = value;
    }

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 30, Margin = new Padding(4, 0, 4, 0) };
        button.Click += onClick;
        return button;
    }
}
