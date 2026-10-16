"use strict";

const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

function normalizePath(value) {
  return path.resolve(value).toLowerCase();
}

// Never execute an unqualified name from a different location than the one
// shown to the user. Resolve a concrete executable before checking policy.
function resolveExecutable(raw, cwd) {
  const name = String(raw || "").trim();
  if (!name) throw new Error("executable is required");

  const qualified = path.isAbsolute(name) || name.includes("/") || name.includes("\\");
  const locations = qualified
    ? [path.isAbsolute(name) ? name : path.resolve(cwd, name)]
    : [
        path.join(cwd, name),
        ...String(process.env.Path || process.env.PATH || "")
          .split(path.delimiter).filter(Boolean).map(dir => path.join(dir, name)),
      ];

  for (const location of locations) {
    try {
      if (fs.statSync(location).isFile()) return fs.realpathSync(location);
    } catch { /* continue */ }
  }
  throw new Error("Executable not found: " + name);
}

function sha256(file) {
  return new Promise((resolve, reject) => {
    const hash = crypto.createHash("sha256");
    const stream = fs.createReadStream(file);
    stream.on("data", chunk => hash.update(chunk));
    stream.once("error", reject);
    stream.once("end", () => resolve(hash.digest("hex")));
  });
}

function readTrusted(file) {
  try {
    const data = JSON.parse(fs.readFileSync(file, "utf8"));
    return Array.isArray(data.entries) ? data.entries.filter(entry =>
      typeof entry.path === "string" && /^[0-9a-f]{64}$/i.test(entry.sha256)) : [];
  } catch {
    return [];
  }
}

function sessionKey(resolvedPath, hash, argv, cwd) {
  return JSON.stringify([normalizePath(resolvedPath), hash, argv, normalizePath(cwd)]);
}

module.exports = { normalizePath, resolveExecutable, sha256, readTrusted, sessionKey };
