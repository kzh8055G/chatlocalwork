# workmachine Docker setup

`workmachine` runs the LocalWorkMCP server in Docker and mounts the host workspace at `/shared`.

> Compatibility note: existing Docker project, volume, and internal state paths still use the historical `cokacremote` identifier so upgrades keep the current OAuth state and running environment. These are runtime compatibility identifiers, not the LocalWorkMCP project name.

## Windows recommended flow

Use the integrated scripts instead of running Compose manually:

```bat
scripts\windows\StartMCP.bat
scripts\windows\StopMCP.bat
```

`StartMCP.bat`:

- starts the Windows Runner and Docker Desktop when needed
- prefers host port `3999` and automatically selects another free port in `4000-4999` when necessary
- passes the selected port to Compose through `MCP_HOST_PORT`
- verifies the local `/health` endpoint
- starts the Tailscale Windows service and client when needed
- configures Tailscale Funnel to the selected host port
- verifies the public MCP/OAuth endpoints

## Configuration

Copy `.env.example` to `.env` and set at least:

```dotenv
SHARED_PATH=C:/Users/you/Documents/chat_local_workspace
MCP_PUBLIC_URL=https://your-machine.your-tailnet.ts.net
TZ=Asia/Seoul
```

Do not commit the populated `.env` file.

The container always listens on port `2999`. The Windows host port is configurable:

```yaml
127.0.0.1:${MCP_HOST_PORT:-3999}:2999
```

The start script supplies `MCP_HOST_PORT` automatically.

## Manual Compose use

If needed, Compose can still be run directly:

```bash
docker compose --env-file tunneling/.env -f tunneling/docker-compose.yml up -d --build
```

Read the generated OAuth approval key with:

```bash
docker compose --env-file tunneling/.env -f tunneling/docker-compose.yml exec workmachine cat /var/lib/cokacremote/oauth-approval-key
```
