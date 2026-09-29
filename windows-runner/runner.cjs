const fs = require("fs");
const path = require("path");
const { spawn, spawnSync } = require("child_process");

function parseArgs(argv) {
  const out = {};
  for (let i = 2; i < argv.length; i += 2) {
    if (argv[i]?.startsWith("--")) out[argv[i].slice(2)] = argv[i + 1];
  }
  return out;
}

const args = parseArgs(process.argv);
const workspaceRoot = path.resolve(args["workspace-root"] || path.resolve(__dirname, "..", ".."));
const queueDir = path.resolve(args["queue-dir"] || path.join(workspaceRoot, ".windows-runner"));
const configPath = path.resolve(args["config"] || path.join(__dirname, "runner-config.json"));
const requestsDir = path.join(queueDir, "requests");
const responsesDir = path.join(queueDir, "responses");
const stateDir = path.join(queueDir, "state");
const logDir = path.join(queueDir, "logs");
const readyFile = path.join(stateDir, "ready.json");
const logFile = path.join(logDir, "runner.log");

for (const dir of [requestsDir, responsesDir, stateDir, logDir]) {
  fs.mkdirSync(dir, { recursive: true });
}

const config = JSON.parse(fs.readFileSync(configPath, "utf8"));
const allowedExecutables = new Set((config.allowedExecutables || []).map(x => String(x).toLowerCase()));
const blockedExecutables = new Set((config.blockedExecutables || []).map(x => String(x).toLowerCase()));
const defaultTimeoutMs = Number(config.defaultTimeoutMs || 120000);
const maxOutputBytes = Number(config.maxOutputBytes || 1048576);

function log(message) {
  const line = "[" + new Date().toISOString() + "] " + message;
  console.log(line);
  fs.appendFileSync(logFile, line + "\n", "utf8");
}

function writeJsonAtomic(file, value) {
  const temp = file + ".tmp-" + process.pid;
  fs.writeFileSync(temp, JSON.stringify(value, null, 2), "utf8");
  fs.renameSync(temp, file);
}

function isInsideWorkspace(candidate) {
  const root = workspaceRoot.toLowerCase();
  const resolved = path.resolve(candidate).toLowerCase();
  return resolved === root || resolved.startsWith(root + path.sep);
}

function validateExecutable(executable) {
  const raw = String(executable || "").trim();
  if (!raw) throw new Error("executable is required");

  const base = path.basename(raw).toLowerCase();
  if (blockedExecutables.has(base)) throw new Error("Executable is blocked: " + base);
  if (allowedExecutables.has(base)) return raw;

  if (path.isAbsolute(raw) && raw.toLowerCase().endsWith(".exe") && isInsideWorkspace(raw) && fs.existsSync(raw)) {
    return path.resolve(raw);
  }

  throw new Error("Executable is not allowed: " + raw);
}

function appendBounded(state, chunk) {
  if (!chunk) return;
  const remaining = maxOutputBytes - Buffer.byteLength(state.text, "utf8");
  if (remaining <= 0) {
    state.truncated = true;
    return;
  }
  const buffer = Buffer.from(String(chunk), "utf8");
  if (buffer.length <= remaining) {
    state.text += buffer.toString("utf8");
  } else {
    state.text += buffer.subarray(0, remaining).toString("utf8");
    state.truncated = true;
  }
}

function execute(executable, argv, workdir, timeoutMs) {
  return new Promise(resolve => {
    const started = Date.now();
    const stdout = { text: "", truncated: false };
    const stderr = { text: "", truncated: false };
    let finished = false;
    let timedOut = false;

    const child = spawn(executable, argv, {
      cwd: workdir,
      shell: false,
      windowsHide: true,
      stdio: ["ignore", "pipe", "pipe"],
    });

    child.stdout.setEncoding("utf8");
    child.stderr.setEncoding("utf8");
    child.stdout.on("data", chunk => appendBounded(stdout, chunk));
    child.stderr.on("data", chunk => appendBounded(stderr, chunk));

    const timer = setTimeout(() => {
      timedOut = true;
      try {
        spawnSync("taskkill.exe", ["/PID", String(child.pid), "/T", "/F"], {
          windowsHide: true,
          shell: false,
          stdio: "ignore",
          timeout: 10000,
        });
      } catch {}
    }, timeoutMs);

    const done = (exitCode, error) => {
      if (finished) return;
      finished = true;
      clearTimeout(timer);
      resolve({
        exitCode,
        stdout: stdout.text,
        stderr: stderr.text,
        stdoutTruncated: stdout.truncated,
        stderrTruncated: stderr.truncated,
        timedOut,
        error,
        durationMs: Date.now() - started,
      });
    };

    child.on("error", err => done(null, String(err.message || err)));
    child.on("close", code => done(code, null));
  });
}

writeJsonAtomic(readyFile, {
  ok: true,
  pid: process.pid,
  platform: process.platform,
  runtime: "node",
  executionMode: "direct-process",
  shell: false,
  workspaceRoot,
  queueDir,
  startedAt: new Date().toISOString(),
});

log("READY pid=" + process.pid + " workspace=" + workspaceRoot);

let busy = false;

async function processOne() {
  if (busy) return;
  busy = true;
  try {
    const files = fs.readdirSync(requestsDir).filter(name => name.endsWith(".json")).sort();
    for (const name of files) {
      const requestFile = path.join(requestsDir, name);
      let request;
      try {
        request = JSON.parse(fs.readFileSync(requestFile, "utf8"));
      } catch {
        continue;
      }

      const id = String(request.id || path.basename(name, ".json"));
      const responseFile = path.join(responsesDir, id + ".json");
      if (fs.existsSync(responseFile)) continue;

      const startedAt = new Date().toISOString();
      try {
        const executable = validateExecutable(request.executable);
        const argv = Array.isArray(request.args) ? request.args.map(String) : [];
        const workdir = path.resolve(String(request.workdir || workspaceRoot));
        if (!isInsideWorkspace(workdir)) throw new Error("workdir must be inside workspace root");
        if (!fs.existsSync(workdir) || !fs.statSync(workdir).isDirectory()) {
          throw new Error("workdir does not exist: " + workdir);
        }

        const timeoutMs = Math.min(Math.max(Number(request.timeoutMs || defaultTimeoutMs), 1000), 60 * 60 * 1000);

        log("START " + id + ": " + executable + " " + argv.join(" "));
        const result = await execute(executable, argv, workdir, timeoutMs);
        writeJsonAtomic(responseFile, {
          id,
          ok: result.exitCode === 0 && !result.timedOut && !result.error,
          executable,
          args: argv,
          cwd: workdir,
          shell: false,
          startedAt,
          completedAt: new Date().toISOString(),
          ...result,
        });
        log("DONE " + id + ": exitCode=" + result.exitCode + " timedOut=" + result.timedOut);
      } catch (err) {
        writeJsonAtomic(responseFile, {
          id,
          ok: false,
          exitCode: null,
          stdout: "",
          stderr: "",
          timedOut: false,
          error: String(err?.message || err),
          startedAt,
          completedAt: new Date().toISOString(),
        });
        log("REJECT " + id + ": " + (err?.message || err));
      }
      break;
    }
  } catch (err) {
    log("pump error: " + (err?.stack || err));
  } finally {
    busy = false;
  }
}

process.on("uncaughtException", err => log("uncaughtException: " + (err.stack || err)));
process.on("unhandledRejection", err => log("unhandledRejection: " + (err?.stack || err)));

setInterval(processOne, 200);
processOne();
