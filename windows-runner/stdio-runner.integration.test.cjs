'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const os=require('node:os');
const {spawn}=require('node:child_process');
const crypto=require('node:crypto');
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));

test('Windows Runner queue forwards an approved MCP stdio server',async t=>{
  if(process.platform!=='win32'){t.skip('Windows only');return;}
  const temp=fs.mkdtempSync(path.join(os.tmpdir(),'localwork-runner-stdio-'));
  const queue=path.join(temp,'queue');
  const workspace=path.resolve(__dirname,'..','..');
  const config=path.join(temp,'ChatLocalWork','config','mcp-servers.json');
  const profiles=path.join(temp,'ChatLocalWork','private','mcp-stdio-profiles.json');
  fs.mkdirSync(path.dirname(config),{recursive:true});
  fs.mkdirSync(path.dirname(profiles),{recursive:true});
  fs.writeFileSync(config,JSON.stringify({version:1,servers:[{id:'mock',name:'Test',transport:'windowsStdio',enabled:true,allowedTools:['ping']}]}));
  fs.writeFileSync(profiles,JSON.stringify({version:1,servers:[{id:'mock',approved:true,command:process.execPath,args:[path.join(__dirname,'mock-stdio-server.test.cjs')],cwd:workspace,envNames:[],allowedTools:['ping']}]}));
  const runner=spawn(process.execPath,[path.join(__dirname,'runner.cjs'),'--workspace-root',workspace,'--queue-dir',queue,'--config',path.join(__dirname,'runner-config.json')],{
    cwd:workspace,env:{...process.env,LOCALAPPDATA:temp},windowsHide:true,stdio:['ignore','pipe','pipe']
  });
  let output='';runner.stdout.on('data',b=>output+=b.toString());runner.stderr.on('data',b=>output+=b.toString());
  async function call(tool,arguments_) {
    const id=crypto.randomUUID();
    fs.writeFileSync(path.join(queue,'requests',id+'.json'),JSON.stringify({id,kind:'mcpHub',server:'mock',operation:tool?'callTool':'listTools',tool,arguments:arguments_||{}}));
    for(let i=0;i<150;i++){
      const resultFile=path.join(queue,'responses',id+'.json');
      if(fs.existsSync(resultFile))return JSON.parse(fs.readFileSync(resultFile,'utf8'));
      await sleep(100);
    }
    throw new Error('Windows Runner request timed out: '+output.slice(-900));
  }
  try {
    for(let i=0;i<80;i++){if(fs.existsSync(path.join(queue,'state','ready.json')))break;await sleep(100);}
    assert.ok(fs.existsSync(path.join(queue,'state','ready.json')),'Runner did not start');
    const list=await call();assert.equal(list.ok,true,JSON.stringify(list));assert.deepEqual(list.result.tools.map(x=>x.name),['ping']);
    const invoked=await call('ping',{word:'via-queue'});assert.equal(invoked.ok,true,JSON.stringify(invoked));
    assert.match(JSON.stringify(invoked.result),/ping:via-queue/);
    const denied=await call('dangerous',{});assert.equal(denied.ok,false);
    assert.match(denied.error,/not approved/);
    console.log('WINDOWS_STDIO_QUEUE_PASS');
  }finally{
    runner.kill();
    await sleep(300);
    fs.rmSync(temp,{recursive:true,force:true});
  }
});
