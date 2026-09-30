# ChatLocalWork Windows Runner 보안

## 보안 목표

Windows Runner는 ChatGPT가 Windows 호스트에서 필요한 개발 도구를 실행할 수 있게 하되, 임의의 셸 실행 경로를 기본 제공하지 않는 것을 목표로 한다.

핵심 원칙:

- direct process execution
- `shell:false`
- allowlist
- 명시적 blocklist
- workspace 제한
- timeout
- output limit

## 현재 허용 실행 파일

실제 기준 파일:

```text
windows-runner/runner-config.json
```

현재 allowlist:

- `git.exe`
- `dotnet.exe`
- `cmake.exe`
- `ninja.exe`
- `msbuild.exe`
- `cl.exe`
- `docker.exe`
- `tailscale.exe`
- `taskkill.exe`

문서보다 `runner-config.json`이 최종 기준이다.

## 명시적 차단

현재 blocklist:

- `powershell.exe` / `powershell`
- `pwsh.exe` / `pwsh`
- `cmd.exe` / `cmd`
- `wscript.exe` / `wscript`
- `cscript.exe` / `cscript`
- `mshta.exe` / `mshta`
- `rundll32.exe` / `rundll32`
- `regsvr32.exe` / `regsvr32`

PowerShell과 cmd를 기본 Windows 실행 경로로 사용하지 않는다.

## 프로세스 실행

Runner는 Node.js `child_process.spawn`을 사용하며 항상 `shell:false`로 실행한다.

즉:

```text
executable + args[]
```

형태로 직접 프로세스를 실행한다.

문자열 전체를 셸에 넘겨 파싱시키는 구조가 아니다.

## workspace 제한

Runner의 현재 workspaceRoot는 Windows의 `chat_local_workspace`다.

예:

```text
C:\Users\<user>\Documents\chat_local_workspace
```

`workdir`는 이 workspace 내부여야 한다.

workspace 내부에서 직접 빌드된 실행 파일에 대한 처리는 Runner 구현의 경로 검증 규칙을 따른다.

## timeout / output 제한

현재 기본값:

```text
defaultTimeoutMs = 120000
maxOutputBytes   = 1048576
```

timeout이 발생하면 프로세스 트리 종료를 위해 `taskkill.exe`를 직접 사용할 수 있다.

## Runner runtime

Windows Runner runtime은 프로젝트 폴더가 아니라 다음 위치를 사용한다.

```text
%LOCALAPPDATA%\ChatLocalWork\runtime\windows-runner
```

Docker에는:

```text
/chatlocalwork-runtime/windows-runner
```

로 bind mount된다.

이 폴더에는 request/response JSON과 로그, `ready.json`이 저장된다.

이 데이터는 실행 상태이므로 백업 대상이 아니다.

## MCP 서버 권한과 Runner 권한의 차이

LocalWorkMCP의 Linux/Docker 파일 도구는 `/shared`로 마운트된 workspace에 접근할 수 있다.

Windows 프로세스 실행은 별도의 Windows Runner 정책을 거친다.

따라서 다음 두 권한 모델은 구분해서 봐야 한다.

```text
Docker/Linux 파일 작업
  -> LocalWorkMCP 도구 권한

Windows 프로세스 실행
  -> Windows Runner allowlist + 경로 제한
```

## Tailscale / OAuth

ChatGPT에서 MCP에 접근하는 경로는 Tailscale Funnel을 사용한다.

MCP 서버는 OAuth 설정을 사용하며, OAuth state는 Docker volume에 저장한다.

기존 상태 호환성을 위해 일부 내부 경로/volume 이름에는 `cokacremote` 식별자가 남아 있다.

이 값은 보안 상태/OAuth state 호환성에 영향을 줄 수 있으므로 단순 브랜딩 목적으로 임의 변경하지 않는다.

## Avast 관련

초기 PowerShell Bridge는 Avast에서 `IDP.HELU.PSE91` 탐지를 반복 유발했다.

현재 구조는 PowerShell/cmd를 거치지 않고 허용된 실행 파일을 직접 실행한다.

이 구조로 `git.exe`, `dotnet.exe` 실행과 실제 빌드 작업을 검증했다.
