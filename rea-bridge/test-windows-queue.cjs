'use strict';
const fs=require('node:fs'),path=require('node:path'),{spawn}=require('node:child_process'),crypto=require('node:crypto');
const work=path.resolve(__dirname,'..','..');
const queue=path.join(process.env.LOCALAPPDATA,'ChatLocalWork','runtime','windows-runner','rea-hub-test-'+crypto.randomBytes(3).toString('hex'));
for(const name of ['requests','responses','state'])fs.mkdirSync(path.join(queue,name),{recursive:true});
const runner=spawn(process.execPath,[path.resolve(__dirname,'..','windows-runner','runner.cjs'),'--workspace-root',work,'--queue-dir',queue,'--config',path.resolve(__dirname,'..','windows-runner','runner-config.json')],{cwd:work,windowsHide:true,stdio:['ignore','pipe','pipe']});
let log='';runner.stdout.on('data',d=>log+=d.toString());runner.stderr.on('data',d=>log+=d.toString());
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
(async()=>{
try{
 let ready=false;for(let i=0;i<60;i++){if(fs.existsSync(path.join(queue,'state','ready.json'))){ready=true;break;}await sleep(100);}if(!ready)throw Error('Runner did not start: '+log);
 const id=crypto.randomUUID(),req=path.join(queue,'requests',id+'.json'),res=path.join(queue,'responses',id+'.json');
 fs.writeFileSync(req,JSON.stringify({id,kind:'mcpHub',server:'rea',operation:'listTools'}));
 let response;for(let i=0;i<300;i++){if(fs.existsSync(res)){response=JSON.parse(fs.readFileSync(res,'utf8'));break;}await sleep(100);}
 if(!response)throw Error('REA MCP request timed out: '+log.slice(-800));
 if(!response.ok)throw Error('Runner REA error: '+response.error);
 console.log('QUEUED_REA_TOOLS='+response.result.tools.length);console.log('OPEN_BINARY_SCHEMA='+JSON.stringify(response.result.tools.find(t=>t.name==='open_binary')?.inputSchema).slice(0,3000));
 if(response.result.tools.length!==21)throw Error('REA tool count mismatch');
 async function queueCall(tool,args){
   const opId=crypto.randomUUID(),input=path.join(queue,'requests',opId+'.json'),output=path.join(queue,'responses',opId+'.json');
   fs.writeFileSync(input,JSON.stringify({id:opId,kind:'mcpHub',server:'rea',operation:'callTool',tool,arguments:args}));
   for(let i=0;i<1200;i++){if(fs.existsSync(output)){const result=JSON.parse(fs.readFileSync(output,'utf8'));if(!result.ok)throw Error('REA '+tool+': '+result.error);return result.result;}await sleep(100);}
   throw Error('REA '+tool+' timed out');
 }
 const binary=await queueCall('open_binary',{path:path.join(work,'CPS1NativeRecompiler','build','native','sample.exe'),provider_id:'ghidra'});
 console.log('OPEN_BINARY='+JSON.stringify(binary).slice(0,900));
 if(binary.isError)throw Error('Cannot open sample binary');
 const documents=await queueCall('list_documents',{});
 console.log('LIST_DOCUMENTS='+JSON.stringify(documents).slice(0,800));
 if(documents.isError)throw Error('List documents failed');
 const procedures=await queueCall('list_procedures',{});
 console.log('LIST_PROCEDURES='+JSON.stringify(procedures).slice(0,1300));
 if(procedures.isError)throw Error('List procedures failed');
 console.log('WINDOWS_REA_QUEUE_PASS');
}finally{runner.kill();await sleep(300);try{fs.rmSync(queue,{recursive:true,force:true})}catch{}}})().catch(e=>{console.error('WINDOWS_REA_QUEUE_FAIL',e.stack);process.exitCode=1});
