using ChatLocalWork.Manager;
var windowsWorkspace=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
var nodeExe=args.Length>0?args[0]:throw new ArgumentException("node.exe absolute path required");
var server=Path.Combine(windowsWorkspace,"windows-runner","mock-stdio-server.test.cjs");
if(!File.Exists(server))throw new FileNotFoundException(server);
var tools=await McpStdioInspector.ListToolsAsync(nodeExe,[server],windowsWorkspace,[],CancellationToken.None);
if(!tools.SequenceEqual(["dangerous","ping"]))throw new Exception("MCP tool inspection mismatch: "+string.Join(",",tools));
try {
    await McpStdioInspector.ListToolsAsync(Path.Combine(Environment.SystemDirectory,"cmd.exe"),[],windowsWorkspace,[],CancellationToken.None);
    throw new Exception("Shell executable was not rejected");
} catch (ArgumentException) { }
Console.WriteLine("STDIO_INSPECTOR_SMOKE_PASS "+string.Join(",",tools));
