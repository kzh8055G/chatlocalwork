"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("fs");
const os = require("os");
const path = require("path");
const crypto = require("crypto");
const { awaitApproval } = require("./approval-gate.cjs");
const { readTrusted, sha256, sessionKey } = require("./approval-policy.cjs");

test("approval gate: once decision accepted for exact nonce", async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "clw-approve-"));
  try {
    const promise = awaitApproval({ approvalsDir: root, id: "test-once", executable: "test.exe",
      args: ["--foo"], workdir: root, hash: "a".repeat(64), timeoutMs: 2000 });
    let request;
    for (let i = 0; i < 100; i++) {
      const file = path.join(root, "pending", "test-once.json");
      if (fs.existsSync(file)) { request = JSON.parse(fs.readFileSync(file, "utf8")); break; }
      await new Promise(resolve => setTimeout(resolve, 5));
    }
    assert.ok(request);
    fs.writeFileSync(path.join(root, "decisions", "test-once.json"),
      JSON.stringify({ id: request.id, nonce: request.nonce, choice: "once" }));
    assert.equal(await promise, "once");
    assert.equal(fs.existsSync(path.join(root, "pending", "test-once.json")), false);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test("approval gate: forged response fails closed", async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "clw-approve-"));
  try {
    const promise = awaitApproval({ approvalsDir: root, id: "forged", executable: "test.exe",
      args: [], workdir: root, hash: "b".repeat(64), timeoutMs: 2000 });
    fs.writeFileSync(path.join(root, "decisions", "forged.json"),
      JSON.stringify({ id: "forged", nonce: "wrong", choice: "trust" }));
    await assert.rejects(promise, /Invalid approval response/);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test("approval gate: defaults to denial on timeout", async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "clw-approve-"));
  try {
    await assert.rejects(awaitApproval({ approvalsDir: root, id: "timeout", executable: "test.exe",
      args: [], workdir: root, hash: "c".repeat(64), timeoutMs: 250 }), /timed out/);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

test("trusted records require a full sha256 and change when binary changes", async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "clw-approve-"));
  try {
    const exe = path.join(root, "test.exe");
    fs.writeFileSync(exe, "test v1");
    const first = await sha256(exe);
    assert.equal(first, crypto.createHash("sha256").update("test v1").digest("hex"));
    const trusted = path.join(root, "trusted.json");
    fs.writeFileSync(trusted, JSON.stringify({ entries: [{ path: exe, sha256: first }, { path: "bad", sha256: "123" }] }));
    assert.equal(readTrusted(trusted).length, 1);
    fs.writeFileSync(exe, "test v2");
    assert.notEqual(await sha256(exe), first);
    assert.notEqual(sessionKey(exe, first, ["a"], root), sessionKey(exe, first, ["b"], root));
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});
