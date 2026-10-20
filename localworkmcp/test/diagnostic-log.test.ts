import { mkdtemp, readFile, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";

import { afterEach, describe, expect, it } from "vitest";

import { McpDiagnosticLogger } from "../src/diagnostic-log.js";

const directories: string[] = [];

afterEach(async () => {
  await Promise.all(directories.splice(0).map(directory => rm(directory, { recursive: true, force: true })));
});

describe("McpDiagnosticLogger", () => {
  it("writes only the supplied non-secret diagnostic fields as NDJSON", async () => {
    const directory = await mkdtemp(path.join(os.tmpdir(), "chatlocalwork-diagnostic-"));
    directories.push(directory);
    const file = path.join(directory, "mcp-access.ndjson");
    const logger = new McpDiagnosticLogger(file);

    logger.write({
      timestamp: "2026-10-20T00:00:00.000Z",
      requestId: "request-1",
      httpMethod: "POST",
      path: "/mcp",
      rpcMethod: "tools/list",
      protocolVersion: "2025-11-25",
      userAgent: "openai-mcp/1.0.0",
      status: 200,
      outcome: "completed",
      durationMs: 12.3,
    });

    for (let attempt = 0; attempt < 50; attempt += 1) {
      try {
        const content = await readFile(file, "utf8");
        const parsed = JSON.parse(content.trim());
        expect(parsed).toMatchObject({
          path: "/mcp",
          rpcMethod: "tools/list",
          protocolVersion: "2025-11-25",
          status: 200,
        });
        expect(content).not.toContain("authorization");
        expect(content).not.toContain("refresh_token");
        return;
      } catch (error) {
        if (attempt === 49) {
          throw error;
        }
        await new Promise(resolve => setTimeout(resolve, 10));
      }
    }
  });
});
