using ChatLocalWork.Manager;

var windowsWorkspace=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
var nodeExe=args.Length>0?args[0]:throw new ArgumentException("node.exe absolute path required");
var externalServer=args.Length>1?Path.GetFullPath(args[1]):null;
var server=externalServer??Path.Combine(windowsWorkspace,"windows-runner","mock-stdio-server.test.cjs");
if(!File.Exists(server))throw new FileNotFoundException(server);

var tools=await McpStdioInspector.ListToolsAsync(nodeExe,[server],windowsWorkspace,[],CancellationToken.None);

if(externalServer is null)
{
    if(!tools.SequenceEqual(["dangerous","ping"]))throw new Exception("MCP tool inspection mismatch: "+string.Join(",",tools));
    var verified=await McpStdioInspector.VerifyAllowedToolsAsync(nodeExe,[server],windowsWorkspace,[],["ping"],CancellationToken.None);
    if(verified.MissingAllowedTools.Count!=0)throw new Exception("Expected ping to exist");
}
else
{
    foreach(var required in new[]{"echo","get-sum"})
        if(!tools.Contains(required,StringComparer.Ordinal))throw new Exception("Expected external tool: "+required);
    var verified=await McpStdioInspector.VerifyAllowedToolsAsync(nodeExe,[server],windowsWorkspace,[],["echo","get-sum"],CancellationToken.None);
    if(verified.MissingAllowedTools.Count!=0)throw new Exception("Expected external allowlist tools to exist");
}

var missing=await McpStdioInspector.VerifyAllowedToolsAsync(nodeExe,[server],windowsWorkspace,[],["missing-tool"],CancellationToken.None);
if(!missing.MissingAllowedTools.SequenceEqual(["missing-tool"]))throw new Exception("Missing tool verification mismatch: "+string.Join(",",missing.MissingAllowedTools));

try {
    await McpStdioInspector.ListToolsAsync(Path.Combine(Environment.SystemDirectory,"cmd.exe"),[],windowsWorkspace,[],CancellationToken.None);
    throw new Exception("Shell executable was not rejected");
} catch (ArgumentException) { }

Console.WriteLine("STDIO_INSPECTOR_SMOKE_PASS "+string.Join(",",tools));
