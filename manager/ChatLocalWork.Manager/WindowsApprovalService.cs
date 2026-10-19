using System.Security.Cryptography;
using System.Text.Json;

namespace ChatLocalWork.Manager;

internal sealed class ApprovalDialog : Form
{
    internal string Choice { get; private set; } = "deny";

    public ApprovalDialog(string executable, string arguments, string cwd, string fingerprint)
    {
        Text = "Windows 명령 실행 승인";
        StartPosition = FormStartPosition.CenterParent;
        TopMost = true;
        ShowInTaskbar = true;
        MinimumSize = new Size(650, 420);
        Size = new Size(760, 490);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 6
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        layout.Controls.Add(new Label {
            Text = "ChatGPT가 다음 Windows 프로그램을 실행하려고 합니다.", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10F, FontStyle.Bold)
        }, 0, 0);
        var details = new TextBox {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            WordWrap = false, Dock = DockStyle.Fill,
            Text = $"실행 파일: {executable}\r\n인수: {arguments}\r\n작업 폴더: {cwd}\r\nSHA-256: {fingerprint}"
        };
        layout.Controls.Add(details, 0, 1);

        AddButton("이번만 실행", "once", 2);
        AddButton("동일 명령을 현재 Runner 세션에서 허용", "session", 3);
        AddButton("이 실행 파일을 whitelist에 추가 (동일 SHA-256에만 적용)", "trust", 4);
        AddButton("거부", "deny", 5);
        Controls.Add(layout);
        Shown += (_, _) => { Activate(); BringToFront(); };

        void AddButton(string caption, string choice, int row)
        {
            var button = new Button { Text = caption, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
            button.Click += (_, _) => { Choice = choice; DialogResult = choice == "deny" ? DialogResult.Cancel : DialogResult.OK; Close(); };
            layout.Controls.Add(button, 0, row);
            if (choice == "deny") CancelButton = button;
        }
    }
}

internal sealed class WindowsApprovalService
{
    private readonly string _approvalDirectory;
    private readonly HashSet<string> _handled = new(StringComparer.OrdinalIgnoreCase);

    public WindowsApprovalService(string runnerQueueDirectory)
    {
        _approvalDirectory = Path.Combine(Path.GetDirectoryName(runnerQueueDirectory)!, "approvals");
    }

    public void Poll(Form owner, Action<string> log)
    {
        var pendingDir = Path.Combine(_approvalDirectory, "pending");
        if (!Directory.Exists(pendingDir)) return;
        foreach (var file in Directory.EnumerateFiles(pendingDir, "*.json"))
        {
            if (!_handled.Add(file)) continue;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                var req = document.RootElement;
                var id = req.GetProperty("id").GetString()!;
                var nonce = req.GetProperty("nonce").GetString()!;
                var executable = req.GetProperty("executable").GetString()!;
                var hash = req.GetProperty("sha256").GetString()!;
                var cwd = req.GetProperty("workdir").GetString()!;
                var arguments = string.Join(" ", req.GetProperty("args").EnumerateArray()
                    .Select(element => JsonSerializer.Serialize(element.GetString())));
                var expires = req.GetProperty("expiresAt").GetDateTime();
                if (DateTime.UtcNow >= expires || !File.Exists(file)) continue;

                using var dialog = new ApprovalDialog(executable, arguments, cwd, hash);
                dialog.ShowDialog(owner);
                if (DateTime.UtcNow >= expires || !File.Exists(file)) continue;

                var choice = dialog.Choice;
                if (choice == "trust")
                {
                    // Hash is verified again by Runner before execution.
                    var trustedPath = Path.Combine(_approvalDirectory, "trusted.json");
                    var entries = new List<TrustedEntry>();
                    if (File.Exists(trustedPath))
                    {
                        try
                        {
                            using var existing = JsonDocument.Parse(File.ReadAllText(trustedPath));
                            foreach (var entry in existing.RootElement.GetProperty("entries").EnumerateArray())
                                entries.Add(new TrustedEntry(
                                    entry.GetProperty("path").GetString()!,
                                    entry.GetProperty("sha256").GetString()!));
                        }
                        catch (Exception ex) { log("기존 whitelist 읽기 오류: " + ex.Message); choice = "deny"; }
                    }
                    if (choice == "trust")
                    {
                        // Whitelist should only include the file currently shown.
                        using var stream = File.OpenRead(executable);
                        var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                        if (!actualHash.Equals(hash, StringComparison.OrdinalIgnoreCase)) choice = "deny";
                        else
                        {
                            if (!entries.Any(e => string.Equals(e.Path, executable, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(e.Sha256, hash, StringComparison.OrdinalIgnoreCase)))
                                entries.Add(new TrustedEntry(executable, hash));
                            WriteAtomic(trustedPath, new { entries });
                        }
                    }
                }
                var decisionPath = Path.Combine(_approvalDirectory, "decisions", id + ".json");
                Directory.CreateDirectory(Path.GetDirectoryName(decisionPath)!);
                WriteAtomic(decisionPath, new { id, nonce, choice });
                log($"Windows 실행 승인: {choice} - {Path.GetFileName(executable)}");
            }
            catch (Exception ex) { log("Windows 실행 승인 처리 오류: " + ex.Message); }
        }
        _handled.RemoveWhere(file => !File.Exists(file));
    }

    private static void WriteAtomic(string file, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        File.Move(temp, file, true);
    }

    private sealed record TrustedEntry(string Path, string Sha256);
}
