'use strict';

const fs=require('node:fs');
const path=require('node:path');

const action=process.argv[2];
if(!['apply','restore'].includes(action)){
  throw new Error('usage: node EverythingLiveValidationConfig.cjs apply|restore');
}

const localAppData=process.env.LOCALAPPDATA;
if(!localAppData) throw new Error('LOCALAPPDATA is not set');

const root=path.join(localAppData,'ChatLocalWork');
const registryFile=path.join(root,'config','mcp-servers.json');
const profilesFile=path.join(root,'private','mcp-stdio-profiles.json');
const backupDir=path.join(root,'runtime','diagnostics','everything-live-validation');
const registryBackup=path.join(backupDir,'mcp-servers.json');
const profilesBackup=path.join(backupDir,'mcp-stdio-profiles.json');
const profilesMissing=path.join(backupDir,'profiles-was-missing');

function writeJson(file,value){
  fs.mkdirSync(path.dirname(file),{recursive:true});
  const tmp=file+'.tmp';
  fs.writeFileSync(tmp,JSON.stringify(value,null,2)+'\n','utf8');
  fs.renameSync(tmp,file);
}

if(action==='apply'){
  fs.mkdirSync(backupDir,{recursive:true});
  fs.copyFileSync(registryFile,registryBackup);
  if(fs.existsSync(profilesFile)){
    fs.copyFileSync(profilesFile,profilesBackup);
    if(fs.existsSync(profilesMissing)) fs.rmSync(profilesMissing);
  }else{
    fs.writeFileSync(profilesMissing,'missing\n');
  }

  const registry=JSON.parse(fs.readFileSync(registryFile,'utf8'));
  registry.servers=(registry.servers||[]).filter(s=>s.id!=='everything_test');
  registry.servers.push({
    id:'everything_test',
    name:'Everything MCP Validation',
    enabled:true,
    transport:'windowsStdio',
    allowedTools:['echo','get-sum']
  });
  writeJson(registryFile,registry);

  const entry=path.join(root,'tools','mcp-everything','node_modules','@modelcontextprotocol','server-everything','dist','index.js');
  if(!fs.existsSync(entry)) throw new Error('Everything MCP entry not found');

  const profiles=fs.existsSync(profilesFile)
    ? JSON.parse(fs.readFileSync(profilesFile,'utf8'))
    : {version:1,servers:[]};
  profiles.version=1;
  profiles.servers=(profiles.servers||[]).filter(s=>s.id!=='everything_test');
  profiles.servers.push({
    id:'everything_test',
    approved:true,
    command:process.execPath,
    args:[entry,'stdio'],
    cwd:path.resolve(__dirname,'..','..'),
    envNames:[],
    allowedTools:['echo','get-sum']
  });
  writeJson(profilesFile,profiles);
  console.log('EVERYTHING_LIVE_VALIDATION_APPLIED');
}else{
  if(!fs.existsSync(registryBackup)) throw new Error('Registry backup missing');
  fs.copyFileSync(registryBackup,registryFile);
  if(fs.existsSync(profilesBackup)){
    fs.copyFileSync(profilesBackup,profilesFile);
  }else if(fs.existsSync(profilesMissing) && fs.existsSync(profilesFile)){
    fs.rmSync(profilesFile);
  }
  console.log('EVERYTHING_LIVE_VALIDATION_RESTORED');
}
