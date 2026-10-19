'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const os=require('node:os');
const {spawn}=require('node:child_process');
const crypto=require('node:crypto');
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));

const entry=process.argv[2];

test('Windows Runner queue forwards a real approved stdio MCP server',{timeout:90000},async t=>{
  if(process.platform!=='win32'){t.skip('Windows only');return;}
  if(!entry){t.skip('Pass an MCP server entry path');return;}
  if(!path.isAbsolute(entry)||!fs.existsSync(entry))throw new Error('MCP server entry must be an existing absolute path');

  const temp=fs.mkdtempSync(path.join(os.tmpdir(),'localwork-runner-real-mcp-'));
  const queue=path.join(temp,'queue');
  const workspace=path.resolve(__dirname,'..','..');
  const config=path.join(temp,'ChatLocalWork','config','mcp-servers.json');
  const profiles=path.join(temp,'ChatLocalWork','private','mcp-stdio-profiles.json');
  fs.mkdirSync(path.dirname(config),{recursive:true});
  fs.mkdirSync(path.dirname(profiles),{recursive:true});
  fs.writeFileSync(config,JSON.stringify({version:1,servers:[{
    id:'realtest',name:'Real MCP',transport:'windowsStdio',enabled:true,allowedTools:['echo','get-sum']
  }]}));
  fs.writeFileSync(profiles,JSON.stringify({version:1,servers:[{
    id:'realtest',approved:true,command:process.execPath,args:[entry,'stdio'],
    cwd:workspace,envNames:[],allowedTools:['echo','get-sum']
  }]}));

  const runner=spawn(process.execPath,[
    path.join(__dirname,'runner.cjs'),'--workspace-root',workspace,'--queue-dir',queue,
    '--config',path.join(__dirname,'runner-config.json')
  ],{cwd:workspace,env:{...process.env,LOCALAPPDATA:temp},windowsHide:true,stdio:['ignore','pipe','pipe']});

  let output='';
  runner.stdout.on('data',b=>output+=b.toString());
  runner.stderr.on('data',b=>output+=b.toString());

  async function call(tool,args){
    const id=crypto.randomUUID();
    fs.writeFileSync(path.join(queue,'requests',id+'.json'),JSON.stringify({
      id,kind:'mcpHub',server:'realtest',operation:tool?'callTool':'listTools',tool,arguments:args||{}
    }));
    for(let i=0;i<300;i++){
      const resultFile=path.join(queue,'responses',id+'.json');
      if(fs.existsSync(resultFile))return JSON.parse(fs.readFileSync(resultFile,'utf8'));
      await sleep(100);
    }
    throw new Error('Windows Runner request timed out: '+output.slice(-900));
  }

  try{
    for(let i=0;i<100;i++){
      if(fs.existsSync(path.join(queue,'state','ready.json')))break;
      await sleep(100);
    }
    assert.ok(fs.existsSync(path.join(queue,'state','ready.json')),'Runner did not start');
    const list=await call();
    assert.equal(list.ok,true,JSON.stringify(list));
    assert.deepEqual(list.result.tools.map(x=>x.name).sort(),['echo','get-sum']);

    const echo=await call('echo',{message:'LocalWork queue real MCP test'});
    assert.equal(echo.ok,true,JSON.stringify(echo));
    assert.match(JSON.stringify(echo.result),/LocalWork queue real MCP test/);

    const sum=await call('get-sum',{a:17,b:25});
    assert.equal(sum.ok,true,JSON.stringify(sum));
    assert.match(JSON.stringify(sum.result),/42/);

    const denied=await call('longRunningOperation',{});
    assert.equal(denied.ok,false);
    assert.match(denied.error,/not approved/);
    console.log('EVERYTHING_WINDOWS_RUNNER_PASS');
  }finally{
    runner.kill();
    await sleep(300);
    fs.rmSync(temp,{recursive:true,force:true});
  }
});
