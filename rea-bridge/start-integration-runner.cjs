'use strict';
const fs=require('node:fs'),path=require('node:path'),{spawn}=require('node:child_process');
const repo=path.resolve(__dirname,'..');
const workspace=path.resolve(repo,'..');
const queue=path.join(process.env.LOCALAPPDATA,'ChatLocalWork','runtime','windows-runner','rea-hub-integration');
fs.mkdirSync(queue,{recursive:true});
const readyFile=path.join(queue,'state','ready.json');
if(fs.existsSync(readyFile)){
  try{const ready=JSON.parse(fs.readFileSync(readyFile,'utf8'));process.kill(ready.pid,0);console.log('ALREADY_RUNNING_PID='+ready.pid);process.exit(0);}catch{}
}
const out=fs.openSync(path.join(queue,'integration-runner.log'),'a');
const runner=spawn(process.execPath,[path.join(repo,'windows-runner','runner.cjs'),'--workspace-root',workspace,'--queue-dir',queue,'--config',path.join(repo,'windows-runner','runner-config.json')],{cwd:workspace,windowsHide:true,detached:true,stdio:['ignore',out,out]});
runner.unref();fs.closeSync(out);console.log('INTEGRATION_RUNNER_PID='+runner.pid);console.log('QUEUE='+queue);
