const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");

const CONTAINER = "workmachine";
const scriptRoot = __dirname;
const projectRoot = path.resolve(scriptRoot, "..", "..");
const mcpRoot = path.join(projectRoot, "localworkmcp");
const workspaceRoot = path.dirname(projectRoot);
const composeFile = path.join(mcpRoot, "tunneling", "docker-compose.yml");
const envFile = path.join(mcpRoot, "tunneling", ".env");
const runnerReadyFile = path.join(workspaceRoot, ".windows-runner", "state", "ready.json");

function step(text) {
  console.log("");
  console.log("==> " + text);
}

function run(command, args = [], options = {}) {
  const result = spawnSync(command, args, {
    cwd: options.cwd,
    encoding: "utf8",
    windowsHide: true,
    stdio: options.inherit ? "inherit" : ["ignore", "pipe", "pipe"],
    timeout: options.timeout || 30000,
  });
  return result;
}

function outputOf(result) {
  return (String(result.stdout || "") + String(result.stderr || "")).trim();
}

function bestEffort(command, args = [], options = {}) {
  const result = run(command, args, options);
  if (result.status !== 0 && options.reportFailure) {
    console.log("[WARN] " + command + " " + args.join(" ") + " -> " + outputOf(result));
  }
  return result;
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

function stopRunner() {
  try {
    const ready = JSON.parse(fs.readFileSync(runnerReadyFile, "utf8"));
    const pid = Number(ready.pid);
    if (Number.isInteger(pid) && pid > 0) {
      bestEffort("taskkill.exe", ["/PID", String(pid), "/T", "/F"]);
    }
  } catch {}
  try { fs.unlinkSync(runnerReadyFile); } catch {}
}

function stopDocker() {
  if (fs.existsSync(composeFile) && fs.existsSync(envFile)) {
    bestEffort("docker.exe", [
      "compose",
      "--env-file", envFile,
      "-f", composeFile,
      "down",
      "--remove-orphans",
    ], { cwd: mcpRoot, timeout: 120000, reportFailure: true });
  }

  bestEffort("docker.exe", ["desktop", "stop"], {
    cwd: mcpRoot,
    timeout: 120000,
    reportFailure: true,
  });

  // Docker Desktop may use WSL2. Terminate only Docker-owned distributions.
  bestEffort("wsl.exe", ["--terminate", "docker-desktop"]);
  bestEffort("wsl.exe", ["--terminate", "docker-desktop-data"]);

  // Stop Docker's Windows helper service if present.
  bestEffort("sc.exe", ["stop", "com.docker.service"]);

  for (const image of [
    "Docker Desktop.exe",
    "com.docker.backend.exe",
    "com.docker.build.exe",
    "com.docker.proxy.exe",
    "vpnkit.exe",
  ]) {
    bestEffort("taskkill.exe", ["/IM", image, "/T", "/F"]);
  }
}

function stopTailscale() {
  const ts = findTailscale();
  if (ts) {
    const reset = run(ts, ["funnel", "reset"], { timeout: 10000 });
    if (reset.status !== 0) {
      console.log("[WARN] Tailscale Funnel reset was not needed or could not be applied.");
    }

    const down = run(ts, ["down"], { timeout: 10000 });
    if (down.status !== 0) {
      console.log("[WARN] Tailscale connection was already down or could not be changed.");
    }
  }

  bestEffort("taskkill.exe", ["/IM", "tailscale-ipn.exe", "/T", "/F"]);

  let stopped = run("sc.exe", ["stop", "Tailscale"], { timeout: 15000 });

  if (stopped.status !== 0) {
    if (!runElevatedSc("stop", "Tailscale")) {
      throw new Error("Tailscale Windows service could not be stopped after UAC approval.");
    }
  }

  console.log("Tailscale service: STOPPED");
}

function verifyStopped() {
  const problems = [];

  const docker = run("docker.exe", ["desktop", "status"], { timeout: 10000 });
  const dockerText = outputOf(docker).toLowerCase();
  if (docker.status === 0 && !dockerText.includes("stopped") && !dockerText.includes("not running")) {
    problems.push("Docker Desktop may still be running: " + outputOf(docker));
  }

  const tailscaleService = run("sc.exe", ["query", "Tailscale"], { timeout: 10000 });
  const serviceText = outputOf(tailscaleService).toUpperCase();
  if (tailscaleService.status === 0 && serviceText.includes("RUNNING")) {
    problems.push("Tailscale Windows service is still RUNNING.");
  }

  if (problems.length > 0) {
    throw new Error(problems.join("\n"));
  }
}

function main() {
  step("Tailscale Funnel / connection");
  stopTailscale();
  console.log("Tailscale      : STOPPED");

  step("Docker / workmachine");
  stopDocker();
  console.log("Docker         : STOPPED");

  step("Windows Runner");
  stopRunner();
  console.log("Windows Runner : STOPPED");

  step("Final verification");
  verifyStopped();
  console.log("MCP resources  : STOPPED");

  console.log("");
  console.log("[OK] MCP environment fully stopped.");
}

try {
  main();
} catch (err) {
  console.error("");
  console.error("[ERROR] " + (err?.message || err));
  process.exitCode = 1;
}
