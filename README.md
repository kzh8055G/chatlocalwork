# ChatLocalWork

ChatLocalWork는 일반 ChatGPT 대화에서 사용자의 Windows 로컬 개발 환경을 읽고, 수정하고, 빌드/실행할 수 있도록 연결하는 프로젝트입니다.

핵심 목표는 **Codex가 아닌 일반 ChatGPT Chat에서도 로컬 작업을 수행할 수 있게 만드는 것**입니다.

## 전체 구조

```text
ChatGPT
  │
  │ MCP over HTTPS
  ▼
Tailscale Funnel
  │
  ▼
LocalWorkMCP (Docker / Linux)
  │
  │ file queue
  ▼
Windows Runner
  │
  │ child_process.spawn(..., { shell:false })
  ▼
Windows 개발 도구 / 프로젝트
```

ChatLocalWork는 다음 구성 요소를 하나의 프로젝트로 관리합니다.

```text
ChatLocalWork/
├─ localworkmcp/             # MCP 서버 컴포넌트
├─ installer/
│  └─ ChatLocalWork.Installer/ # 설치/의존성 bootstrapper
├─ manager/
│  └─ ChatLocalWork.Manager/ # Windows 관리 UI
├─ windows-runner/           # Windows Runner 소스와 설정
├─ scripts/
│  └─ windows/               # StartMCP / StopMCP 자동화
├─ docs/                     # 전체 프로젝트 문서
├─ .gitattributes
├─ .gitignore
└─ README.md
```

## 런타임 데이터

프로젝트 소스와 실행 중 생성되는 데이터는 분리되어 있습니다.

Windows Runner 소스:

```text
ChatLocalWork/windows-runner/
├─ runner.cjs
├─ runner-config.json
└─ README.md
```

Windows Runner 런타임:

```text
%LOCALAPPDATA%\ChatLocalWork\runtime\windows-runner/
├─ state/
│  └─ ready.json
├─ logs/
├─ requests/
└─ responses/
```

런타임 폴더는 백업 대상이 아닙니다. MCP가 완전히 종료된 상태에서는 삭제해도 되고, 다음 Start 시 필요한 디렉터리가 다시 생성됩니다.

Docker에서는 이 런타임 폴더가 다음 경로로 마운트됩니다.

```text
/chatlocalwork-runtime/windows-runner
```

## 주요 구성 요소

### LocalWorkMCP

`localworkmcp/`에 위치합니다.

- MCP Streamable HTTP endpoint 제공
- OAuth 2.1 / DCR / PKCE 처리
- Linux/Docker 파일 및 프로세스 도구 제공
- `windows_exec`를 통해 Windows Runner와 연결

LocalWorkMCP 자체 설명은 `localworkmcp/README.md`를 참고합니다.

### Windows Runner

`windows-runner/`에 위치합니다.

- Windows에서 허용된 실행 파일을 직접 실행
- PowerShell/cmd를 중간 셸로 사용하지 않음
- `shell:false` 고정
- 실행 파일 allowlist / blocklist 적용
- 요청/응답은 파일 큐로 교환

### ChatLocalWork Installer

`installer/ChatLocalWork.Installer/`에 위치합니다.

- Git / Docker Desktop / Tailscale 설치 책임
- ChatLocalWork 전용 Portable Node LTS 다운로드/배치
- GitHub `kzh8055G/chatlocalwork`를 `%LOCALAPPDATA%\ChatLocalWork\app\current`에 clone
- Manager에는 외부 의존성 설치 책임을 두지 않음

현재 Installer는 `--check`, `--install` 모드를 제공합니다.

### ChatLocalWork Manager

`manager/ChatLocalWork.Manager/`에 위치하는 .NET 8 WinForms 프로그램입니다.

- Start MCP / Stop MCP
- Windows Runner 상태
- Docker `workmachine` 상태
- Tailscale 상태
- Funnel 상태
- MCP `/health` 상태
- 설치형 환경 bootstrap 점검 및 최초 실행 설정
- 최초 설치형 실행에서 `app-config.json` 생성(기존 설정은 보존)
- 시작 시 환경이 READY인 경우에만 MCP 자동 Start
- `Tailscale 로그인` 버튼을 눌렀을 때만 `tailscale up` 실행
- 로그인 완료 후 `.env`를 구성하고 환경 READY이면 MCP 자동 Start
- Runner 로그 확인

설치형 실행 시 기본 경로는 다음과 같습니다.

```text
App       : %LOCALAPPDATA%\ChatLocalWork\app\current
Node      : %LOCALAPPDATA%\ChatLocalWork\tools\node\node.exe
Workspace : Documents\ChatLocalWorkWorkspace
```

### Start / Stop 자동화

`scripts/windows/`에 위치합니다.

StartMCP는 다음을 순서대로 준비합니다.

1. Windows Runner
2. Docker Desktop
3. 사용 가능한 MCP host port
4. `workmachine` build/start
5. Local MCP health
6. Tailscale service/client
7. Tailscale Funnel
8. ChatGPT-facing OAuth/MCP readiness

StopMCP는 반대 순서로 관련 리소스를 정리하고 Windows Runner도 종료합니다.

## 현재 기준 경로

프로젝트 예시:

```text
C:\Users\<user>\Documents\chat_local_workspace\chatlocalwork
```

Windows Runner runtime:

```text
%LOCALAPPDATA%\ChatLocalWork\runtime\windows-runner
```

Docker shared workspace:

```text
Windows: C:\Users\<user>\Documents\chat_local_workspace
Docker : /shared
```

## 시작 방법

일반적으로 Manager를 사용합니다.

```text
manager\ChatLocalWork.Manager\bin\Release\net8.0-windows\ChatLocalWork.Manager.exe
```

또는 직접:

```bat
scripts\windows\StartMCP.bat
```

중지는:

```bat
scripts\windows\StopMCP.bat
```

## 문서

- `docs/ARCHITECTURE.md` — 전체 구조와 데이터 흐름
- `docs/PROJECT_STATE.md` — 현재 구현/검증 상태와 남은 작업
- `docs/SECURITY.md` — Windows Runner 실행 보안 모델
- `docs/TROUBLESHOOTING.md` — Start/Runner/Docker/Tailscale 문제 해결
- `installer/README.md` — Installer / 의존성 bootstrap
- `manager/README.md` — Manager
- `windows-runner/README.md` — Windows Runner
- `localworkmcp/README.md` — MCP 서버 컴포넌트

## 저장소

`kzh8055G/chatlocalwork`
