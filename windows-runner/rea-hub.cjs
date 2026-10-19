'use strict';
// REA policy adapter on top of the shared Windows MCP stdio session manager.
const fs = require('node:fs');
const path = require('node:path');
const { StdioMcpClient } = require('./stdio-mcp-client.cjs');
const allow = new Set([
  'open_binary','list_documents','current_document','list_segments','list_procedures',
  'list_strings','list_names','search_procedures','search_strings',
  'read_bytes','procedure_info','procedure_pseudo_code',
  'procedure_assembly','procedure_callers','procedure_callees',
  'read_function_instructions','inspect_native_instruction',
  'inspect_native_data_type','inspect_native_load_image',
  'address_to_file_offset','procedure_references',
]);
let instance;
function providerConfig() {
  if (!process.env.LOCALAPPDATA) throw Error('LOCALAPPDATA is required');
  const configPath = path.join(process.env.LOCALAPPDATA,'ChatLocalWork','config','rea-provider.json');
  const config = JSON.parse(fs.readFileSync(configPath,'utf8'));
  if (![config.ghidraInstallDir,config.javaHome,config.npxCli].every(p => typeof p === 'string' && path.isAbsolute(p))) throw Error('Invalid REA provider configuration');
  if (!fs.existsSync(path.join(config.ghidraInstallDir,'Ghidra','application.properties')) || !fs.existsSync(path.join(config.javaHome,'bin','java.exe')) || !fs.existsSync(config.npxCli)) throw Error('Configured REA/Ghidra/JDK files missing');
  return config;
}
async function handle(req, options = {}) {
  if(req.server!=='rea')throw Error('Unknown Windows MCP server');
  if(!['listTools','callTool'].includes(req.operation))throw Error('Invalid MCP operation');
  // Enforce the host's independently stored, currently enabled Registry policy.
  const registry=JSON.parse(fs.readFileSync(path.join(process.env.LOCALAPPDATA,'ChatLocalWork','config','mcp-servers.json'),'utf8'));
  const entry=registry.servers?.find(x=>x.id==='rea' && x.transport==='windowsRea' && x.enabled===true);
  if(!entry || !Array.isArray(entry.allowedTools)) throw Error('REA server is disabled in Registry');
  const approved=new Set(entry.allowedTools.filter(name=>allow.has(name)));
  if(req.operation==='callTool' && !approved.has(req.tool)) throw Error('REA tool not allowed by Registry');
  if(req.operation==='callTool') {
    if(!allow.has(req.tool))throw Error('REA tool not in approved Windows whitelist');
    if(!req.arguments || typeof req.arguments!=='object' || Array.isArray(req.arguments))throw Error('Invalid REA tool arguments');
    if(req.tool === 'open_binary') {
      const workspaceRoot = options.workspaceRoot;
      if(typeof workspaceRoot !== 'string' || !path.isAbsolute(workspaceRoot))throw Error('REA workspace root is required');
      if(typeof req.arguments.path !== 'string' || !path.win32.isAbsolute(req.arguments.path))throw Error('REA target must be an absolute Windows path');
      if(req.arguments.snapshot_path !== undefined)throw Error('REA snapshots are disabled by the hub read-only policy');
      const root=fs.realpathSync.native(workspaceRoot).toLowerCase();
      const target=fs.realpathSync.native(req.arguments.path).toLowerCase();
      if(target!==root && !target.startsWith(root+path.sep))throw Error('REA target must be inside the configured workspace');
    }
  }
  if(!instance){
    const c=providerConfig();
    instance=new StdioMcpClient({
      command:process.execPath,args:[c.npxCli,'-y','rea-agents@6.5.0','mcp'],
      cwd:path.dirname(c.npxCli),
      env:{...process.env,GHIDRA_INSTALL_DIR:c.ghidraInstallDir,JAVA_HOME:c.javaHome,REA_ANALYSIS_PROVIDER:'ghidra'},
    });
  }
  if(req.operation==='listTools'){
    const result=await instance.listTools();
    return {tools:result.tools.filter(x=>approved.has(x.name))};
  }
  return instance.callTool(req.tool,req.arguments);
}
process.once('exit',()=>instance?.dispose());
module.exports={handle};
