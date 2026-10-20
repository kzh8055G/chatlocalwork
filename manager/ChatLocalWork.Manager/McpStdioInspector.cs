using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChatLocalWork.Manager;

// Only initialize and tools/list. Never invokes tools/call or persists approval.
internal static class McpStdioInspector
{
    internal sealed record VerificationResult(
        IReadOnlyList<string> DiscoveredTools,
        IReadOnlyList<string> MissingAllowedTools);

    internal static async Task<VerificationResult> VerifyAllowedToolsAsync(
        string executable,
        IReadOnlyList<string> args,
        string workdir,
        IReadOnlyList<string> envNames,
        IReadOnlyList<string> allowedTools,
        CancellationToken token)
    {
        var discovered = await ListToolsAsync(executable, args, workdir, envNames, token);
        var available = new HashSet<string>(discovered, StringComparer.Ordinal);
        var missing = allowedTools
            .Where(name => !available.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        return new VerificationResult(discovered, missing);
    }

    internal static async Task<IReadOnlyList<string>> ListToolsAsync(string executable,
        IReadOnlyList<string> args, string workdir, IReadOnlyList<string> envNames, CancellationToken token)
    {
        var blocked = new[] { "powershell.exe", "pwsh.exe", "cmd.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe" };
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable) ||
            !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            blocked.Contains(Path.GetFileName(executable).ToLowerInvariant()))
            throw new ArgumentException("허용된 Windows .exe 절대 경로가 필요합니다.");
        if (!Directory.Exists(workdir) || args.Count > 64 || envNames.Count > 40 ||
            envNames.Any(s => !Regex.IsMatch(s, @"^[A-Za-z_][A-Za-z0-9_]{0,80}$")))
            throw new ArgumentException("작업 폴더, 인수 또는 환경 변수 이름이 올바르지 않습니다.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workdir,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        var baseline = new[] { "SystemRoot", "WINDIR", "PATH", "PATHEXT", "COMSPEC", "TEMP", "TMP", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "PROGRAMDATA", "ProgramFiles", "ProgramFiles(x86)", "HOMEDRIVE", "HOMEPATH" };
        start.Environment.Clear();
        foreach (var name in baseline.Concat(envNames).Distinct())
            if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        foreach (var name in envNames)
            if (!start.Environment.ContainsKey(name)) throw new ArgumentException("환경 변수 없음: " + name);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(45));
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("MCP 프로세스를 시작할 수 없습니다.");
        try
        {
            // Discard untrusted stderr, which could contain credentials or arbitrary logs.
            _ = Task.Run(async () => { try { await process.StandardError.ReadToEndAsync(limit.Token); } catch { } });
            var nextId = 0;
            async Task<JsonElement> RequestAsync(string method, object parameters)
            {
                var id = ++nextId;
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters }).AsMemory(), limit.Token);
                await process.StandardInput.FlushAsync(limit.Token);
                while (true)
                {
                    var line = await process.StandardOutput.ReadLineAsync(limit.Token)
                        ?? throw new InvalidDataException("MCP 서버 응답 없이 종료됐습니다.");
                    if (line.Length > 8 * 1024 * 1024) throw new InvalidDataException("MCP 응답 크기 초과");
                    using var json = JsonDocument.Parse(line);
                    var response = json.RootElement;
                    if (!response.TryGetProperty("id", out var responseId) || responseId.ValueKind != JsonValueKind.Number || responseId.GetInt32() != id) continue;
                    if (response.TryGetProperty("error", out _)) throw new InvalidDataException("MCP 서버가 오류를 반환했습니다.");
                    if (!response.TryGetProperty("result", out var result)) throw new InvalidDataException("MCP 응답에 result가 없습니다.");
                    return result.Clone();
                }
            }
            var hello = await RequestAsync("initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "chatlocalwork-manager-inspector", version = "0.4.2" } });
            if (!hello.TryGetProperty("serverInfo", out _)) throw new InvalidDataException("MCP 초기화 응답 오류");
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "notifications/initialized" }).AsMemory(), limit.Token);
            await process.StandardInput.FlushAsync(limit.Token);
            var names = new SortedSet<string>(StringComparer.Ordinal);
            string? cursor = null;
            for (var page = 0; page < 10; page++)
            {
                var result = await RequestAsync("tools/list", cursor is null ? (object)new { } : new { cursor });
                if (!result.TryGetProperty("tools", out var tools) || tools.ValueKind != JsonValueKind.Array) throw new InvalidDataException("tools/list 응답 오류");
                foreach (var tool in tools.EnumerateArray())
                    if (tool.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && name.GetString() is { } value && value.Length <= 256)
                        names.Add(value);
                if (names.Count > 1000) throw new InvalidDataException("MCP 도구 수 제한 초과");
                if (!result.TryGetProperty("nextCursor", out var next) || next.ValueKind != JsonValueKind.String) return names.ToArray();
                cursor = next.GetString();
            }
            throw new InvalidDataException("도구 목록 페이지 제한 초과");
        }
        finally { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } }
    }
}
