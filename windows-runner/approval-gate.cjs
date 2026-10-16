"use strict";

const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

function atomicJson(file, value) {
  const temp = file + "." + process.pid + ".tmp";
  fs.writeFileSync(temp, JSON.stringify(value, null, 2), "utf8");
  fs.renameSync(temp, file);
}

async function awaitApproval({ approvalsDir, id, executable, args, workdir, hash, timeoutMs }) {
  const pendingDir = path.join(approvalsDir, "pending");
  const decisionsDir = path.join(approvalsDir, "decisions");
  fs.mkdirSync(pendingDir, { recursive: true });
  fs.mkdirSync(decisionsDir, { recursive: true });

  const nonce = crypto.randomBytes(24).toString("hex");
  const pendingPath = path.join(pendingDir, id + ".json");
  const decisionPath = path.join(decisionsDir, id + ".json");
  const expiresAt = new Date(Date.now() + timeoutMs).toISOString();
  try { fs.unlinkSync(decisionPath); } catch {}
  atomicJson(pendingPath, {
    id, nonce, executable, args, workdir, sha256: hash,
    createdAt: new Date().toISOString(), expiresAt,
  });

  try {
    const deadline = Date.parse(expiresAt);
    while (Date.now() < deadline) {
      if (fs.existsSync(decisionPath)) {
        let decision;
        try { decision = JSON.parse(fs.readFileSync(decisionPath, "utf8")); }
        catch { decision = null; }
        try { fs.unlinkSync(decisionPath); } catch {}
        if (decision?.id !== id || decision?.nonce !== nonce ||
            !["once", "session", "trust", "deny"].includes(decision?.choice)) {
          throw new Error("Invalid approval response.");
        }
        if (decision.choice === "deny") throw new Error("Execution denied by user.");
        return decision.choice;
      }
      await new Promise(resolve => setTimeout(resolve, 200));
    }
    throw new Error("Execution approval timed out (default deny).");
  } finally {
    try { fs.unlinkSync(pendingPath); } catch {}
    try { fs.unlinkSync(decisionPath); } catch {}
  }
}

module.exports = { atomicJson, awaitApproval };
