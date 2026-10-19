using ChatLocalWork.Manager;

var samples = """
# MCP sources
[mcp_servers.rea]
command = "npx"
args = ["-y", "rea-agents@6.5.0", "mcp"]
[mcp_servers.rea.env]
GHIDRA_INSTALL_DIR = "C:\\tools\\ghidra"
[mcp_servers.remote]
url = "http://127.0.0.1:4111/mcp" # local only
[mcp_servers.other]
command = "node"
args = ["server.js"]
""";
var items = CodexMcpImporter.Read(samples);
if (items.Count != 3 || items[0].Name != "rea" || items[0].Args.Count != 3
    || items[0].Env["GHIDRA_INSTALL_DIR"] != @"C:\tools\ghidra"
    || items[1].Url != "http://127.0.0.1:4111/mcp"
    || items[2].Command != "node") throw new Exception("Codex importer parse regression");
Console.WriteLine("CODEX_MCP_IMPORTER_SMOKE_PASS entries=" + items.Count);
