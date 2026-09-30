using System.Diagnostics;
using System.Text;

namespace ChatLocalWork.Manager;

internal sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut)
{
    public bool Success => !TimedOut && ExitCode == 0;
}

internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory ?? string.Empty,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            stdout.AppendLine(e.Data);
            onOutput?.Invoke(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            stderr.AppendLine(e.Data);
            onOutput?.Invoke(e.Data);
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start {executable}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = timeout.HasValue
            ? new CancellationTokenSource(timeout.Value)
            : new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        var timedOut = false;

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
            process.WaitForExit();
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            TryKill(process);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return new ProcessResult(
            timedOut ? -1 : process.ExitCode,
            stdout.ToString(),
            stderr.ToString(),
            timedOut);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
