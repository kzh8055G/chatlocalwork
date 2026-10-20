import { appendFile, mkdir, rename, stat } from "node:fs/promises";
import path from "node:path";

export interface McpDiagnosticEvent {
  timestamp: string;
  requestId: string;
  httpMethod: string;
  path: string;
  rpcMethod?: string;
  toolName?: string;
  protocolVersion?: string;
  userAgent?: string;
  status: number;
  outcome: "completed" | "aborted";
  durationMs: number;
}

const MAX_LOG_BYTES = 8 * 1024 * 1024;

export class McpDiagnosticLogger {
  private queue: Promise<void> = Promise.resolve();

  constructor(private readonly file: string | undefined) {}

  write(event: McpDiagnosticEvent): void {
    if (!this.file) {
      return;
    }

    const line = JSON.stringify(event) + "\n";
    this.queue = this.queue
      .then(async () => {
        await mkdir(path.dirname(this.file!), { recursive: true });
        await this.rotateIfNeeded();
        await appendFile(this.file!, line, { encoding: "utf8", mode: 0o600 });
      })
      .catch((error) => {
        console.error("Failed to write MCP diagnostic log:", error instanceof Error ? error.message : String(error));
      });
  }

  private async rotateIfNeeded(): Promise<void> {
    if (!this.file) {
      return;
    }

    try {
      const info = await stat(this.file);
      if (info.size < MAX_LOG_BYTES) {
        return;
      }

      const previous = this.file + ".1";
      await rename(this.file, previous).catch(async () => {
        // Rotation is best-effort. If another request rotated first, append to the current file.
      });
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== "ENOENT") {
        throw error;
      }
    }
  }
}
