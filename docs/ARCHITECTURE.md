# ChatLocalWork 아키텍처

## 목적

일반 ChatGPT 채팅에서 Windows 로컬 개발 환경의 파일을 다루고 Git, .NET, CMake 같은 개발 도구를 직접 실행할 수 있게 한다.

ChatLocalWork는 하나의 기능이 아니라 다음 구성 요소를 묶는 상위 프로젝트다.

- LocalWorkMCP
- Windows Runner
- ChatLocalWork Manager
- Start/Stop 자동화
- Docker + Tailscale Funnel 연결

## 전체 요청 흐름

```text
ChatGPT
  │
  │ HTTPS / MCP
  ▼
Tailscale Funnel
  │
  ▼
Docker workmachine
  │
  ├─ Nginx :2999
  │
  └─ LocalWorkMCP :3000
          │
          │ windows_exec
          ▼
/chatlocalwork-runtime/windows-runner/requests/<id>.json
          │
          │ host bind mount
          ▼
%LOCALAPPDATA%\ChatLocalWork\runtime\windows-runner\requests\<id>.json
          │
          ▼
Windows Runner
          │
          │ child_process.spawn(executable, args, { shell:false })
          ▼
Windows executable
          │
          ▼
responses/<id>.json
          │
          ▼
LocalWorkMCP
          │
          ▼
ChatGPT
```

## 저장소 구조

```text
ChatLocalWork/
├─ localworkmcp/
│  ├─ src/
│  ├─ test/
│  ├─ tunneling/
│  ├─ deploy/
│  ├─ package.json
│  └─ README.md
├─ manager/
│  ├─ ChatLocalWork.Manager/
│  └─ README.md
├─ windows-runner/
│  ├─ runner.cjs
│  ├─ runner-config.json
│  └─ README.md
├─ scripts/
│  └─ windows/
│     ├─ StartMCP.bat
│     ├─ StartMCP.cjs
│     ├─ StopMCP.bat
│     └─ StopMCP.cjs
├─ docs/
└─ README.md
```

## Windows Runner 런타임

Runner 프로그램 자체와 런타임 상태는 분리한다.

소스:

```text
ChatLocalWork/windows-runner/
```

런타임:

```text
%LOCALAPPDATA%\ChatLocalWork\runtime\windows-runner/
├─ state/
│  └─ ready.json
├─ logs/
│  ├─ runner.log
│  └─ launcher.log
├─ requests/
└─ responses/
```

Docker에는 다음 위치로 bind mount된다.

```text
/chatlocalwork-runtime/windows-runner
```

`ready.json`에는 현재 Runner PID, workspaceRoot, queueDir, runtime, executionMode 등이 기록된다.

런타임 폴더는 소스나 백업 데이터가 아니다. MCP가 종료된 상태에서는 삭제 가능하며 다음 시작 시 재생성된다.

## Windows workspace

Windows Runner의 `workspaceRoot`는 설치형 환경에서 다음 설정으로 지정한다.

```text
%LOCALAPPDATA%\ChatLocalWork\config\app-config.json
```

`workspaceRoot`는 임의의 절대 Windows 경로를 사용할 수 있으며, StartMCP가 같은 경로를 Docker의 `SHARED_PATH`로 전달한다.

```text
Windows: <workspaceRoot>
Docker : /shared
```

일반 작업 디렉터리는 이 workspace 내부로 제한한다. 설정 파일이 없는 개발 환경에서는 기존 호환을 위해 ChatLocalWork 저장소의 부모 디렉터리를 workspaceRoot로 사용한다.

이 마운트는 프로젝트 파일 접근을 위한 것이고, Windows Runner queue는 별도의 LocalAppData mount를 사용한다.

## Windows 실행 방식

Windows Runner는 Node.js `child_process.spawn`으로 프로세스를 직접 실행한다.

핵심 원칙:

- `shell:false`
- PowerShell/cmd를 중간 셸로 사용하지 않음
- 일반 실행 파일은 allowlist 기반
- 명시적 blocklist 적용
- workdir는 workspace 내부로 제한
- timeout 적용
- stdout/stderr 크기 제한
- 요청마다 UUID 사용

## PowerShell Bridge를 폐기한 이유

초기 구조에서는 Windows 명령 전달에 PowerShell을 사용했다.

Avast가 해당 실행 경로를 `IDP.HELU.PSE91`로 반복 탐지했다.

PowerShell/cmd를 거치지 않고 `git.exe`, `dotnet.exe` 등을 direct-process 방식으로 실행했을 때 해당 탐지가 발생하지 않았고, 이후 Windows Runner를 이 구조로 통일했다.

## 주요 파일

### MCP

- `localworkmcp/src/windows-runner-tools.ts`
  - `windows_exec` 구현
  - request JSON 생성
  - response JSON 대기/수집

- `localworkmcp/tunneling/docker-compose.yml`
  - workspace mount
  - Windows Runner runtime mount
  - OAuth state volume
  - gateway port

### Windows Runner

- `windows-runner/runner.cjs`
  - queue polling
  - executable 검증
  - Windows 프로세스 실행
  - 결과 JSON 작성

- `windows-runner/runner-config.json`
  - allowlist
  - blocklist
  - timeout
  - output limit

### Lifecycle

- `scripts/windows/StartMCP.cjs`
  - Runner → Docker → Funnel → readiness 순으로 시작

- `scripts/windows/StopMCP.cjs`
  - Funnel/Tailscale → Docker → Runner 정리
  - 이전 `.windows-runner` 위치도 마이그레이션 호환 목적으로 정리

### Manager

- `manager/ChatLocalWork.Manager/`
  - Start/Stop 스크립트 실행
  - 각 구성 요소 상태 조회
  - Runner 로그 표시
  - 설치형 경로와 개발 저장소 경로를 분리
  - App package / Node / Docker / Tailscale / MCP config bootstrap 점검
  - 누락된 Docker Desktop / Tailscale을 winget으로 설치
  - Tailscale 로그인 완료 후 MCP public URL과 workspace 설정 자동 생성

설치형 기본 레이아웃:

```text
%LOCALAPPDATA%\ChatLocalWork\
├─ app\current\
├─ tools\node\node.exe
├─ config\app-config.json
└─ runtime\windows-runner\

Documents\ChatLocalWorkWorkspace\
```

Manager가 Git 저장소 내부에서 실행되면 기존 개발 환경을 보호하기 위해 설치형 bootstrap 변경을 자동 적용하지 않는다.

## windows_exec 인터페이스

```text
executable: string
args: string[]
workdir: string
timeoutMs: number
```

예:

```text
executable = git.exe
args       = ["status", "--short", "--branch"]
workdir    = C:\Users\<user>\Documents\chat_local_workspace\chatlocalwork
timeoutMs  = 10000
```

새 프로젝트나 빌드 작업을 추가할 때 별도 액션 이름을 만들지 않는다. 필요한 실행 파일이 허용되어 있다면 동일한 범용 인터페이스를 사용한다.

## 네트워크 구조

```text
ChatGPT
  -> https://<tailscale-funnel-domain>/mcp
  -> Tailscale Funnel
  -> 127.0.0.1:<selected host port>
  -> Docker workmachine:2999
  -> Nginx
  -> LocalWorkMCP:3000
```

host port는 기본 3999를 우선 사용하고, 사용할 수 없으면 범위 내에서 자동 선택한다.

## 런타임 호환 식별자

프로젝트 이름은 ChatLocalWork이고 MCP 컴포넌트 이름은 LocalWorkMCP다.

다만 기존 OAuth/Docker 상태 호환성을 위해 아래와 같은 `cokacremote` 식별자가 일부 런타임에 남아 있다.

- Docker Compose project name
- Docker volume name
- `/var/lib/cokacremote`
- `/opt/cokacremote`
- Supervisor program name

이 값들은 프로젝트 브랜딩이 아니라 기존 상태와의 호환을 위한 내부 식별자이므로 임의로 변경하지 않는다.
