'use strict';
const fs=require('node:fs'),os=require('node:os'),path=require('node:path'),crypto=require('node:crypto'),{spawn}=require('node:child_process');
(async()=>{
 const temp=fs.mkdtempSync(path.join(os.tmpdir(),'rea-bridge-smoke-'));
 const key=crypto.randomBytes(32).toString('hex');
 const file=path.join(temp,'key.txt');
 fs.writeFileSync(file,key+'\n');
 const root='F:\\Dev\\PortableTool\\REA';
 const env={...process.env,
 GHIDRA_INSTALL_DIR:path.join(root,'extract-ghidra_12.1.4_PUBLIC_20260921','ghidra_12.1.4_PUBLIC'),
 JAVA_HOME:path.join(root,'extract-OpenJDK25U-jdk_x64_windows_hotspot_25.0.4.1_1','jdk-25.0.4.1+1'),
 REA_BRIDGE_API_KEY_FILE:file,REA_NPX_CMD:'C:\\nvm4w\\nodejs\\npx.cmd',REA_BRIDGE_PORT:'4188'};
 const c=spawn(process.execPath,[path.join(__dirname,'start.cjs')],{cwd:__dirname,env,windowsHide:true});
 let log='';c.stdout.on('data',b=>{log+=b.toString()});c.stderr.on('data',b=>{log+=b.toString()});
 const target='http://127.0.0.1:4188/mcp';
 try{
 let ready=false;
 for(let i=0;i<45;i++){
  if(c.exitCode!==null)throw Error('gateway exited '+c.exitCode+' '+log.slice(-1000));
  try{const r=await fetch(target,{method:'POST',headers:{'content-type':'application/json'},body:'{}'});if(r.status===401){ready=true;break;}}catch{}
  await new Promise(r=>setTimeout(r,300));
 }
 if(!ready)throw Error('no authorization challenge '+log.slice(-1600));
 console.log('UNAUTHENTICATED=401');
 const resp=await fetch(target,{method:'POST',headers:{'content-type':'application/json',authorization:'Bearer '+key,accept:'application/json, text/event-stream','mcp-protocol-version':'2025-06-18'},body:JSON.stringify({jsonrpc:'2.0',id:1,method:'initialize',params:{protocolVersion:'2025-06-18',capabilities:{},clientInfo:{name:'rea-bridge-smoke',version:'1.0'}}})});
 const body=await resp.text();
 console.log('AUTH_STATUS='+resp.status); console.log('RESPONSE='+body.slice(0,900));
 if(resp.status!==200 || !body.includes('serverInfo'))throw Error('MCP initialize unsuccessful');
 const sessionId=resp.headers.get('mcp-session-id');
 const listResp=await fetch(target,{method:'POST',headers:{'content-type':'application/json',authorization:'Bearer '+key,accept:'application/json, text/event-stream','mcp-protocol-version':'2025-06-18',...(sessionId?{'mcp-session-id':sessionId}:{})},body:JSON.stringify({jsonrpc:'2.0',id:2,method:'tools/list',params:{}})});
 const listBody=await listResp.text();
 const dataLine=listBody.split('\n').find(line=>line.startsWith('data: '));
 const toolData=dataLine?JSON.parse(dataLine.slice(6)):JSON.parse(listBody);
 const toolNames=(toolData.result?.tools||[]).map(tool=>tool.name);
 console.log('TOOL_COUNT='+toolNames.length+' TOOLS='+JSON.stringify(toolNames.slice(0,40)));
 if(toolNames.length!==20 || toolNames.includes('set_comment') || !toolNames.includes('procedure_pseudo_code'))throw Error('Tool allowlist failed: '+toolNames.length);
 const blocked=await fetch(target,{method:'POST',headers:{'content-type':'application/json',authorization:'Bearer '+key,accept:'application/json, text/event-stream','mcp-protocol-version':'2025-06-18',...(sessionId?{'mcp-session-id':sessionId}:{})},body:JSON.stringify({jsonrpc:'2.0',id:3,method:'tools/call',params:{name:'set_comment',arguments:{}}})});
 const blockedText=await blocked.text();
 if(!blockedText.includes('not allowed') && !blockedText.includes('not available') && !blockedText.includes('not found') && !blockedText.includes('Unknown tool')) throw Error('Blocked tool did not return an explicit denial: '+blockedText.slice(0,300));
 console.log('BLOCKED_MUTATION=DENIED');
 console.log('SMOKE_PASS');
 }finally{
 try{c.kill()}catch{}
 setTimeout(()=>{try{fs.rmSync(temp,{recursive:true,force:true})}catch{}},1000).unref();
 }
})().catch(e=>{console.error('SMOKE_FAIL',e.message);process.exitCode=1});
