using System.Text;
using System.Text.RegularExpressions;

namespace ChatLocalWork.Manager;

// Narrow TOML reader for Codex MCP server configuration. Importing is only a
// *proposal*: an unknown server must never be executed or enabled automatically.
internal static class CodexMcpImporter
{
    internal sealed record Server(string Name, string? Command, IReadOnlyList<string> Args, string? Url, IReadOnlyDictionary<string, string> Env);

    internal static IReadOnlyList<Server> Read(string toml)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var current = "";
        foreach (var raw in toml.Split('\n'))
        {
            var line = StripComment(raw.Trim());
            if (line.Length == 0) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = line[1..^1].Trim();
                if (!sections.ContainsKey(current)) sections[current] = new(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            var equals = line.IndexOf('=');
            if (equals <= 0 || current.Length == 0) continue;
            sections[current][line[..equals].Trim()] = line[(equals + 1)..].Trim();
        }
        var output = new List<Server>();
        foreach (var (section, fields) in sections)
        {
            if (!section.StartsWith("mcp_servers.", StringComparison.OrdinalIgnoreCase)) continue;
            var suffix = section["mcp_servers.".Length..];
            if (suffix.Contains('.')) continue;
            var name = Unquote(suffix);
            if (!Regex.IsMatch(name, "^[a-zA-Z][a-zA-Z0-9_-]{0,47}$")) continue;
            fields.TryGetValue("command", out var command);
            fields.TryGetValue("args", out var args);
            fields.TryGetValue("url", out var url);
            var env = new Dictionary<string, string>(StringComparer.Ordinal);
            var envSection = "mcp_servers." + suffix + ".env";
            if (sections.TryGetValue(envSection, out var vars))
                foreach (var (key, value) in vars) env[key] = Unquote(value);
            output.Add(new(name, command is null ? null : Unquote(command), ParseArray(args), url is null ? null : Unquote(url), env));
        }
        return output;
    }

    private static string StripComment(string value)
    {
        bool quoted = false, escape = false;
        for (var i=0; i<value.Length; i++)
        {
            var c = value[i];
            if (escape) {escape=false;continue;}
            if (quoted && c == '\\') {escape=true;continue;}
            if (c == '"') quoted=!quoted;
            if (!quoted && c == '#') return value[..i].TrimEnd();
        }
        return value;
    }
    private static string Unquote(string s)
    {
        s=s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
        {
            try { return System.Text.Json.JsonSerializer.Deserialize<string>(s) ?? ""; }
            catch { return s[1..^1]; }
        }
        return s.Trim('\'');
    }
    private static IReadOnlyList<string> ParseArray(string? raw)
    {
        if (raw is null || !raw.TrimStart().StartsWith('[')) return [];
        var matches = Regex.Matches(raw, "\\\"(?:\\\\.|[^\\\"])*\\\"");
        return matches.Select(x => Unquote(x.Value)).ToArray();
    }
}
