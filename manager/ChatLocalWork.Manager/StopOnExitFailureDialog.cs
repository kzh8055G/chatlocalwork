namespace ChatLocalWork.Manager;

internal enum StopOnExitFailureChoice
{
    Retry,
    CloseManagerOnly,
    Cancel,
}

internal sealed class StopOnExitFailureDialog : Form
{
    public StopOnExitFailureChoice Choice { get; private set; } = StopOnExitFailureChoice.Cancel;

    public StopOnExitFailureDialog(string message)
    {
        Text = "ChatLocalWork Manager";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(570, 190);
        Font = new Font("Segoe UI", 9F);

        var messageLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "MCP 종료에 실패했습니다.\r\n\r\n" + message,
            AutoEllipsis = true,
            Padding = new Padding(14),
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(8),
        };

        var cancelButton = CreateButton("취소", StopOnExitFailureChoice.Cancel);
        var closeOnlyButton = CreateButton("Manager만 종료", StopOnExitFailureChoice.CloseManagerOnly);
        var retryButton = CreateButton("다시 시도", StopOnExitFailureChoice.Retry);

        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(closeOnlyButton);
        buttons.Controls.Add(retryButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.Controls.Add(messageLabel, 0, 0);
        layout.Controls.Add(buttons, 0, 1);
        Controls.Add(layout);

        AcceptButton = retryButton;
        CancelButton = cancelButton;
    }

    private Button CreateButton(string text, StopOnExitFailureChoice choice)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 32,
            Padding = new Padding(10, 0, 10, 0),
        };

        button.Click += (_, _) =>
        {
            Choice = choice;
            DialogResult = choice == StopOnExitFailureChoice.Cancel
                ? DialogResult.Cancel
                : DialogResult.OK;
            Close();
        };

        return button;
    }
}
