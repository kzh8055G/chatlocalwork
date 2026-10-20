namespace ChatLocalWork.Manager;

internal interface IMcpHubPrompts
{
    bool Confirm(IWin32Window owner, string message, string title, MessageBoxIcon icon);
    void Show(IWin32Window owner, string message, string title, MessageBoxIcon icon);
    IReadOnlyList<string>? SelectTools(
        IWin32Window owner,
        IReadOnlyList<string> tools,
        IReadOnlyCollection<string> selectedTools);
}

internal sealed class WinFormsMcpHubPrompts : IMcpHubPrompts
{
    public bool Confirm(IWin32Window owner, string message, string title, MessageBoxIcon icon)
        => MessageBox.Show(owner, message, title, MessageBoxButtons.YesNo, icon) == DialogResult.Yes;

    public void Show(IWin32Window owner, string message, string title, MessageBoxIcon icon)
        => MessageBox.Show(owner, message, title, MessageBoxButtons.OK, icon);

    public IReadOnlyList<string>? SelectTools(
        IWin32Window owner,
        IReadOnlyList<string> tools,
        IReadOnlyCollection<string> selectedTools)
    {
        using var dialog = new Form
        {
            Text = "허용할 MCP 도구 선택",
            Width = 590,
            Height = 610,
            MinimumSize = new Size(480, 450),
            StartPosition = FormStartPosition.CenterParent,
            AutoScaleMode = AutoScaleMode.Dpi,
        };
        var list = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            HorizontalScrollbar = true,
        };
        var selected = new HashSet<string>(selectedTools, StringComparer.Ordinal);
        foreach (var name in tools)
        {
            list.Items.Add(name, selected.Contains(name));
        }

        var explanation = new Label
        {
            Text = $"조회 도구 {tools.Count}개 — 허용할 도구만 선택하세요.",
            Dock = DockStyle.Top,
            Height = 50,
        };
        var add = new Button
        {
            Text = "선택 도구 적용",
            AutoSize = true,
            DialogResult = DialogResult.OK,
        };
        var cancel = new Button
        {
            Text = "취소",
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
        };
        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 55,
            AutoScroll = true,
        };
        bottom.Controls.AddRange([add, cancel]);
        dialog.Controls.Add(list);
        dialog.Controls.Add(bottom);
        dialog.Controls.Add(explanation);
        dialog.AcceptButton = add;
        dialog.CancelButton = cancel;

        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            return null;
        }

        return list.CheckedItems.Cast<string>().ToArray();
    }
}
