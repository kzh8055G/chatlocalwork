'use strict';
// One-time host-local REA provider binding; never store host paths or secrets in Git.
const fs=require('node:fs');const path=require('node:path');
const [ghidraInstallDir,javaHome,npxCli]=process.argv.slice(2);
if (![ghidraInstallDir,javaHome,npxCli].every(p=>typeof p==='string'&&path.isAbsolute(p))) throw Error('Usage: node configure-windows-hub.cjs <ghidra_dir> <jdk_dir> <npx-cli.js>');
for(const p of [path.join(ghidraInstallDir,'Ghidra','application.properties'),path.join(javaHome,'bin','java.exe'),npxCli])if(!fs.existsSync(p)) throw Error('Missing required REA component: '+p);
if(!process.env.LOCALAPPDATA)throw Error('LOCALAPPDATA missing');
const dir=path.join(process.env.LOCALAPPDATA,'ChatLocalWork','config');fs.mkdirSync(dir,{recursive:true});
const target=path.join(dir,'rea-provider.json');
const content=JSON.stringify({ghidraInstallDir,javaHome,npxCli},null,2);
const tmp=target+'.tmp';fs.writeFileSync(tmp,content);fs.renameSync(tmp,target);
console.log('REA_PROVIDER_CONFIGURED='+target);
