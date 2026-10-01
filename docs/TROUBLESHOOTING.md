# ChatLocalWork 문제 해결

## 1. Manager에서 Start MCP가 실패하는 경우

Manager의 실행 로그에서 어느 단계까지 성공했는지 먼저 확인한다.

일반적인 단계:

```text
Windows Runner
Docker
Gateway port selection
workmachine build / start
Local MCP gateway
Tailscale
Tailscale Funnel
ChatGPT MCP readiness API check
```

가장 먼저 실패한 단계가 실제 원인에 가깝다.

## 2. Windows Runner 상태 확인

현재 runtime 경로:

```text
%LOCALAPPDATA%\ChatLocalWork\runtime\windows-runner
```

상태 파일:

```text
%LOCALAPPDATA%\ChatLocalWork\runtime\windows-runner\state\ready.json
```

정상 예:

```json
{
  "ok": true,
  "pid": 12345,
  "platform": "win32",
  "runtime": "node",
  "executionMode": "direct-process",
  "shell": false,
  "workspaceRoot": "C:\\Users\\<user>\\Documents\\chat_local_workspace",
  "queueDir": "C:\\Users\\<user>\\AppData\\Local\\ChatLocalWork\\runtime\\windows-runner"
}
```

`queueDir`가 예전 `.windows-runner`를 가리킨다면 이전 Runner가 남아 있거나 stale `ready.json`일 수 있다.

Stop MCP 후 다시 Start MCP 한다.

## 3. Runner 로그

```text
%LOCALAPPDATA%\ChatLocalWork\runtime\windows-runner\logs\runner.log
%LOCALAPPDATA%\ChatLocalWork\runtime\windows-runner\logs\launcher.log
```

## 4. windows_exec가 timeout 되는 경우

다음 순서로 확인한다.

1. `ready.json`이 존재하는지
2. Runner PID가 실제 실행 중인지
3. Docker에 runtime bind mount가 붙어 있는지
4. `requests/`에 요청 JSON이 생성되는지
5. `responses/`에 응답 JSON이 생성되는지
6. `runner.log`에 REJECT / timeout / spawn 오류가 있는지

Docker 내부 Runner queue:

```text
/chatlocalwork-runtime/windows-runner
```

## 5. Docker Runner mount 확인

`workmachine`의 mount에 다음 두 개가 있어야 한다.

Workspace:

```text
C:\Users\<user>\Documents\chat_local_workspace
  -> /shared
```

Runner runtime:

```text
%LOCALAPPDATA%\ChatLocalWork\runtime\windows-runner
  -> /chatlocalwork-runtime/windows-runner
```

## 6. Docker build에서 $'\r' 오류가 나는 경우

증상 예:

```text
/bin/bash: $'\r': command not found
```

또는 Dockerfile heredoc `RUN <<'SETUP'` 구간에서 실패한다.

원인은 Windows CRLF가 Linux Bash heredoc에 들어간 것이다.

현재 저장소는 `.gitattributes`에서 Dockerfile을 LF로 강제한다.

```text
localworkmcp/tunneling/Dockerfile text eol=lf
*.sh text eol=lf
```

문제가 재발하면 저장소를 최신 상태로 맞춘 뒤 Dockerfile line ending을 LF로 확인한다.

## 7. Manager는 실행되지만 Runner가 Stopped로 보이는 경우

Manager도 다음 LocalAppData 경로를 기준으로 상태를 확인한다.

```text
%LOCALAPPDATA%\ChatLocalWork\runtime\windows-runner
```

Manager를 구조 변경 전 빌드로 실행하고 있지 않은지 확인한다.

현재 프로젝트에서 다시 빌드:

```text
dotnet build manager\ChatLocalWork.Manager\ChatLocalWork.Manager.csproj -c Release
```

## 8. MCP endpoint는 열리지만 ChatGPT에서 연결되지 않는 경우

다음을 분리해서 확인한다.

- Local `/health`
- Tailscale 상태
- Funnel 상태
- OAuth protected-resource metadata
- OAuth authorization-server metadata
- `/mcp` 인증 challenge

StartMCP는 위 항목을 마지막 readiness 단계에서 확인한다.

## 9. port 3999를 사용할 수 없는 경우

StartMCP는 3999를 우선 사용한다.

Windows excluded port range 또는 다른 프로세스가 점유 중이면 설정된 범위에서 사용 가능한 port를 자동 선택한다.

따라서 3999가 아니더라도 Start 로그에서 선택된 port와 Funnel 설정이 일치하면 정상이다.

## 10. Avast

구 PowerShell Bridge는 `IDP.HELU.PSE91` 탐지를 유발했다.

현재 Windows Runner는 PowerShell/cmd를 거치지 않고 `shell:false`로 실행 파일을 직접 호출한다.

Avast가 다시 탐지하면 어떤 executable과 어떤 Runner 로그 시점에서 발생했는지 먼저 확인한다.

## 11. runtime 폴더 삭제

다음 폴더는 런타임 데이터다.

```text
%LOCALAPPDATA%\ChatLocalWork\runtime\windows-runner
```

MCP가 실행 중일 때는 삭제하면 안 된다.

Manager에서 Stop MCP를 실행해 Runner가 종료된 뒤라면 삭제 가능하다. 다음 Start에서 필요한 디렉터리가 다시 생성된다.

## 12. 기존 .windows-runner

이전 버전은 다음 위치를 사용했다.

```text
chat_local_workspace\.windows-runner
```

현재는 사용하지 않는다.

StopMCP에 기존 위치 cleanup 코드가 남아 있는 것은 마이그레이션 호환 목적이다.

## 13. Installer에서 필수 의존성 설치가 실패하는 경우

외부 의존성 설치는 Manager가 아니라 `ChatLocalWork.Installer`가 담당한다.

현재 Installer는 다음을 처리한다.

- Git: winget
- Docker Desktop: winget
- Tailscale: winget
- Portable Node: nodejs.org의 현재 Windows x64 LTS ZIP 다운로드
- ChatLocalWork app: GitHub `kzh8055G/chatlocalwork`의 `main`을 `app\current`에 clone

확인 순서:

1. `ChatLocalWork.Installer.exe --check`로 누락 항목 확인
2. winget 사용 가능 여부와 인터넷 연결 확인
3. Git/Docker/Tailscale 설치 중 표시되는 UAC 또는 설치 UI 확인
4. Docker Desktop 설치 후 재부팅/최초 실행 필요 여부 확인
5. Portable Node 경로 `%LOCALAPPDATA%\ChatLocalWork\tools\node\node.exe` 확인
6. `%LOCALAPPDATA%\ChatLocalWork\app\current\.git` 및 `scripts\windows\StartMCP.cjs` 존재 여부 확인

설치 로직만 격리 검증할 때는 `--data-root`와 `--workspace-root`를 임시 경로로 지정한다.
