import { randomUUID } from "node:crypto";
import { mkdir, readFile, rename, rm, writeFile } from "node:fs/promises";
import path from "node:path";

import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import * as z from "zod/v4";

import type { AppConfig } from "./config.js";
import { runTool } from "./tool-result.js";
import { TOOL_ANNOTATIONS, toolAuthMetadata } from "./tool-metadata.js";

interface WindowsExecResponse {
  id: string;
  ok: boolean;
  executable?: string;
  args?: string[];
  cwd?: string;
  exitCode: number | null;
  stdout: string;
  stderr: string;
  timedOut: boolean;
  error: string | null;
  durationMs?: number;
  stdoutTruncated?: boolean;
  stderrTruncated?: boolean;
}

async function sleep(ms: number): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, ms));
}

export function registerWindowsRunnerTools(server: McpServer, config: AppConfig): void {
  const authMetadata = toolAuthMetadata(config);

  server.registerTool(
    "windows_exec",
    {
      title: "Execute Windows process",
      description:
        "Run an allowed Windows executable through the local Windows Runner. The runner executes the program directly with shell:false; PowerShell and cmd.exe are not used.",
      inputSchema: {
        executable: z
          .string()
          .min(1)
          .describe("Executable name or full path, for example dotnet.exe, git.exe, cmake.exe, or an application .exe inside the shared workspace."),
        args: z
          .array(z.string())
          .optional()
          .describe("Arguments passed directly to the executable. No shell parsing is performed."),
        workdir: z
          .string()
          .optional()
          .describe("Windows working directory inside the shared workspace."),
        timeoutMs: z
          .number()
          .int()
          .min(1_000)
          .max(60 * 60 * 1000)
          .optional()
          .describe("Process timeout in milliseconds. Defaults to WINDOWS_RUNNER_TIMEOUT_MS."),
      },
      annotations: TOOL_ANNOTATIONS.destructiveNonIdempotentOpen,
      _meta: authMetadata,
    },
    async ({ executable, args, workdir, timeoutMs }) =>
      runTool(async () => {
        const queueDir = config.windowsRunnerQueueDir;
        const requestsDir = path.join(queueDir, "requests");
        const responsesDir = path.join(queueDir, "responses");
        const readyPath = path.join(queueDir, "state", "ready.json");

        try {
          const ready = JSON.parse(await readFile(readyPath, "utf8")) as {
            ok?: boolean;
            platform?: string;
            runtime?: string;
            executionMode?: string;
          };
          if (
            ready.ok !== true ||
            ready.platform !== "win32" ||
            ready.runtime !== "node" ||
            ready.executionMode !== "direct-process"
          ) {
            throw new Error("invalid ready state");
          }
        } catch {
          throw new Error(
            "Windows Runner is not ready. Start MCP with StartMCP.bat and verify %LOCALAPPDATA%\\ChatLocalWork\\runtime\\windows-runner\\state\\ready.json.",
          );
        }

        await mkdir(requestsDir, { recursive: true });
        await mkdir(responsesDir, { recursive: true });

        const id = randomUUID();
        const effectiveTimeoutMs = timeoutMs ?? config.windowsRunnerTimeoutMs;
        const requestPath = path.join(requestsDir, id + ".json");
        const tempPath = requestPath + ".tmp";
        const responsePath = path.join(responsesDir, id + ".json");

        await writeFile(
          tempPath,
          JSON.stringify({
            id,
            executable,
            args: args ?? [],
            workdir,
            timeoutMs: effectiveTimeoutMs,
            createdAt: new Date().toISOString(),
          }),
          "utf8",
        );
        await rename(tempPath, requestPath);

        const deadline = Date.now() + effectiveTimeoutMs + 15_000;
        while (Date.now() < deadline) {
          try {
            const raw = await readFile(responsePath, "utf8");
            const result = JSON.parse(raw) as WindowsExecResponse;
            await rm(requestPath, { force: true }).catch(() => undefined);
            await rm(responsePath, { force: true }).catch(() => undefined);
            return { ...result };
          } catch (error) {
            const code = (error as NodeJS.ErrnoException).code;
            if (code && code !== "ENOENT") {
              throw error;
            }
          }
          await sleep(100);
        }

        await rm(requestPath, { force: true }).catch(() => undefined);
        throw new Error(
          "Windows Runner response timed out. Ensure the Windows Runner is running and the shared queue is mounted.",
        );
      }),
  );
}
