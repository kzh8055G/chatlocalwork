import { describe, it, expect } from 'vitest';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { loadHubRegistry } from '../src/mcp-hub.js';

describe('MCP hub registry security', () => {
  async function check(value: unknown, reject: boolean) {
    const dir=await mkdtemp(path.join(tmpdir(),'hub-test-'));
    try {
      const file=path.join(dir,'registry.json');await writeFile(file,JSON.stringify(value));
      if(reject) await expect(loadHubRegistry(file)).rejects.toThrow();
      else expect((await loadHubRegistry(file)).length).toBe(1);
    } finally { await rm(dir,{recursive:true,force:true}); }
  }
  const entry={id:'rea',name:'Reverse Engineer Anything',enabled:true,transport:'streamableHttp',url:'http://host.docker.internal:4188/mcp',allowedTools:['list_procedures']};
  it('accepts declared local services',async()=>check({version:1,servers:[entry]},false));
  it('rejects arbitrary remote hosts',async()=>check({version:1,servers:[{...entry,url:'https://evil.example/mcp'}]},true));
  it('rejects host header spoofing and credentials',async()=>check({version:1,servers:[{...entry,url:'http://user@localhost:4188/mcp'}]},true));
  it('rejects duplicate ids',async()=>check({version:1,servers:[entry,entry]},true));
  it('rejects non-object schema',async()=>check({version:1,servers:['bad']},true));
  it('accepts Windows REA transport without network endpoint',async()=>check({version:1,servers:[{id:'rea',name:'REA',enabled:true,transport:'windowsRea',allowedTools:['open_binary']}]},false));
  it('rejects unexpected Windows REA identities',async()=>check({version:1,servers:[{id:'other',name:'Other',enabled:true,transport:'windowsRea'}]},true));
  it('rejects Windows REA URLs',async()=>check({version:1,servers:[{id:'rea',name:'REA',enabled:true,transport:'windowsRea',url:'http://localhost:4188/mcp'}]},true));
});
