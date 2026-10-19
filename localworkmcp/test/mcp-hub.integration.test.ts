import { describe, it, expect } from 'vitest';
import { createServer } from 'node:http';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StreamableHTTPServerTransport } from '@modelcontextprotocol/sdk/server/streamableHttp.js';
import { loadConfig } from '../src/config.js';
import { InMemoryTransport } from '@modelcontextprotocol/sdk/inMemory.js';
import * as z from 'zod/v4';

describe('MCP hub end-to-end',()=>{
 it('discovers a server, lists tools, forwards permitted call, rejects blocked call',async()=>{
  const dir=await mkdtemp(path.join(tmpdir(),'mcp-hub-integration-'));
  const provider=new McpServer({name:'test-provider',version:'1.0'});
  provider.registerTool('inspect',{inputSchema:{value:z.string()}},async ({value})=>({content:[{type:'text' as const,text:'inspection:'+value}]}));
  provider.registerTool('dangerous',{inputSchema:{}},async()=>({content:[{type:'text' as const,text:'DANGEROUS'}]}));
  const http=createServer(async (req,res)=>{
    const transport=new StreamableHTTPServerTransport({sessionIdGenerator:undefined,enableJsonResponse:true});
    const service=new McpServer({name:'test-provider',version:'1.0'});
    service.registerTool('inspect',{inputSchema:{value:z.string()}},async ({value})=>({content:[{type:'text' as const,text:'inspection:'+value}]}));
    service.registerTool('dangerous',{inputSchema:{}},async()=>({content:[{type:'text' as const,text:'DANGEROUS'}]}));
    await service.connect(transport);
    await transport.handleRequest(req,res);
    res.once('close',()=>void service.close());
  });
  await new Promise<void>(resolve=>http.listen(0,'127.0.0.1',resolve));
  const port=(http.address() as {port:number}).port;
  try {
    const file=path.join(dir,'registry.json');
    await writeFile(file,JSON.stringify({version:1,servers:[{id:'mock',name:'Mock',enabled:true,transport:'streamableHttp',url:`http://127.0.0.1:${port}/mcp`,allowedTools:['inspect']}]}));
    process.env.MCP_HUB_REGISTRY_FILE=file;
    // Dynamic import avoids capturing the registry path before this test's setup.
    const {registerHubTools}=await import('../src/mcp-hub.js');
    const hub=new McpServer({name:'hub',version:'1.0'});registerHubTools(hub, loadConfig({MCP_AUTH_TOKEN:"test-secret"}, dir));
    const client=new Client({name:'test-client',version:'1.0'});
    const [local,remote]=InMemoryTransport.createLinkedPair();
    await hub.connect(remote);await client.connect(local);
    try {
      const servers=await client.callTool({name:'mcp_list_servers',arguments:{}});
      expect(JSON.stringify(servers)).toContain('mock');
      const list=await client.callTool({name:'mcp_list_tools',arguments:{server:'mock'}});
      expect(JSON.stringify(list)).toContain('inspect');
      expect(JSON.stringify(list)).not.toContain('dangerous');
      const result=await client.callTool({name:'mcp_call_tool',arguments:{server:'mock',tool:'inspect',arguments:{value:'sample'}}});
      expect(JSON.stringify(result)).toContain('inspection:sample');
      const denied=await client.callTool({name:'mcp_call_tool',arguments:{server:'mock',tool:'dangerous',arguments:{}}});
      expect(JSON.stringify(denied)).not.toContain('DANGEROUS');
    }finally{await client.close();await hub.close();}
  }finally{delete process.env.MCP_HUB_REGISTRY_FILE;await new Promise<void>(resolve=>http.close(()=>resolve()));await rm(dir,{recursive:true,force:true});}
 });
});
