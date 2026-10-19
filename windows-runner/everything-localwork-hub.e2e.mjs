import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { pathToFileURL } from 'node:url';

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

async function main() {
  if (process.platform !== 'win32') throw new Error('Windows only');

  const repo = path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\//,'')), '..');
  const workspace = path.resolve(repo, '..');
  const installedEntry = path.join(process.env.LOCALAPPDATA || '', 'ChatLocalWork', 'tools',
    'mcp-everything', 'node_modules', '@modelcontextprotocol', 'server-everything', 'dist', 'index.js');
  if (!fs.existsSync(installedEntry)) throw new Error('Everything MCP 1.0.0 is not installed');

  const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'localwork-hub-real-mcp-'));
  const queue = path.join(temp, 'queue');
  const registryFile = path.join(temp, 'registry.json');
  const dataRoot = path.join(temp, 'ChatLocalWork');
  const profilesFile = path.join(dataRoot, 'private', 'mcp-stdio-profiles.json');
  const hostRegistryFile = path.join(dataRoot, 'config', 'mcp-servers.json');

  const registry = {version:1,servers:[{
    id:'everything_test',name:'Everything MCP Validation',transport:'windowsStdio',
    enabled:true,allowedTools:['echo','get-sum']
  }]};
  fs.mkdirSync(path.dirname(hostRegistryFile),{recursive:true});
  fs.mkdirSync(path.dirname(profilesFile),{recursive:true});
  fs.writeFileSync(registryFile,JSON.stringify(registry));
  fs.writeFileSync(hostRegistryFile,JSON.stringify(registry));
  fs.writeFileSync(profilesFile,JSON.stringify({version:1,servers:[{
    id:'everything_test',approved:true,command:process.execPath,args:[installedEntry,'stdio'],
    cwd:workspace,envNames:[],allowedTools:['echo','get-sum']
  }]}));

  const runner = spawn(process.execPath,[
    path.join(repo,'windows-runner','runner.cjs'),
    '--workspace-root',workspace,
    '--queue-dir',queue,
    '--config',path.join(repo,'windows-runner','runner-config.json')
  ],{
    cwd:repo,
    env:{...process.env,LOCALAPPDATA:temp},
    windowsHide:true,
    stdio:['ignore','pipe','pipe']
  });

  let runnerLog='';
  runner.stdout.on('data',b=>runnerLog+=b.toString());
  runner.stderr.on('data',b=>runnerLog+=b.toString());

  try {
    for(let i=0;i<100;i++){
      if(fs.existsSync(path.join(queue,'state','ready.json')))break;
      await sleep(100);
    }
    if(!fs.existsSync(path.join(queue,'state','ready.json')))
      throw new Error('Windows Runner did not become ready: '+runnerLog.slice(-900));

    process.env.MCP_HUB_REGISTRY_FILE = registryFile;
    process.env.WINDOWS_RUNNER_QUEUE_DIR = queue;

    const hub = await import(pathToFileURL(path.join(repo,'localworkmcp','dist','src','mcp-hub.js')).href);
    const configModule = await import(pathToFileURL(path.join(repo,'localworkmcp','dist','src','config.js')).href);
    const { Client } = await import(pathToFileURL(path.join(repo,'localworkmcp','node_modules','@modelcontextprotocol','sdk','dist','esm','client','index.js')).href);
    const { McpServer } = await import(pathToFileURL(path.join(repo,'localworkmcp','node_modules','@modelcontextprotocol','sdk','dist','esm','server','mcp.js')).href);
    const { InMemoryTransport } = await import(pathToFileURL(path.join(repo,'localworkmcp','node_modules','@modelcontextprotocol','sdk','dist','esm','inMemory.js')).href);

    const server = new McpServer({name:'everything-e2e',version:'1.0.0'});
    hub.registerHubTools(server,configModule.loadConfig({MCP_ALLOW_NO_AUTH:'true'},workspace));
    const client = new Client({name:'everything-e2e-client',version:'1.0.0'});
    const [a,b] = InMemoryTransport.createLinkedPair();
    await server.connect(b);
    await client.connect(a);

    try {
      const listed = await client.callTool({name:'mcp_list_tools',arguments:{server:'everything_test'}},undefined,{timeout:60000});
      const listedText = listed.content?.find(x=>x.type==='text')?.text || '';
      const tools = JSON.parse(listedText);
      const names = tools.map(x=>x.name).sort();
      if(JSON.stringify(names)!==JSON.stringify(['echo','get-sum'])) throw new Error('Unexpected allowed tools: '+JSON.stringify(names));

      const echoed = await client.callTool({name:'mcp_call_tool',arguments:{
        server:'everything_test',tool:'echo',arguments:{message:'LocalWork full hub E2E'}
      }},undefined,{timeout:60000});
      if(echoed.isError || !JSON.stringify(echoed).includes('LocalWork full hub E2E')) throw new Error('Echo failed');

      const sum = await client.callTool({name:'mcp_call_tool',arguments:{
        server:'everything_test',tool:'get-sum',arguments:{a:17,b:25}
      }},undefined,{timeout:60000});
      if(sum.isError || !JSON.stringify(sum).includes('42')) throw new Error('Sum failed');

      let denied=false;
      try {
        const rejected = await client.callTool({name:'mcp_call_tool',arguments:{
          server:'everything_test',tool:'longRunningOperation',arguments:{}
        }},undefined,{timeout:30000});
        denied = rejected.isError === true;
      } catch {
        denied=true;
      }
      if(!denied) throw new Error('Unapproved tool was not rejected');

      console.log('EVERYTHING_LOCALWORK_HUB_E2E_PASS');
    } finally {
      await client.close();
      await server.close();
    }
  } finally {
    runner.kill();
    await sleep(300);
    fs.rmSync(temp,{recursive:true,force:true});
  }
}

main().catch(error=>{
  console.error('EVERYTHING_LOCALWORK_HUB_E2E_FAIL',error?.stack||String(error));
  process.exitCode=1;
});
