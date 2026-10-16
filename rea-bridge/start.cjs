'use strict';
// Start only a loopback MCP proxy. Public exposure requires an independently
// configured HTTPS + OAuth security gateway and is intentionally not automatic.
const fs = require('node:fs');
const path = require('node:path');
const { spawn } = require('node:child_process');

const ghidra = process.env.GHIDRA_INSTALL_DIR;
const javaHome = process.env.JAVA_HOME;
const apiKeyFile = process.env.REA_BRIDGE_API_KEY_FILE;
if (!ghidra || !fs.existsSync(path.join(ghidra, 'Ghidra', 'application.properties')) ||
    !javaHome || !fs.existsSync(path.join(javaHome, 'bin', 'java.exe'))) {
  throw new Error('GHIDRA_INSTALL_DIR and JAVA_HOME must point to installed Windows providers.');
}
if (!apiKeyFile || !fs.existsSync(apiKeyFile)) {
  throw new Error('REA_BRIDGE_API_KEY_FILE is required. Never expose an unauthenticated endpoint.');
}
const port = Number(process.env.REA_BRIDGE_PORT || 4188);
if (!Number.isInteger(port) || port < 1024 || port > 65535) throw new Error('Invalid bridge port');
const npx = process.env.REA_NPX_CMD || 'npx';
const cli = path.resolve(__dirname, 'node_modules', 'supergateway', 'dist', 'index.js');
if (!fs.existsSync(cli)) throw new Error('Run npm install in rea-bridge first');
const stdio = `cmd.exe /d /c "${npx}" -y rea-agents@6.5.0 mcp`;
// Supergateway binding must be verified before exposure; API key required.
const child = spawn(process.execPath, [cli,
  '--stdio', stdio,
  '--outputTransport', 'streamableHttp',
  '--stateful',
  '--port', String(port),
  '--host', '127.0.0.1',
  '--streamableHttpPath', '/mcp',
  '--apiKeyFile', apiKeyFile,
], { env: { ...process.env, REA_ANALYSIS_PROVIDER: 'ghidra' },
     cwd: __dirname, stdio: 'inherit', shell: false, windowsHide: true });
child.on('exit', code => { process.exitCode = code || 0; });
