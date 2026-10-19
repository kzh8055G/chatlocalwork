import { readFile, writeFile, rename, rm, mkdir } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import path from 'node:path';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import * as z from 'zod/v4';
import type { AppConfig } from './config.js';
import { TOOL_ANNOTATIONS, toolAuthMetadata } from './tool-metadata.js';

export interface HubServer { id: string; name: string; enabled: boolean; transport: 'streamableHttp' | 'windowsRea'; url?: string; allowedTools?: string[]; }
export const registryFile = process.env.MCP_HUB_REGISTRY_FILE || '/shared/.chatlocalwork/mcp-servers.json';
const idPattern = /^[a-z][a-z0-9_-]{0,47}$/;
export async function loadHubRegistry(file = registryFile): Promise<HubServer[]> {
  let raw: string;
  try { raw = await readFile(file, 'utf8'); }
  catch (err) { if ((err as NodeJS.ErrnoException).code === 'ENOENT') return []; throw err; }
  const parsed: unknown = JSON.parse(raw);
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw Error('Invalid MCP hub registry');
  const obj = parsed as { version?: unknown; servers?: unknown };
  if (obj.version !== 1 || !Array.isArray(obj.servers) || obj.servers.length > 32) throw Error('Invalid MCP hub registry schema');
  const seen = new Set<string>();
  return obj.servers.map((v: unknown) => {
    const s = v as Partial<HubServer>;
    if (!s || typeof s !== 'object' || !idPattern.test(s.id || '') || typeof s.name !== 'string' || s.name.length > 120 || typeof s.enabled !== 'boolean' || !['streamableHttp', 'windowsRea'].includes(s.transport || '') || (s.transport === 'streamableHttp' && typeof s.url !== 'string') || (s.allowedTools !== undefined && (!Array.isArray(s.allowedTools) || !s.allowedTools.every(x => typeof x === 'string')))) throw Error('Invalid MCP hub server entry');
    if (s.transport === 'windowsRea') { if (s.id !== 'rea' || s.url !== undefined) throw Error('Windows REA transport requires id rea without URL'); }
    else {
    const u = new URL(s.url!);
    // Prevent arbitrary SSRF: only explicit local Docker-host / loopback services.
    if (!(['127.0.0.1', 'localhost', 'host.docker.internal'].includes(u.hostname)) || u.protocol !== 'http:' || u.username || u.password || u.hash || u.search) throw Error('MCP hub endpoint must be a local HTTP endpoint');
    }
    if (seen.has(s.id!)) throw Error('Duplicate MCP hub server id');
    seen.add(s.id!);
    return s as HubServer;
  });
}
async function withClient<T>(entry: HubServer, action: (client: Client) => Promise<T>): Promise<T> {
  if (!entry.url) throw Error('MCP endpoint URL missing');
  const client = new Client({ name: 'localwork-mcp-hub', version: '0.1.0' });
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 20000);
  try {
    await client.connect(new StreamableHTTPClientTransport(new URL(entry.url)), { signal: controller.signal, timeout: 20000 });
    return await action(client);
  } finally { clearTimeout(timeout); await client.close().catch(() => undefined); }
}
async function windowsReaRequest(operation: 'listTools' | 'callTool', tool?: string, args?: Record<string,unknown>): Promise<unknown> {
  const root = process.env.WINDOWS_RUNNER_QUEUE_DIR || '/chatlocalwork-runtime/windows-runner';
  const ready = JSON.parse(await readFile(path.join(root, 'state', 'ready.json'),'utf8')) as {ok?:boolean;heartbeatAt?:string};
  if (!ready.ok || Date.now()-Date.parse(ready.heartbeatAt || '')>10000) throw Error('Windows Runner not ready');
  const id=randomUUID(), reqDir=path.join(root,'requests'),resDir=path.join(root,'responses');
  await mkdir(reqDir,{recursive:true});
  const payload={id,kind:'mcpHub',server:'rea',operation,tool,arguments:args||{}};
  const reqPath=path.join(reqDir,id+'.json'),tmpPath=reqPath+'.tmp',resPath=path.join(resDir,id+'.json');
  await writeFile(tmpPath,JSON.stringify(payload));await rename(tmpPath,reqPath);
  try {
    const deadline=Date.now()+125000;
    while(Date.now()<deadline) {
      try {const raw=JSON.parse(await readFile(resPath,'utf8')) as {ok:boolean;error?:string;result?:unknown};if(!raw.ok)throw Error(raw.error || 'Windows REA operation failed');return raw.result;}
      catch(e) {if((e as NodeJS.ErrnoException).code !== 'ENOENT')throw e;}
      await new Promise(resolve=>setTimeout(resolve,200));
    }
    throw Error('Windows REA hub request timed out');
  } finally {await Promise.allSettled([rm(reqPath,{force:true}),rm(resPath,{force:true}),rm(tmpPath,{force:true})]);}
}

export function registerHubTools(server: McpServer, config: AppConfig): void {
  const security = toolAuthMetadata(config);
  server.registerTool('mcp_list_servers', { title: 'List MCP Hub Servers', description: 'List enabled and disabled MCP servers configured in the local hub registry.', inputSchema: {}, annotations: TOOL_ANNOTATIONS.readOnlyClosed, _meta: security }, async () => {
    const entries = await loadHubRegistry();
    return { content: [{ type: 'text' as const, text: JSON.stringify(entries.map(({id,name,enabled,transport}) => ({id,name,enabled,transport}))) }] };
  });
  server.registerTool('mcp_list_tools', { title: 'List MCP Hub Tools', description: 'Get tools and input schemas from an enabled MCP hub server.', inputSchema: { server: z.string().describe('Registered MCP server ID to inspect.') }, annotations: TOOL_ANNOTATIONS.readOnlyClosed, _meta: security }, async ({server: id}) => {
    const entry = (await loadHubRegistry()).find(s => s.id === id && s.enabled);
    if (!entry) throw Error('MCP hub server is not enabled or not found');
    const result = entry.transport === 'windowsRea' ? await windowsReaRequest('listTools') as {tools: Array<{name:string}>} : await withClient(entry, client => client.listTools());
    const tools = entry.allowedTools ? result.tools.filter(tool => entry.allowedTools!.includes(tool.name)) : result.tools;
    return { content: [{ type: 'text' as const, text: JSON.stringify(tools) }] };
  });
  server.registerTool('mcp_call_tool', { title: 'Call MCP Hub Tool', description: 'Call a named tool in an enabled MCP hub server. Only configured allowedTools may be called.', inputSchema: { server: z.string().describe('Registered MCP server ID.'), tool: z.string().describe('Name of the tool to call on the server.'), arguments: z.record(z.string(), z.unknown()).optional().describe('Tool arguments matching its input schema.') }, annotations: TOOL_ANNOTATIONS.destructiveNonIdempotentOpen, _meta: security }, async ({server: id, tool, arguments: args}) => {
    const entry = (await loadHubRegistry()).find(s => s.id === id && s.enabled);
    if (!entry) throw Error('MCP hub server is not enabled or not found');
    if (!entry.allowedTools || !entry.allowedTools.includes(tool)) throw Error('Tool is not approved by the MCP hub allowlist');
    const result = entry.transport === 'windowsRea' ? await windowsReaRequest('callTool', tool, args || {}) as {isError?:boolean} : await withClient(entry, client => client.callTool({name: tool, arguments: args || {}}));
    return { content: [{type:'text' as const,text:JSON.stringify(result)}], isError: result.isError === true };
  });
}
