const fs = require("fs");
const path = require("path");
const http = require("http");
const https = require("https");
const net = require("net");
const { spawn, spawnSync } = require("child_process");

const CONTAINER = "workmachine";
const DEFAULT_GATEWAY_PORT = 3999;
const MAX_GATEWAY_PORT = 4999;
let gatewayPort = DEFAULT_GATEWAY_PORT;

const scriptRoot = __dirname;
const projectRoot = path.resolve(scriptRoot, "..", "..");
const mcpRoot = path.join(projectRoot, "localworkmcp");
const workspaceRoot = path.dirname(projectRoot);
const composeFile = path.join(mcpRoot, "tunneling", "docker-compose.yml");
const envFile = path.join(mcpRoot, "tunneling", ".env");
const runnerScript = path.join(projectRoot, "windows-runner", "runner.cjs");
const runnerConfig = path.join(projectRoot, "windows-runner", "runner-config.json");
const localAppData = process.env.LOCALAPPDATA;
if (!localAppData) throw new Error("LOCALAPPDATA is not available.");
const runnerQueueDir = path.join(localAppData, "ChatLocalWork", "runtime", "windows-runner");
const runnerContainerQueueDir = "/chatlocalwork-runtime/windows-runner";
const runnerReadyFile = path.join(runnerQueueDir, "state", "ready.json");
const startStateFile = path.join(localAppData, "ChatLocalWork", "runtime", "start-state.json");
const nodeExe = process.execPath;
let rollbackOnFailure = false;

function step(text) {
  console.log("");
  console.log(`==> ${text}`);
}

function run(command, args = [], options = {}) {
  return spawnSync(command, args, {
    cwd: options.cwd,
    encoding: "utf8",
    windowsHide: true,
    stdio: options.inherit ? "inherit" : ["ignore", "pipe", "pipe"],
    timeout: options.timeout,
    env: options.env ? { ...process.env, ...options.env } : process.env,
  });
}

function trimFileTail(file, maxBytes = 4 * 1024 * 1024, keepBytes = 2 * 1024 * 1024) {
  try {
    const stat = fs.statSync(file);
    if (stat.size <= maxBytes) return;

    const fd = fs.openSync(file, "r");
    try {
      const bytesToRead = Math.min(keepBytes, stat.size);
      const buffer = Buffer.alloc(bytesToRead);
      fs.readSync(fd, buffer, 0, bytesToRead, stat.size - bytesToRead);
      let text = buffer.toString("utf8");
      const firstNewLine = text.indexOf("\n");
      if (firstNewLine >= 0) text = text.slice(firstNewLine + 1);
      fs.writeFileSync(file, text, "utf8");
    } finally {
      fs.closeSync(fd);
    }
  } catch {}
}

function sleep(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

async function waitUntil(fn, timeoutMs, intervalMs = 1000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      if (await fn()) return true;
    } catch {}
    await sleep(intervalMs);
  }
  return false;
}

function readEnv(file) {
  const result = {};
  for (const raw of fs.readFileSync(file, "utf8").split(/\r?\n/)) {
    const line = raw.trim();
    if (!line || line.startsWith("#")) continue;
    const index = line.indexOf("=");
    if (index < 0) continue;
    result[line.slice(0, index).trim()] = line.slice(index + 1).trim();
  }
  return result;
}

function request(url, options = {}) {
  return new Promise(resolve => {
    const lib = url.startsWith("https:") ? https : http;
    const body = options.body ? Buffer.from(JSON.stringify(options.body), "utf8") : null;
    const req = lib.request(url, {
      method: options.method || "GET",
      headers: {
        ...(body ? {
          "content-type": "application/json",
          "content-length": body.length,
        } : {}),
        ...(options.headers || {}),
      },
      timeout: options.timeout || 5000,
      rejectUnauthorized: true,
    }, res => {
      const chunks = [];
      res.on("data", c => chunks.push(c));
      res.on("end", () => resolve({
        status: res.statusCode || 0,
        body: Buffer.concat(chunks).toString("utf8"),
      }));
    });
    req.on("timeout", () => {
      req.destroy();
      resolve({ status: 0, body: "" });
    });
    req.on("error", () => resolve({ status: 0, body: "" }));
    if (body) req.write(body);
    req.end();
  });
}

function sameWindowsPath(a, b) {
  return path.resolve(String(a || "")).toLowerCase() === path.resolve(String(b || "")).toLowerCase();
}

function readRunnerReadyState() {
  try {
    return JSON.parse(fs.readFileSync(runnerReadyFile, "utf8"));
  } catch {
    return null;
  }
}

function runnerStateMatches(ready) {
  return Boolean(
    ready?.ok &&
    ready?.runtime === "node" &&
    ready?.executionMode === "direct-process" &&
    sameWindowsPath(ready.queueDir, runnerQueueDir) &&
    sameWindowsPath(ready.workspaceRoot, workspaceRoot)
  );
}

function runnerHeartbeatFresh(ready) {
  const heartbeatMs = Date.parse(String(ready?.heartbeatAt || ""));
  return Number.isFinite(heartbeatMs) && Date.now() - heartbeatMs <= 10000;
}

function runnerReady() {
  const ready = readRunnerReadyState();
  if (!runnerStateMatches(ready) || !runnerHeartbeatFresh(ready)) return false;

  try {
    process.kill(Number(ready.pid), 0);
    return true;
  } catch {
    return false;
  }
}

function stopStaleRunnerIfOwned() {
  const ready = readRunnerReadyState();
  if (!runnerStateMatches(ready)) return;
  if (runnerHeartbeatFresh(ready)) return;

  const pid = Number(ready.pid);
  if (!Number.isInteger(pid) || pid <= 0) return;

  console.log("Windows Runner : stale heartbeat detected, restarting PID " + pid);
  run("taskkill.exe", ["/PID", String(pid), "/T", "/F"], { timeout: 10000 });
}

async function ensureRunner() {
  if (runnerReady()) return;

  stopStaleRunnerIfOwned();
  fs.mkdirSync(path.dirname(runnerReadyFile), { recursive: true });
  try { fs.unlinkSync(runnerReadyFile); } catch {}

  const logDir = path.join(runnerQueueDir, "logs");
  fs.mkdirSync(logDir, { recursive: true });
  const launcherLog = path.join(logDir, "launcher.log");
  trimFileTail(launcherLog);
  const logFd = fs.openSync(launcherLog, "a");

  const child = spawn(nodeExe, [
    runnerScript,
    "--workspace-root", workspaceRoot,
    "--queue-dir", runnerQueueDir,
    "--config", runnerConfig,
  ], {
    cwd: path.dirname(runnerScript),
    detached: true,
    windowsHide: true,
    stdio: ["ignore", logFd, logFd],
  });
  child.unref();
  fs.closeSync(logFd);

  if (!await waitUntil(runnerReady, 20000, 250)) {
    throw new Error("Windows Runner did not become ready.");
  }
}

function currentGitHead() {
  const p = run("git.exe", ["rev-parse", "HEAD"], {
    cwd: projectRoot,
    timeout: 10000,
  });
  return p.status === 0 ? String(p.stdout || "").trim() : null;
}

function gitWorkingTreeClean() {
  const p = run("git.exe", ["status", "--porcelain"], {
    cwd: projectRoot,
    timeout: 10000,
  });
  return p.status === 0 && String(p.stdout || "").trim() === "";
}

function readStartState() {
  try {
    return JSON.parse(fs.readFileSync(startStateFile, "utf8"));
  } catch {
    return null;
  }
}

function writeStartState() {
  fs.mkdirSync(path.dirname(startStateFile), { recursive: true });
  fs.writeFileSync(startStateFile, JSON.stringify({
    gitHead: currentGitHead(),
    gatewayPort,
    startedAt: new Date().toISOString(),
  }, null, 2), "utf8");
}

function dockerReady() {
  const p = run("docker.exe", ["info"], { timeout: 10000 });
  return p.status === 0;
}

async function ensureDocker() {
  if (dockerReady()) return;

  const started = run("docker.exe", ["desktop", "start"], {
    timeout: 120000,
    inherit: true,
  });

  if (started.status !== 0) {
    throw new Error("Docker Desktop could not be started with 'docker desktop start'.");
  }

  if (!await waitUntil(dockerReady, 120000, 3000)) {
    throw new Error("Docker did not become ready after 'docker desktop start'.");
  }
}

function containerHealthy() {
  const p = run("docker.exe", [
    "inspect", "-f",
    "{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}",
    CONTAINER
  ], { timeout: 10000 });
  const state = String(p.stdout || "").trim();
  return p.status === 0 && (state === "healthy" || state === "running");
}

async function localGatewayHealthy(port = gatewayPort) {
  const url = "http://127.0.0.1:" + port + "/health";
  const result = await request(url, { timeout: 3000 });
  return result.status >= 200 && result.status < 300;
}

function publishedGatewayPort() {
  const p = run("docker.exe", ["port", CONTAINER, "2999/tcp"], { timeout: 10000 });
  if (p.status !== 0) return null;

  const lines = String(p.stdout || "").split(/\r?\n/).map(line => line.trim()).filter(Boolean);
  for (const line of lines) {
    const match = line.match(/:(\d+)$/);
    if (match) return Number(match[1]);
  }
  return null;
}

function getExcludedPortRanges() {
  const p = run("netsh.exe", [
    "interface", "ipv4", "show", "excludedportrange", "protocol=tcp"
  ], { timeout: 10000 });

  if (p.status !== 0) return [];

  const ranges = [];
  const text = String(p.stdout || "");
  for (const line of text.split(/\r?\n/)) {
    const m = line.match(/^\s*(\d+)\s+(\d+)(?:\s+\*)?\s*$/);
    if (!m) continue;
    ranges.push({ start: Number(m[1]), end: Number(m[2]) });
  }
  return ranges;
}

function isPortExcluded(port, ranges) {
  return ranges.some(range => port >= range.start && port <= range.end);
}

function isPortBindable(port) {
  return new Promise(resolve => {
    const server = net.createServer();
    let settled = false;

    const finish = value => {
      if (settled) return;
      settled = true;
      resolve(value);
    };

    server.once("error", () => finish(false));
    server.listen({ host: "127.0.0.1", port, exclusive: true }, () => {
      server.close(() => finish(true));
    });
  });
}

async function chooseGatewayPort() {
  const ranges = getExcludedPortRanges();

  // If the preferred port is already serving this MCP, reuse it.
  if (!isPortExcluded(DEFAULT_GATEWAY_PORT, ranges) && await localGatewayHealthy(DEFAULT_GATEWAY_PORT)) {
    return DEFAULT_GATEWAY_PORT;
  }

  for (let port = DEFAULT_GATEWAY_PORT; port <= MAX_GATEWAY_PORT; port++) {
    if (isPortExcluded(port, ranges)) continue;
    if (await isPortBindable(port)) return port;
  }

  throw new Error(
    "No usable TCP port was found between " + DEFAULT_GATEWAY_PORT + " and " + MAX_GATEWAY_PORT + "."
  );
}

function findExecutable(name) {
  const where = run("where.exe", [name], { timeout: 10000 });
  if (where.status === 0) {
    const first = String(where.stdout || "")
      .split(/\r?\n/)
      .map(line => line.trim())
      .find(Boolean);
    if (first && fs.existsSync(first)) return first;
  }
  return null;
}

function findTailscale() {
  const discovered = findExecutable("tailscale.exe");
  if (discovered) return discovered;

  const candidates = [
    path.join(process.env.ProgramFiles || "C:\Program Files", "Tailscale", "tailscale.exe"),
    path.join(process.env["ProgramFiles(x86)"] || "C:\Program Files (x86)", "Tailscale", "tailscale.exe"),
  ];
  return candidates.find(fs.existsSync) || null;
}

function findTailscaleClient(ts) {
  const sameDir = ts ? path.join(path.dirname(ts), "tailscale-ipn.exe") : null;
  if (sameDir && fs.existsSync(sameDir)) return sameDir;

  const discovered = findExecutable("tailscale-ipn.exe");
  if (discovered) return discovered;

  const candidates = [
    path.join(process.env.ProgramFiles || "C:\Program Files", "Tailscale", "tailscale-ipn.exe"),
    path.join(process.env["ProgramFiles(x86)"] || "C:\Program Files (x86)", "Tailscale", "tailscale-ipn.exe"),
  ];
  return candidates.find(fs.existsSync) || null;
}

function tailscaleClientRunning() {
  const p = run("tasklist.exe", ["/FI", "IMAGENAME eq tailscale-ipn.exe"], { timeout: 10000 });
  if (p.status !== 0) return false;
  return String(p.stdout || "").toLowerCase().includes("tailscale-ipn.exe");
}

async function ensureTailscaleClient(ts) {
  if (tailscaleClientRunning()) return;

  const client = findTailscaleClient(ts);
  if (!client) {
    throw new Error("tailscale-ipn.exe was not found.");
  }

  console.log("Tailscale client: starting");
  const child = spawn(client, [], {
    cwd: path.dirname(client),
    detached: true,
    windowsHide: false,
    stdio: "ignore",
  });
  child.unref();

  if (!await waitUntil(tailscaleClientRunning, 15000, 500)) {
    throw new Error("Tailscale client did not start.");
  }
}

function tailscaleServiceRunning() {
  const p = run("sc.exe", ["query", "Tailscale"], { timeout: 10000 });
  const output = String(p.stdout || "") + String(p.stderr || "");
  return p.status === 0 && output.toUpperCase().includes("RUNNING");
}

function runElevatedSc(action, serviceName) {
  console.log("Administrator permission is required for " + action + " " + serviceName + ".");
  console.log("Requesting UAC approval...");

  const psCommand =
    "$p = Start-Process -FilePath 'sc.exe' " +
    "-ArgumentList @('" + action + "','" + serviceName + "') " +
    "-Verb RunAs -Wait -PassThru; exit $p.ExitCode";

  const result = spawnSync("powershell.exe", [
    "-NoProfile",
    "-Command", psCommand,
  ], {
    windowsHide: false,
    stdio: "inherit",
    timeout: 60000,
  });

  return result.status === 0;
}

async function ensureTailscaleService() {
  if (tailscaleServiceRunning()) return;

  let started = run("sc.exe", ["start", "Tailscale"], { timeout: 15000 });

  if (started.status !== 0) {
    if (!runElevatedSc("start", "Tailscale")) {
      throw new Error("Tailscale Windows service could not be started after UAC approval.");
    }
  }

  if (!await waitUntil(tailscaleServiceRunning, 30000, 1000)) {
    throw new Error("Tailscale Windows service did not become ready.");
  }
}

function tailscaleRunning(ts) {
  const p = run(ts, ["status", "--json"], { timeout: 10000 });
  if (p.status !== 0) return false;
  try {
    return JSON.parse(p.stdout).BackendState === "Running";
  } catch {
    return false;
  }
}

function funnelReady(ts, port = gatewayPort) {
  const p = run(ts, ["funnel", "status"], { timeout: 10000 });
  const output = String(p.stdout || "") + String(p.stderr || "");
  return p.status === 0 && output.includes("https://") && output.includes(String(port));
}

function ensureFunnel(ts) {
  if (funnelReady(ts)) return;

  const p = run(ts, ["funnel", "--bg", "--yes", String(gatewayPort)], {
    timeout: 20000,
    inherit: true,
  });
  if (p.status !== 0) throw new Error("Failed to configure Tailscale Funnel.");
}

async function publicReady(baseUrl, verbose = true) {
  const health = await request(baseUrl + "/health");
  const resource = await request(baseUrl + "/.well-known/oauth-protected-resource/mcp");
  const auth = await request(baseUrl + "/.well-known/oauth-authorization-server");
  const mcp = await request(baseUrl + "/mcp", {
    method: "POST",
    headers: { Accept: "application/json, text/event-stream" },
    body: {
      jsonrpc: "2.0",
      id: 1,
      method: "initialize",
      params: {
        protocolVersion: "2025-11-25",
        capabilities: {},
        clientInfo: { name: "mcp-readiness-check", version: "1.0" },
      },
    },
  });

  let resourceOk = resource.status >= 200 && resource.status < 300;
  let authOk = auth.status >= 200 && auth.status < 300;

  try {
    if (resourceOk) resourceOk = JSON.parse(resource.body).resource === baseUrl + "/mcp";
  } catch { resourceOk = false; }

  try {
    if (authOk) {
      const j = JSON.parse(auth.body);
      authOk = Boolean(j.authorization_endpoint && j.token_endpoint);
    }
  } catch { authOk = false; }

  const healthOk = health.status >= 200 && health.status < 300;
  const mcpOk = mcp.status === 401 || (mcp.status >= 200 && mcp.status < 300);

  if (verbose) {
    console.log(`Health API        : ${healthOk ? "OK" : "FAIL HTTP " + health.status}`);
    console.log(`OAuth resource    : ${resourceOk ? "OK" : "FAIL"}`);
    console.log(`OAuth server      : ${authOk ? "OK" : "FAIL"}`);
    console.log(`MCP auth challenge: ${mcpOk ? "OK" : "FAIL HTTP " + mcp.status}`);
  }

  return healthOk && resourceOk && authOk && mcpOk;
}

async function existingEnvironmentReady(publicUrl) {
  const state = readStartState();
  const gitHead = currentGitHead();
  if (!state || !gitHead || state.gitHead !== gitHead || !gitWorkingTreeClean()) return false;
  if (!runnerReady() || !dockerReady() || !containerHealthy()) return false;

  const port = publishedGatewayPort();
  if (!port || !await localGatewayHealthy(port)) return false;

  const ts = findTailscale();
  if (!ts || !tailscaleServiceRunning() || !tailscaleRunning(ts)) return false;
  if (!funnelReady(ts, port)) return false;
  if (!await publicReady(publicUrl, false)) return false;

  gatewayPort = port;
  return true;
}

async function main() {
  for (const file of [composeFile, envFile, runnerScript, runnerConfig]) {
    if (!fs.existsSync(file)) throw new Error(`Required file not found: ${file}`);
  }

  const env = readEnv(envFile);
  const publicUrl = String(env.MCP_PUBLIC_URL || "").replace(/\/$/, "");
  if (!publicUrl) throw new Error("MCP_PUBLIC_URL is missing.");

  step("Existing environment check");
  if (await existingEnvironmentReady(publicUrl)) {
    console.log("Environment    : ALREADY READY");
    console.log("Gateway port   : " + gatewayPort);
    console.log("Docker rebuild : SKIPPED");
    console.log("");
    console.log("ChatGPT MCP    : READY");
    return;
  }
  console.log("Environment    : startup/recovery required");
  rollbackOnFailure = true;

  step("Windows Runner");
  await ensureRunner();
  console.log("Windows Runner : READY");

  step("Docker");
  await ensureDocker();
  console.log("Docker         : OK");

  step("Gateway port selection");
  gatewayPort = await chooseGatewayPort();
  if (gatewayPort === DEFAULT_GATEWAY_PORT) {
    console.log("Gateway port   : " + gatewayPort + " (preferred)");
  } else {
    console.log("Gateway port   : " + gatewayPort + " (automatic fallback)");
  }

  step("workmachine build / start");
  console.log(`Source         : ${mcpRoot}`);

  let p = run("docker.exe", [
    "compose", "--env-file", envFile,
    "-f", composeFile,
    "up", "-d", "--build", "--force-recreate", "workmachine"
  ], {
    cwd: mcpRoot,
    timeout: 300000,
    inherit: true,
    env: {
      MCP_HOST_PORT: String(gatewayPort),
      WINDOWS_RUNNER_HOST_DIR: runnerQueueDir,
      WINDOWS_RUNNER_CONTAINER_DIR: runnerContainerQueueDir,
    },
  });
  if (p.status !== 0) throw new Error("docker compose up --build failed.");

  if (!await waitUntil(containerHealthy, 120000, 2000)) {
    throw new Error("workmachine did not become healthy.");
  }
  console.log("workmachine    : HEALTHY");

  step("Local MCP gateway");
  if (!await waitUntil(() => localGatewayHealthy(gatewayPort), 30000, 1000)) {
    throw new Error(
      "Local MCP gateway is not reachable at http://127.0.0.1:" + gatewayPort + "/health. " +
      "The selected port could not be reached after Docker startup."
    );
  }
  console.log("Local gateway  : OK 127.0.0.1:" + gatewayPort);

  if (!fs.existsSync(runnerQueueDir)) {
    throw new Error("Windows Runner queue directory is missing.");
  }
  console.log("Runner queue   : OK " + runnerQueueDir);

  step("Tailscale");
  const ts = findTailscale();
  if (!ts) throw new Error("tailscale.exe was not found.");
  await ensureTailscaleService();
  await ensureTailscaleClient(ts);
  console.log("Tailscale client: RUNNING");
  if (!tailscaleRunning(ts)) {
    const up = run(ts, ["up"], { timeout: 30000, inherit: true });
    if (up.status !== 0 || !tailscaleRunning(ts)) {
      throw new Error("Tailscale could not be connected.");
    }
  }
  console.log("Tailscale      : OK");

  step("Tailscale Funnel");
  ensureFunnel(ts);
  console.log("Funnel         : OK");

  step("ChatGPT MCP readiness API check");
  if (!await publicReady(publicUrl)) {
    throw new Error("ChatGPT-facing readiness check failed.");
  }

  writeStartState();
  rollbackOnFailure = false;

  console.log("");
  console.log("ChatGPT MCP    : READY");
}

main().catch(err => {
  console.error("");
  console.error("[ERROR] " + (err?.message || err));

  if (rollbackOnFailure) {
    console.log("");
    console.log("==> Rollback failed startup");
    const stopScript = path.join(scriptRoot, "StopMCP.cjs");
    const rollback = run(nodeExe, [stopScript], {
      cwd: projectRoot,
      timeout: 240000,
      inherit: true,
    });
    if (rollback.status === 0) {
      console.log("Rollback       : COMPLETE");
    } else {
      console.error("[WARN] Automatic rollback did not complete cleanly.");
    }
  }

  process.exitCode = 1;
});
