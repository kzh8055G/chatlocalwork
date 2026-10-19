'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { StdioMcpClient } = require('./stdio-mcp-client.cjs');

test('published Everything MCP: stdio initialization, echo and sum', { timeout: 90000 }, async t => {
  if (process.platform !== 'win32') {
    t.skip('Windows integration test');
    return;
  }
  const pkgRoot = path.join(process.env.LOCALAPPDATA || '', 'ChatLocalWork', 'tools',
    'mcp-everything', 'node_modules', '@modelcontextprotocol', 'server-everything');
  const entry = path.join(pkgRoot, 'dist', 'index.js');
  if (!fs.existsSync(entry)) {
    t.skip('Pin @modelcontextprotocol/server-everything@1.0.0 before running');
    return;
  }
  const manifest = JSON.parse(fs.readFileSync(path.join(pkgRoot, 'package.json'), 'utf8'));
  assert.equal(manifest.version, '1.0.0');
  const keys = ['SystemRoot','WINDIR','PATH','PATHEXT','TEMP','TMP','APPDATA','LOCALAPPDATA','USERPROFILE'];
  const env = Object.fromEntries(keys.filter(key => process.env[key] !== undefined)
    .map(key => [key, process.env[key]]));
  const client = new StdioMcpClient({
    command: process.execPath,
    args: [entry, 'stdio'],
    cwd: path.resolve(__dirname, '..'),
    env,
  });
  try {
    const result = await client.listTools();
    const names = result.tools.map(tool => tool.name);
    assert.ok(names.includes('echo'), 'Everything MCP lacks echo');
    assert.ok(names.includes('get-sum'), 'Everything MCP lacks get-sum');
    const echoed = await client.callTool('echo', {message:'LocalWork real MCP test'});
    assert.notEqual(echoed.isError, true);
    assert.match(JSON.stringify(echoed), /LocalWork real MCP test/);
    const sum = await client.callTool('get-sum', {a:17,b:25});
    assert.notEqual(sum.isError, true);
    assert.match(JSON.stringify(sum), /42/);
    console.log('EVERYTHING_STDIO_SMOKE_PASS', names.length);
  } finally {
    client.dispose();
  }
});
