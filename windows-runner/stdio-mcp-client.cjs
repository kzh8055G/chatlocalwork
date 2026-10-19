'use strict';
const { spawn, spawnSync } = require('node:child_process');
const readline = require('node:readline');

// One MCP stdio process per server ID. Messages use the MCP SDK newline JSON framing.
class StdioMcpClient {
  constructor(profile) {
    this.profile = profile;
    this.child = null;
    this.connecting = null;
    this.pending = new Map();
    this.serial = 0;
  }
  dispose(error = new Error('MCP stdio session closed')) {
    const currentProcess = this.child;
    this.child = null;
    this.connecting = null;
    for (const waiter of this.pending.values()) { clearTimeout(waiter.timer); waiter.reject(error); }
    this.pending.clear();
    if (currentProcess && !currentProcess.killed) {
      // Stop the child process tree as well when profiles are revoked.
      if (process.platform === 'win32' && currentProcess.pid) {
        try { spawnSync('taskkill.exe',['/PID',String(currentProcess.pid),'/T','/F'], {shell:false,windowsHide:true,stdio:'ignore',timeout:10000}); } catch {}
      } else currentProcess.kill();
    }
  }
  async connect() {
    if (this.child && this.child.exitCode === null && !this.connecting) return;
    if (this.connecting) return this.connecting;
    this.connecting = (async () => {
      const profile = this.profile;
      const instance = spawn(profile.command, profile.args, {
        cwd: profile.cwd, shell: false, windowsHide: true,
        stdio: ['pipe', 'pipe', 'pipe'], env: profile.env,
      });
      this.child = instance;
      const lines = readline.createInterface({ input: instance.stdout, crlfDelay: Infinity });
      lines.on('line', line => {
        if (line.length > 8 * 1024 * 1024) { this.dispose(new Error('MCP stdio response exceeded limit')); return; }
        let message;
        try { message = JSON.parse(line); } catch { return; }
        if (message.id === undefined) return;
        const waiter = this.pending.get(message.id);
        if (!waiter) return;
        this.pending.delete(message.id);
        clearTimeout(waiter.timer);
        if (message.error) waiter.reject(new Error(`MCP server error: ${JSON.stringify(message.error).slice(0,1024)}`));
        else waiter.resolve(message.result);
      });
      instance.stderr.on('data', () => {}); // Do not disclose third-party logs or credentials.
      instance.on('error', err => this.dispose(err));
      instance.on('exit', () => this.dispose(new Error('MCP stdio process exited')));
      const handshake = await this.request('initialize', {
        protocolVersion: '2025-06-18', capabilities: {},
        clientInfo: { name: 'chatlocalwork-windows-stdio', version: '0.2.0' },
      }, 60000);
      if (!handshake?.serverInfo) throw new Error('Invalid MCP initialize response');
      instance.stdin.write(JSON.stringify({jsonrpc:'2.0',method:'notifications/initialized'})+'\n');
    })();
    try { await this.connecting; }
    catch (err) { this.dispose(err); throw err; }
    finally { this.connecting = null; }
  }
  request(method, params, timeoutMs=120000) {
    const child=this.child;
    if (!child || child.exitCode !== null) return Promise.reject(new Error('MCP stdio process unavailable'));
    const id=++this.serial;
    return new Promise((resolve,reject) => {
      const timer=setTimeout(() => {
        this.pending.delete(id);
        this.dispose(new Error('MCP stdio request timed out'));
        reject(new Error('MCP stdio request timed out'));
      },timeoutMs);
      this.pending.set(id,{resolve,reject,timer});
      child.stdin.write(JSON.stringify({jsonrpc:'2.0',id,method,params})+'\n',error=>{
        if (!error) return;
        const pending=this.pending.get(id);
        if (pending) {clearTimeout(timer);this.pending.delete(id);reject(error);}
      });
    });
  }
  async listTools() {
    await this.connect();
    const tools=[];
    let cursor;
    for (let page=0; page<10; page++) {
      const response=await this.request('tools/list',cursor?{cursor}:{},45000);
      if(!Array.isArray(response?.tools)) throw new Error('Invalid MCP tools/list result');
      tools.push(...response.tools);
      if (!response.nextCursor) return {tools};
      cursor=response.nextCursor;
    }
    throw new Error('MCP tools/list exceeded pagination limit');
  }
  async callTool(tool,args) {
    await this.connect();
    return this.request('tools/call',{name:tool,arguments:args},120000);
  }
}
module.exports={StdioMcpClient};
