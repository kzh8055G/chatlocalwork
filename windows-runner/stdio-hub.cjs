'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { StdioMcpClient } = require('./stdio-mcp-client.cjs');
const sessions = new Map();
const idRegex = /^[a-z][a-z0-9_-]{0,47}$/;
const configRoot = () => path.join(process.env.LOCALAPPDATA || '', 'ChatLocalWork');

function loadProfile(server, workspaceRoot) {
  if (!idRegex.test(server)) throw new Error('Invalid MCP server ID');
  if (!process.env.LOCALAPPDATA) throw new Error('LOCALAPPDATA missing');
  const registry=JSON.parse(fs.readFileSync(path.join(configRoot(),'config','mcp-servers.json'),'utf8'));
  const reg=registry.servers?.find(x=>x.id===server && x.transport==='windowsStdio' && x.enabled===true);
  if (!reg || !Array.isArray(reg.allowedTools) || !reg.allowedTools.length) throw new Error('Windows stdio server is not approved in Registry');
  const doc=JSON.parse(fs.readFileSync(path.join(configRoot(),'private','mcp-stdio-profiles.json'),'utf8'));
  if (doc.version!==1 || !Array.isArray(doc.servers)) throw new Error('Invalid host-only MCP profiles');
  const profile=doc.servers.find(x=>x.id===server && x.approved===true);
  if (!profile) throw new Error('Windows stdio profile requires Manager approval');
  if (typeof profile.command!=='string' || !path.win32.isAbsolute(profile.command) || !/\.exe$/i.test(profile.command) ||
      !Array.isArray(profile.args) || !profile.args.every(x=>typeof x==='string') ||
      typeof profile.cwd!=='string' || !path.win32.isAbsolute(profile.cwd) ||
      !Array.isArray(profile.allowedTools) || !profile.allowedTools.every(x=>typeof x==='string')) throw new Error('Invalid host MCP profile');
  const root=fs.realpathSync.native(workspaceRoot).toLowerCase();
  const cwd=fs.realpathSync.native(profile.cwd);
  if (cwd.toLowerCase()!==root && !cwd.toLowerCase().startsWith(root + path.sep)) throw new Error('MCP stdio cwd must be inside Workspace');
  const command=fs.realpathSync.native(profile.command);
  const blocked=new Set(['powershell.exe','pwsh.exe','cmd.exe','wscript.exe','cscript.exe','mshta.exe','rundll32.exe','regsvr32.exe']);
  if (blocked.has(path.basename(command).toLowerCase())) throw new Error('Blocked MCP stdio executable');
  if (profile.args.length>64 || profile.args.join('\0').length>16000 || profile.allowedTools.length>100) throw new Error('MCP stdio profile exceeds limits');
  // Minimal OS environment, plus explicitly selected variable names already present on Windows.
  const baseline=['SystemRoot','WINDIR','PATH','PATHEXT','COMSPEC','TEMP','TMP','USERPROFILE','APPDATA','LOCALAPPDATA','PROGRAMDATA','ProgramFiles','ProgramFiles(x86)','HOMEDRIVE','HOMEPATH'];
  const env={};
  for (const key of baseline) if (process.env[key]!==undefined) env[key]=process.env[key];
  if (profile.envNames !== undefined) {
    if (!Array.isArray(profile.envNames) || profile.envNames.length>40 ||
        !profile.envNames.every(x=>typeof x==='string' && /^[A-Za-z_][A-Za-z0-9_]{0,80}$/.test(x))) throw new Error('Invalid MCP environment variable names');
    for (const name of profile.envNames) {
      if(process.env[name]===undefined) throw new Error(`Required MCP environment variable not set: ${name}`);
      env[name]=process.env[name];
    }
  }
  const allowed=new Set(profile.allowedTools.filter(t=>reg.allowedTools.includes(t)));
  const fingerprint=JSON.stringify([command,profile.args,cwd,profile.allowedTools,profile.envNames,reg.allowedTools]);
  return {command,args:profile.args,cwd,env,allowed,fingerprint};
}
async function handle(request, {workspaceRoot}) {
  const id=String(request.server||'');
  if(!['listTools','callTool'].includes(request.operation)) throw new Error('Invalid Windows stdio operation');
  let profile;
  try { profile=loadProfile(id,workspaceRoot); }
  catch(error) { const old=sessions.get(id);if(old){old.client.dispose();sessions.delete(id);}throw error; }
  let slot=sessions.get(id);
  if (slot && slot.fingerprint!==profile.fingerprint) {slot.client.dispose();sessions.delete(id);slot=null;}
  if(!slot) {slot={fingerprint:profile.fingerprint,workspaceRoot,client:new StdioMcpClient(profile)};sessions.set(id,slot);}
  if(request.operation==='listTools') {
    const result=await slot.client.listTools();
    return {tools:result.tools.filter(x=>profile.allowed.has(x.name))};
  }
  if(typeof request.tool!=='string' || !profile.allowed.has(request.tool)) throw new Error('Windows stdio tool not approved');
  if(request.arguments===null || typeof request.arguments!=='object' || Array.isArray(request.arguments)) throw new Error('Invalid MCP tool arguments');
  return slot.client.callTool(request.tool,request.arguments);
}
function shutdown(){for(const slot of sessions.values())slot.client.dispose();sessions.clear();}
// Revocations must take effect even when no further tool calls arrive.
const reaper=setInterval(() => {
  for(const [id,slot] of sessions) {
    try {
      const profile=loadProfile(id,slot.workspaceRoot);
      if(profile.fingerprint!==slot.fingerprint) {slot.client.dispose();sessions.delete(id);}
    } catch {slot.client.dispose();sessions.delete(id);}
  }
},5000);
reaper.unref();
process.once('exit',shutdown);
module.exports={handle,shutdown};
