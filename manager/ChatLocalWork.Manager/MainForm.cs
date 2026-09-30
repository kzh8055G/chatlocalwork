using System.Diagnostics;

namespace ChatLocalWork.Manager;

internal sealed class MainForm : Form
{
    private const int MaxLogCharacters = 250_000;

    private static readonly string[] ComponentNames =
    {
        "Windows Runner",
        "Docker",
        "Tailscale",
        "Funnel",
        "MCP",
    };

    private readonly AppPaths _paths;
    private readonly MpcLifecycleService _lifecycleService;
    private readonly StatusService _statusService;
    private readonly Dictionary<string, Label> _stateLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Label> _detailLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer _statusTimer;

    private readonly Button _startButton = new() { Text = "Start MCP", AutoSize = true };
    private readonly Button _stopButton = new() { Text = "Stop MCP", AutoSize = true };
    private readonly Button _refreshButton = new() { Text = "상태 새로 고침", AutoSize = true };
    private readonly Button _loadLogButton = new() { Text = "Runner 로그", AutoSize = true };
    private readonly Button _openRepositoryButton = new() { Text = "프로젝트 폴더", AutoSize = true };
    private readonly Button _clearLogButton = new() { Text = "로그 지우기", AutoSize = true };
    private readonly CheckBox _stopMcpOnExitCheckBox = new()
    {
        Text = "Manager 종료 시 MCP도 종료",
        AutoSize = true,
        Checked = true,
        Margin = new Padding(12, 8, 0, 0),
    };

    private readonly RichTextBox _logBox = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        DetectUrls = false,
        Font = new Font("Consolas", 9F),
        BackColor = SystemColors.Window,
    };

    private readonly Label _overallLabel = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight,
        Font = new Font("Segoe UI", 10F, FontStyle.Bold),
        Text = "상태 확인 중...",
    };

    private readonly Label _lastCheckedLabel = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight,
        ForeColor = SystemColors.GrayText,
        Font = new Font("Segoe UI", 8.5F),
        Text = "마지막 확인: -",
    };

    private readonly Label _operationStateLabel = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Segoe UI", 10F, FontStyle.Bold),
        Text = "대기 중",
    };

    private readonly Label _operationDetailLabel = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = SystemColors.GrayText,
        AutoEllipsis = true,
        Text = "아직 실행된 Start/Stop 작업이 없습니다.",
    };

    private bool _busy;
    private bool _refreshing;
    private bool _lifecycleStatusRefreshPending;
    private bool _lifecycleStatusRefreshWorkerRunning;
    private bool _allowClose;
    private bool _exitStopInProgress;
    private string? _currentLifecycleStage;

    public MainForm(AppPaths paths)
    {
        _paths = paths;
        _lifecycleService = new MpcLifecycleService(paths);
        _statusService = new StatusService(paths);

        var settings = ManagerSettingsStore.Load(_paths.ManagerSettingsFile);
        _stopMcpOnExitCheckBox.Checked = settings.StopMcpOnExit;

        Text = "ChatLocalWork Manager";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(780, 640);
        Size = new Size(980, 720);
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Controls.Add(BuildLayout());

        _startButton.Click += async (_, _) => await RunLifecycleAsync(start: true);
        _stopButton.Click += async (_, _) => await RunLifecycleAsync(start: false);
        _refreshButton.Click += async (_, _) => await RefreshStatusAsync();
        _loadLogButton.Click += (_, _) => LoadRunnerLog();
        _openRepositoryButton.Click += (_, _) => OpenRepository();
        _clearLogButton.Click += (_, _) => _logBox.Clear();
        _stopMcpOnExitCheckBox.CheckedChanged += (_, _) => SaveManagerSettings();

        _statusTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();

        Shown += async (_, _) =>
        {
            AppendManagerLog($"Repository: {_paths.RepositoryRoot}");
            AppendManagerLog($"Workspace : {_paths.WorkspaceRoot}");
            AppendManagerLog($"Runtime   : {_paths.RunnerQueueDirectory}");
            AppendManagerLog("Manager 시작 · MCP 자동 시작");

            await RefreshStatusAsync();
            await RunLifecycleAsync(start: true);
        };

        FormClosing += MainForm_FormClosing;
        FormClosed += (_, _) => _statusTimer.Dispose();
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18),
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 238));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));

        var titlePanel = new Panel { Dock = DockStyle.Fill };

        var title = new Label
        {
            Text = "ChatLocalWork Manager",
            AutoSize = true,
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            Location = new Point(0, 0),
        };

        var subtitle = new Label
        {
            Text = "Windows Runner · Docker · Tailscale · MCP 상태 및 실행 관리",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(2, 40),
        };

        titlePanel.Controls.Add(title);
        titlePanel.Controls.Add(subtitle);

        var summary = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
        };
        summary.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        summary.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        summary.Controls.Add(_overallLabel, 0, 0);
        summary.Controls.Add(_lastCheckedLabel, 0, 1);

        header.Controls.Add(titlePanel, 0, 0);
        header.Controls.Add(summary, 1, 0);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            Padding = new Padding(0, 3, 0, 0),
        };

        var buttons = actions;

        foreach (var button in new[]
                 {
                     _startButton,
                     _stopButton,
                     _refreshButton,
                     _loadLogButton,
                     _openRepositoryButton,
                     _clearLogButton,
                 })
        {
            button.Height = 32;
            button.Padding = new Padding(7, 0, 7, 0);
            buttons.Controls.Add(button);
        }
        buttons.Controls.Add(_stopMcpOnExitCheckBox);

        var operationGroup = new GroupBox
        {
            Text = "작업 상태",
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 8, 10, 8),
        };

        var operationTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        operationTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        operationTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        operationTable.Controls.Add(_operationStateLabel, 0, 0);
        operationTable.Controls.Add(_operationDetailLabel, 0, 1);
        operationGroup.Controls.Add(operationTable);

        var statusGroup = new GroupBox
        {
            Text = "상태",
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
        };

        var statusTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = ComponentNames.Length + 1,
        };

        statusTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        statusTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        statusTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        statusTable.Controls.Add(CreateHeaderLabel("구성 요소"), 0, 0);
        statusTable.Controls.Add(CreateHeaderLabel("상태"), 1, 0);
        statusTable.Controls.Add(CreateHeaderLabel("상세"), 2, 0);

        for (var i = 0; i < ComponentNames.Length; i++)
        {
            var name = ComponentNames[i];
            var row = i + 1;
            statusTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            statusTable.Controls.Add(new Label
            {
                Text = name,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            }, 0, row);

            var stateLabel = new Label
            {
                Text = "-",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            };

            var detailLabel = new Label
            {
                Text = "-",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
            };

            _stateLabels[name] = stateLabel;
            _detailLabels[name] = detailLabel;

            statusTable.Controls.Add(stateLabel, 1, row);
            statusTable.Controls.Add(detailLabel, 2, row);
        }

        statusGroup.Controls.Add(statusTable);

        var logGroup = new GroupBox
        {
            Text = "실행 로그",
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
        };
        logGroup.Controls.Add(_logBox);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(actions, 0, 1);
        root.Controls.Add(operationGroup, 0, 2);
        root.Controls.Add(statusGroup, 0, 3);
        root.Controls.Add(logGroup, 0, 4);

        return root;
    }

    private static Label CreateHeaderLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        };
    }

    private async Task RunLifecycleAsync(bool start)
    {
        if (_busy)
        {
            return;
        }

        var operation = start ? "START" : "STOP";
        var stopwatch = Stopwatch.StartNew();
        _currentLifecycleStage = null;

        SetBusy(true, operation);
        SetOperationStatus(
            $"{operation} 진행 중",
            "초기화 중...",
            Color.SteelBlue);
        _statusTimer.Stop();

        AppendLog(string.Empty);
        AppendLog($"===== {operation} {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");

        try
        {
            void HandleLifecycleOutput(string line)
            {
                AppendLog(line);
                ProcessLifecycleOutput(operation, line);
            }

            var result = start
                ? await _lifecycleService.StartAsync(HandleLifecycleOutput)
                : await _lifecycleService.StopAsync(HandleLifecycleOutput);

            stopwatch.Stop();

            if (result.Success)
            {
                var detail = _currentLifecycleStage is null
                    ? $"완료 · {FormatDuration(stopwatch.Elapsed)}"
                    : $"마지막 단계: {_currentLifecycleStage} · {FormatDuration(stopwatch.Elapsed)}";
                SetOperationStatus($"{operation} 성공", detail, Color.ForestGreen);
                AppendManagerLog($"{operation} 완료 · {FormatDuration(stopwatch.Elapsed)}");
            }
            else if (result.TimedOut)
            {
                var stage = _currentLifecycleStage ?? "알 수 없음";
                SetOperationStatus(
                    $"{operation} 시간 초과 · 단계: {stage}",
                    $"제한 시간을 초과했습니다. · {FormatDuration(stopwatch.Elapsed)}",
                    Color.DarkOrange);
                AppendManagerLog($"{operation} 시간 초과 · 단계={stage} · {FormatDuration(stopwatch.Elapsed)}");
            }
            else
            {
                var reason = GetFailureSummary(result);
                var stage = _currentLifecycleStage ?? "알 수 없음";
                var detail = string.IsNullOrWhiteSpace(reason)
                    ? $"ExitCode={result.ExitCode}"
                    : $"ExitCode={result.ExitCode} · {reason}";
                SetOperationStatus($"{operation} 실패 · 단계: {stage}", detail, Color.Firebrick);
                var suffix = string.IsNullOrWhiteSpace(reason) ? string.Empty : $" · {reason}";
                AppendManagerLog($"{operation} 실패 · 단계={stage} · ExitCode={result.ExitCode}{suffix}");
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var stage = _currentLifecycleStage ?? "초기화";
            SetOperationStatus($"{operation} 오류 · 단계: {stage}", ex.Message, Color.Firebrick);
            AppendManagerLog($"{operation} ERROR · 단계={stage} · {ex.Message}");
            MessageBox.Show(
                ex.Message,
                "ChatLocalWork Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            await RefreshStatusAsync();
            _statusTimer.Start();
        }
    }

    private async Task RefreshStatusAsync(bool forceDuringLifecycle = false)
    {
        if ((!forceDuringLifecycle && _busy) || _refreshing)
        {
            return;
        }

        _refreshing = true;
        _refreshButton.Enabled = false;

        try
        {
            var statuses = await _statusService.GetStatusAsync();

            foreach (var status in statuses)
            {
                if (_stateLabels.TryGetValue(status.Name, out var stateLabel))
                {
                    stateLabel.Text = StateText(status.State);
                    stateLabel.ForeColor = StateColor(status.State);
                }

                if (_detailLabels.TryGetValue(status.Name, out var detailLabel))
                {
                    detailLabel.Text = status.Detail;
                    detailLabel.AccessibleDescription = status.Detail;
                }
            }

            if (!_busy)
            {
                UpdateOverallStatus(statuses);
            }
            _lastCheckedLabel.Text = $"마지막 확인: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _overallLabel.Text = "전체 상태: 확인 실패";
            _overallLabel.ForeColor = Color.Firebrick;
            _lastCheckedLabel.Text = $"마지막 확인 실패: {DateTime.Now:HH:mm:ss}";
            AppendManagerLog($"STATUS 오류 · {ex.Message}");
        }
        finally
        {
            _refreshing = false;
            _refreshButton.Enabled = !_busy;
        }
    }

    private void UpdateOverallStatus(IReadOnlyList<ComponentStatus> statuses)
    {
        var readyCount = statuses.Count(status => status.State == ComponentState.Ready);
        var allReady = readyCount == statuses.Count;
        var allStopped = statuses.All(status => status.State == ComponentState.Stopped);
        var needsAttention = statuses.Any(status =>
            status.State is ComponentState.Warning or ComponentState.Unavailable);

        if (allReady)
        {
            _overallLabel.Text = "전체 상태: READY";
            _overallLabel.ForeColor = Color.ForestGreen;
            return;
        }

        if (allStopped)
        {
            _overallLabel.Text = "전체 상태: STOPPED";
            _overallLabel.ForeColor = SystemColors.GrayText;
            return;
        }

        _overallLabel.Text = needsAttention
            ? $"전체 상태: 확인 필요 · {readyCount}/{statuses.Count} READY"
            : $"전체 상태: PARTIAL · {readyCount}/{statuses.Count} READY";
        _overallLabel.ForeColor = needsAttention ? Color.DarkOrange : Color.SteelBlue;
    }

    private async void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        if (_exitStopInProgress)
        {
            e.Cancel = true;
            return;
        }

        if (_busy)
        {
            e.Cancel = true;
            MessageBox.Show(
                "Start/Stop 작업이 진행 중입니다. 작업이 완료된 뒤 Manager를 종료해 주세요.",
                "ChatLocalWork Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!_stopMcpOnExitCheckBox.Checked)
        {
            _allowClose = true;
            return;
        }

        e.Cancel = true;
        _exitStopInProgress = true;
        _statusTimer.Stop();

        try
        {
            var shouldStop = await IsChatLocalWorkRunningAsync();
            if (!shouldStop)
            {
                _allowClose = true;
                BeginInvoke(Close);
                return;
            }

            while (true)
            {
                SetBusy(true, "STOP");
                SetOperationStatus(
                    "Manager 종료 중 · MCP 정리",
                    "StopMCP를 실행하고 있습니다...",
                    Color.SteelBlue);
                AppendManagerLog("Manager 종료 요청 · MCP 자동 종료 시작");

                ProcessResult result;
                try
                {
                    void HandleLifecycleOutput(string line)
                    {
                        AppendLog(line);
                        ProcessLifecycleOutput("STOP", line);
                    }

                    result = await _lifecycleService.StopAsync(HandleLifecycleOutput);
                }
                catch (Exception ex)
                {
                    result = new ProcessResult(-1, string.Empty, ex.Message, false);
                }

                if (result.Success)
                {
                    AppendManagerLog("MCP 자동 종료 완료 · Manager를 종료합니다.");
                    _allowClose = true;
                    BeginInvoke(Close);
                    return;
                }

                var reason = result.TimedOut
                    ? "StopMCP 실행 시간이 초과되었습니다."
                    : GetFailureSummary(result);
                if (string.IsNullOrWhiteSpace(reason))
                {
                    reason = $"ExitCode={result.ExitCode}";
                }

                SetBusy(false);
                SetOperationStatus("MCP 자동 종료 실패", reason, Color.Firebrick);

                using var dialog = new StopOnExitFailureDialog(reason);
                dialog.ShowDialog(this);

                if (dialog.Choice == StopOnExitFailureChoice.Retry)
                {
                    continue;
                }

                if (dialog.Choice == StopOnExitFailureChoice.CloseManagerOnly)
                {
                    AppendManagerLog("MCP는 유지하고 Manager만 종료합니다.");
                    _allowClose = true;
                    BeginInvoke(Close);
                    return;
                }

                AppendManagerLog("Manager 종료를 취소했습니다.");
                return;
            }
        }
        finally
        {
            _exitStopInProgress = false;
            if (!_allowClose && !IsDisposed)
            {
                SetBusy(false);
                _statusTimer.Start();
                await RefreshStatusAsync();
            }
        }
    }

    private async Task<bool> IsChatLocalWorkRunningAsync()
    {
        try
        {
            var statuses = await _statusService.GetStatusAsync();
            return statuses.Any(status =>
                status.Name is "Windows Runner" or "Docker" or "Funnel" or "MCP"
                && status.State != ComponentState.Stopped);
        }
        catch
        {
            // 상태 확인 자체가 실패하면 잔여 리소스가 있을 수 있으므로 Stop을 시도합니다.
            return true;
        }
    }

    private void SaveManagerSettings()
    {
        try
        {
            ManagerSettingsStore.Save(
                _paths.ManagerSettingsFile,
                new ManagerSettings(_stopMcpOnExitCheckBox.Checked));
        }
        catch (Exception ex)
        {
            AppendManagerLog($"설정 저장 실패 · {ex.Message}");
        }
    }

    private void LoadRunnerLog()
    {
        var candidates = new[] { _paths.RunnerLogFile, _paths.LauncherLogFile };
        var existing = candidates.Where(File.Exists).ToArray();

        if (existing.Length == 0)
        {
            AppendManagerLog("Runner 로그 파일이 없습니다.");
            return;
        }

        foreach (var file in existing)
        {
            AppendLog(string.Empty);
            AppendLog($"----- {file} -----");

            try
            {
                var text = ReadTail(file, 64 * 1024);
                foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                {
                    AppendLog(line);
                }
            }
            catch (Exception ex)
            {
                AppendManagerLog($"Runner 로그 읽기 실패 · {ex.Message}");
            }
        }
    }

    private static string ReadTail(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var offset = Math.Max(0, stream.Length - maxBytes);
        stream.Seek(offset, SeekOrigin.Begin);

        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();

        if (offset > 0)
        {
            var firstNewLine = text.IndexOf('\n');
            if (firstNewLine >= 0)
            {
                text = text[(firstNewLine + 1)..];
            }
        }

        return text;
    }

    private void OpenRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                ArgumentList = { _paths.RepositoryRoot },
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppendManagerLog($"프로젝트 폴더 열기 실패 · {ex.Message}");
        }
    }

    private void SetBusy(bool busy, string? operation = null)
    {
        _busy = busy;
        _startButton.Enabled = !busy;
        _stopButton.Enabled = !busy;
        _refreshButton.Enabled = !busy;
        _loadLogButton.Enabled = !busy;

        _startButton.Text = busy && operation == "START" ? "Starting..." : "Start MCP";
        _stopButton.Text = busy && operation == "STOP" ? "Stopping..." : "Stop MCP";

        if (busy)
        {
            _overallLabel.Text = operation == "START"
                ? "작업 중: STARTING..."
                : "작업 중: STOPPING...";
            _overallLabel.ForeColor = Color.SteelBlue;
        }
    }

    private void ProcessLifecycleOutput(string operation, string line)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ProcessLifecycleOutput(operation, line));
            return;
        }

        var trimmed = line.Trim();
        if (trimmed.StartsWith("==>", StringComparison.Ordinal))
        {
            var stage = trimmed[3..].Trim();
            if (stage.Length == 0)
            {
                return;
            }

            _currentLifecycleStage = stage;
            SetOperationStatus(
                $"{operation} 진행 중 · 단계: {stage}",
                "스크립트 작업을 수행하고 있습니다.",
                Color.SteelBlue);
            return;
        }

        if (trimmed.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase))
        {
            var message = trimmed["[ERROR]".Length..].Trim();
            SetOperationStatus(
                $"{operation} 오류 · 단계: {_currentLifecycleStage ?? "알 수 없음"}",
                message,
                Color.Firebrick);
        }
        else if (trimmed.StartsWith("[WARN]", StringComparison.OrdinalIgnoreCase))
        {
            _operationDetailLabel.Text = trimmed;
            _operationDetailLabel.ForeColor = Color.DarkOrange;
        }

        if (IsLifecycleStatusChange(trimmed))
        {
            RequestLifecycleStatusRefresh();
        }
    }

    private static bool IsLifecycleStatusChange(string line)
    {
        return line.Contains("Windows Runner : READY", StringComparison.OrdinalIgnoreCase)
            || line.Contains("workmachine    : HEALTHY", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Local gateway  : OK", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Tailscale      : OK", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Funnel         : OK", StringComparison.OrdinalIgnoreCase)
            || line.Contains("ChatGPT MCP    : READY", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Environment    : ALREADY READY", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Tailscale      : STOPPED", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Docker         : STOPPED", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Windows Runner : STOPPED", StringComparison.OrdinalIgnoreCase)
            || line.Contains("MCP resources  : STOPPED", StringComparison.OrdinalIgnoreCase);
    }

    private void RequestLifecycleStatusRefresh()
    {
        if (InvokeRequired)
        {
            BeginInvoke(RequestLifecycleStatusRefresh);
            return;
        }

        _lifecycleStatusRefreshPending = true;
        if (_lifecycleStatusRefreshWorkerRunning)
        {
            return;
        }

        _ = DrainLifecycleStatusRefreshesAsync();
    }

    private async Task DrainLifecycleStatusRefreshesAsync()
    {
        _lifecycleStatusRefreshWorkerRunning = true;

        try
        {
            while (_lifecycleStatusRefreshPending)
            {
                _lifecycleStatusRefreshPending = false;
                await RefreshStatusAsync(forceDuringLifecycle: true);
            }
        }
        finally
        {
            _lifecycleStatusRefreshWorkerRunning = false;

            if (_lifecycleStatusRefreshPending && !IsDisposed)
            {
                RequestLifecycleStatusRefresh();
            }
        }
    }

    private void SetOperationStatus(string state, string detail, Color stateColor)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetOperationStatus(state, detail, stateColor));
            return;
        }

        _operationStateLabel.Text = state;
        _operationStateLabel.ForeColor = stateColor;
        _operationDetailLabel.Text = detail;
        _operationDetailLabel.ForeColor = stateColor == Color.Firebrick
            ? Color.Firebrick
            : stateColor == Color.DarkOrange
                ? Color.DarkOrange
                : SystemColors.GrayText;
    }

    private void AppendManagerLog(string text)
    {
        AppendLog($"[{DateTime.Now:HH:mm:ss}] {text}");
    }

    private void AppendLog(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(text));
            return;
        }

        _logBox.AppendText(text + Environment.NewLine);
        TrimLogIfNeeded();
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private void TrimLogIfNeeded()
    {
        if (_logBox.TextLength <= MaxLogCharacters)
        {
            return;
        }

        var removeLength = _logBox.TextLength - MaxLogCharacters;
        var snapshotLength = Math.Min(_logBox.TextLength, removeLength + 4096);
        var snapshot = _logBox.Text[..snapshotLength];
        var nextNewLine = snapshot.IndexOf('\n', removeLength);
        if (nextNewLine >= 0)
        {
            removeLength = nextNewLine + 1;
        }

        _logBox.Select(0, removeLength);
        _logBox.SelectedText = string.Empty;
    }

    private static string GetFailureSummary(ProcessResult result)
    {
        static string? LastUsefulLine(string text)
        {
            return text
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .LastOrDefault(line => line.Length > 0);
        }

        var combined = result.StandardOutput + Environment.NewLine + result.StandardError;
        var explicitError = combined
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .LastOrDefault(line => line.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase));

        var summary = explicitError
            ?? LastUsefulLine(result.StandardError)
            ?? LastUsefulLine(result.StandardOutput)
            ?? string.Empty;

        return summary.Length <= 180 ? summary : summary[..177] + "...";
    }

    private static string FormatDuration(TimeSpan elapsed)
    {
        return elapsed.TotalMinutes >= 1
            ? $"{(int)elapsed.TotalMinutes}분 {elapsed.Seconds}초"
            : $"{elapsed.TotalSeconds:0.0}초";
    }

    private static string StateText(ComponentState state) => state switch
    {
        ComponentState.Ready => "● READY",
        ComponentState.Stopped => "● STOPPED",
        ComponentState.Warning => "● WARNING",
        ComponentState.Unavailable => "● N/A",
        _ => "-",
    };

    private static Color StateColor(ComponentState state) => state switch
    {
        ComponentState.Ready => Color.ForestGreen,
        ComponentState.Stopped => Color.Firebrick,
        ComponentState.Warning => Color.DarkOrange,
        ComponentState.Unavailable => SystemColors.GrayText,
        _ => SystemColors.ControlText,
    };
}
