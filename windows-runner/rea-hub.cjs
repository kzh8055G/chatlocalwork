'use strict';
// An on-demand, persistent Windows REA MCP stdio client. REA executes on the
// Windows host, without exposing its HTTP bridge or Ghidra to the network.
const fs = require('node:fs');
const path = require('node:path');
const { spawn } = require('node:child_process');
const readline = require('node:readline');

const allow = new Set([
  'open_binary','list_documents','current_document','list_segments','list_procedures',
  'list_strings','list_names','search_procedures','search_strings',
  'read_bytes','procedure_info','procedure_pseudo_code',
  'procedure_assembly','procedure_callers','procedure_callees',
  'read_function_instructions','inspect_native_instruction',
  'inspect_native_data_type','inspect_native_load_image',
  'address_to_file_offset','procedure_references',
]);
let child;
let sequence = 1;
let pending = new Map();
let connection;
function providerConfig() {
  const local = process.env.LOCALAPPDATA;
  if (!local) throw Error('LOCALAPPDATA is required');
  const configPath = path.join(local,'ChatLocalWork','config','rea-provider.json');
  const config = JSON.parse(fs.readFileSync(configPath,'utf8'));
  if (![config.ghidraInstallDir,config.javaHome,config.npxCli].every(p => typeof p === 'string' && path.isAbsolute(p))) throw Error('Invalid REA provider configuration');
  if (!fs.existsSync(path.join(config.ghidraInstallDir,'Ghidra','application.properties')) || !fs.existsSync(path.join(config.javaHome,'bin','java.exe')) || !fs.existsSync(config.npxCli)) throw Error('Configured REA/Ghidra/JDK files missing');
  return config;
}
function reset(error) {
  const active = pending; pending = new Map();
  for(const {reject,timer} of active.values()){clearTimeout(timer);reject(error);}
  child = undefined;
  connection = undefined;
}
function request(method, params = {}, timeoutMs=120000) {
  if (!child || child.exitCode !== null) return Promise.reject(Error('REA process unavailable'));
  const id=sequence++;
  return new Promise((resolve,reject)=>{
    const timer=setTimeout(()=>{pending.delete(id);reject(Error('REA request timed out'));try{child.kill()}catch{}},timeoutMs);
    pending.set(id,{resolve,reject,timer});
    child.stdin.write(JSON.stringify({jsonrpc:'2.0',id,method,params})+'\n',err=>{if(err){clearTimeout(timer);pending.delete(id);reject(err);}});
  });
}
async function connect() {
  if (connection) return connection;
  connection=(async()=>{
    const c=providerConfig();
    const instance=spawn(process.execPath,[c.npxCli,'-y','rea-agents@6.5.0','mcp'],{
      cwd:path.dirname(c.npxCli),shell:false,windowsHide:true,
      env:{...process.env,GHIDRA_INSTALL_DIR:c.ghidraInstallDir,JAVA_HOME:c.javaHome,REA_ANALYSIS_PROVIDER:'ghidra'},
      stdio:['pipe','pipe','pipe'],
    });
    child=instance;
    readline.createInterface({input:instance.stdout}).on('line',line=>{
      let obj;try{obj=JSON.parse(line)}catch{return}
      if(obj.id===undefined)return;
      const waiter=pending.get(obj.id);if(!waiter)return;
      pending.delete(obj.id);clearTimeout(waiter.timer);
      if(obj.error)waiter.reject(Error(JSON.stringify(obj.error)));
      else waiter.resolve(obj.result);
    });
    instance.stderr.on('data',()=>{}); // Protocol stderr does not reach ChatGPT.
    instance.on('error',err=>reset(err));
    instance.on('exit',()=>reset(Error('REA MCP process exited')));
    const handshake=await request('initialize',{protocolVersion:'2025-06-18',capabilities:{},clientInfo:{name:'chatlocalwork-hub',version:'0.1.0'}},60000);
    if(!handshake?.serverInfo)throw Error('REA MCP handshake invalid');
    instance.stdin.write(JSON.stringify({jsonrpc:'2.0',method:'notifications/initialized'})+'\n');
    return true;
  })();
  try{return await connection}catch(err){connection=undefined;throw err}
}
async function handle(req, options = {}) {
  if(req.server!=='rea')throw Error('Unknown Windows MCP server');
  if(!['listTools','callTool'].includes(req.operation))throw Error('Invalid MCP operation');
  await connect();
  if(req.operation==='listTools') {
    let tools=[],cursor;
    for(let page=0;page<10;page++){
      const answer=await request('tools/list',cursor?{cursor}:{},45000);
      if(!Array.isArray(answer?.tools))throw Error('Invalid REA tools/list response');
      tools.push(...answer.tools.filter(x=>allow.has(x.name)));
      cursor=answer.nextCursor;if(!cursor)break;
    }
    return {tools};
  }
  if(!allow.has(req.tool))throw Error('REA tool not in approved Windows whitelist');
  if(!req.arguments || typeof req.arguments!=='object' || Array.isArray(req.arguments))throw Error('Invalid REA tool arguments');
  if(req.tool === 'open_binary') {
    const workspaceRoot = options.workspaceRoot;
    if(typeof workspaceRoot !== 'string' || !path.isAbsolute(workspaceRoot)) throw Error('REA workspace root is required');
    if(typeof req.arguments.path !== 'string' || !path.win32.isAbsolute(req.arguments.path)) throw Error('REA target must be an absolute Windows path');
    if(req.arguments.snapshot_path !== undefined) throw Error('REA snapshots are disabled by the hub read-only policy');
    const root = fs.realpathSync.native(workspaceRoot).toLowerCase();
    const target = fs.realpathSync.native(req.arguments.path).toLowerCase();
    if(target !== root && !target.startsWith(root + path.sep)) throw Error('REA target must be inside the configured workspace');
  }
  return await request('tools/call',{name:req.tool,arguments:req.arguments},120000);
}
process.once('exit',()=>{try{child?.kill()}catch{}});
module.exports={handle};
