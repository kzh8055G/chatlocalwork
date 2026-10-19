'use strict';

const fs = require('node:fs');
const path = require('node:path');

function readJson(file) {
  try { return JSON.parse(fs.readFileSync(file, 'utf8')); }
  catch (error) {
    return { _error: error && error.code ? error.code : String(error) };
  }
}

function print(label, value) {
  process.stdout.write(label + '=' + JSON.stringify(value) + '\n');
}

const localAppData = process.env.LOCALAPPDATA;
if (!localAppData) {
  console.error('LOCALAPPDATA is not set');
  process.exitCode = 2;
  return;
}

const root = path.join(localAppData, 'ChatLocalWork');
const registry = readJson(path.join(root, 'config', 'mcp-servers.json'));
const profiles = readJson(path.join(root, 'private', 'mcp-stdio-profiles.json'));
const ready = readJson(path.join(root, 'runtime', 'windows-runner', 'state', 'ready.json'));

print('REGISTRY', Array.isArray(registry.servers)
  ? registry.servers.map(s => ({
      id: s.id,
      transport: s.transport,
      enabled: s.enabled === true,
      allowedToolCount: Array.isArray(s.allowedTools) ? s.allowedTools.length : 0,
    }))
  : registry);

print('STDIO_PROFILES', Array.isArray(profiles.servers)
  ? profiles.servers.map(s => ({
      id: s.id,
      approved: s.approved === true,
      allowedToolCount: Array.isArray(s.allowedTools) ? s.allowedTools.length : 0,
    }))
  : profiles);

if (ready && !ready._error) {
  const heartbeatMs = Date.parse(ready.heartbeatAt || '');
  print('RUNNER', {
    ok: ready.ok === true,
    pid: ready.pid,
    busy: ready.busy === true,
    heartbeatAgeMs: Number.isFinite(heartbeatMs) ? Date.now() - heartbeatMs : null,
  });
} else {
  print('RUNNER', ready);
}
