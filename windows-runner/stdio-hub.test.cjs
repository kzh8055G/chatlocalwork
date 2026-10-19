'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const os=require('node:os');
const stdio=require('./stdio-hub.cjs');

test('approved Windows stdio profile discovers, calls and blocks tools',async t=>{
  if(process.platform!=='win32'){t.skip('Windows host only');return;}
  const prev=process.env.LOCALAPPDATA;
  const temp=fs.mkdtempSync(path.join(os.tmpdir(),'localwork-stdio-test-'));
  const workspace=path.resolve(__dirname,'..','..');
  const config=path.join(temp,'ChatLocalWork','config','mcp-servers.json');
  const privateFile=path.join(temp,'ChatLocalWork','private','mcp-stdio-profiles.json');
  fs.mkdirSync(path.dirname(config),{recursive:true});
  fs.mkdirSync(path.dirname(privateFile),{recursive:true});
  const reg={version:1,servers:[{id:'mock',name:'Mock',transport:'windowsStdio',enabled:true,allowedTools:['ping']}]};
  const profile={id:'mock',approved:true,command:process.execPath,args:[path.join(__dirname,'mock-stdio-server.test.cjs')],cwd:workspace,envNames:[],allowedTools:['ping','dangerous']};
  const save=()=>{fs.writeFileSync(config,JSON.stringify(reg));fs.writeFileSync(privateFile,JSON.stringify({version:1,servers:[profile]}));};
  process.env.LOCALAPPDATA=temp;
  try {
    save();
    const tools=await stdio.handle({server:'mock',operation:'listTools'},{workspaceRoot:workspace});
    assert.deepEqual(tools.tools.map(x=>x.name),['ping']);
    const result=await stdio.handle({server:'mock',operation:'callTool',tool:'ping',arguments:{word:'hello'}},{workspaceRoot:workspace});
    assert.match(JSON.stringify(result),/ping:hello:\d+/);
    const firstPid=JSON.stringify(result).match(/ping:hello:(\d+)/)[1];
    const result2=await stdio.handle({server:'mock',operation:'callTool',tool:'ping',arguments:{word:'again'}},{workspaceRoot:workspace});
    assert.match(JSON.stringify(result2),new RegExp(`ping:again:${firstPid}`));
    await assert.rejects(()=>stdio.handle({server:'mock',operation:'callTool',tool:'dangerous',arguments:{}},{workspaceRoot:workspace}),/not approved/);
    reg.servers[0].enabled=false;save();
    await assert.rejects(()=>stdio.handle({server:'mock',operation:'listTools'},{workspaceRoot:workspace}),/not approved/);
    reg.servers[0].enabled=true;profile.approved=false;save();
    await assert.rejects(()=>stdio.handle({server:'mock',operation:'listTools'},{workspaceRoot:workspace}),/Manager approval/);
    profile.approved=true;profile.cwd='C:\\Windows';save();
    await assert.rejects(()=>stdio.handle({server:'mock',operation:'listTools'},{workspaceRoot:workspace}),/inside Workspace/);
    profile.cwd=workspace;profile.command='C:\\Windows\\System32\\cmd.exe';save();
    await assert.rejects(()=>stdio.handle({server:'mock',operation:'listTools'},{workspaceRoot:workspace}),/Blocked MCP stdio executable/);
  } finally {
    stdio.shutdown();
    if(prev===undefined)delete process.env.LOCALAPPDATA;else process.env.LOCALAPPDATA=prev;
    fs.rmSync(temp,{recursive:true,force:true});
  }
});
