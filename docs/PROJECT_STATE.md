# ChatLocalWork 프로젝트 상태

기준일: 2026-09-30

## 목표

일반 ChatGPT Chat에서 로컬 Windows 개발 환경의 파일을 읽고 수정하고, 빌드/테스트/도구 실행까지 수행할 수 있게 한다.

## 현재 확정 구조

```text
ChatGPT
  -> Tailscale Funnel
  -> LocalWorkMCP (Docker)
  -> file queue
  -> Windows Runner
  -> direct Windows process execution (shell:false)
```

전체 제품 이름은 **ChatLocalWork**이고, MCP 서버는 그 하위 컴포넌트인 **LocalWorkMCP**다.

## 현재 저장소 구조

```text
chatlocalwork/
├─ localworkmcp/
├─ manager/
│  └─ ChatLocalWork.Manager/
├─ windows-runner/
├─ scripts/windows/
├─ docs/
└─ README.md
```

## 완료된 핵심 작업

### MCP / Docker

- LocalWorkMCP 프로젝트명 정리
- Docker 기반 `workmachine` 실행
- Streamable HTTP MCP endpoint
- OAuth 2.1 / DCR / PKCE
- `/health` readiness
- Tailscale Funnel 연결
- 기본 host port 3999 + 자동 fallback
- Dockerfile LF 강제용 `.gitattributes`

### Windows Runner

- PowerShell Bridge 폐기
- Node direct-process Runner 구현
- `shell:false` 고정
- generic `windows_exec(executable, args[], workdir, timeoutMs)`
- allowlist / blocklist
- workspace 경로 제한
- timeout / output limit
- 파일 queue request/response
- Runner 로그 및 ready state

### Runner runtime 분리

기존:

```text
chat_local_workspace\.windows-runner
```

현재:

```text
%LOCALAPPDATA%\ChatLocalWork\runtime\windows-runner
```

Docker mount:

```text
/chatlocalwork-runtime/windows-runner
```

구 `.windows-runner` 폴더는 제거했고, StopMCP에는 마이그레이션 호환 cleanup만 남겨두었다.

### Manager

- .NET 8 WinForms 기반 `ChatLocalWork.Manager`
- Start MCP / Stop MCP
- Windows Runner 상태
- Docker `workmachine` 상태
- Tailscale 상태
- Funnel 상태
- MCP health 상태
- Runner 로그 표시
- 프로젝트 폴더 열기

### 프로젝트 재구성

기존 단일 LocalWorkMCP 중심 구조를 ChatLocalWork 상위 프로젝트 구조로 변경했다.

- `localworkmcp/` → MCP 서버 컴포넌트
- `windows-runner/` → Windows 호스트 컴포넌트
- `manager/` → 운영 UI
- `scripts/` → 전체 시스템 lifecycle
- `docs/` → 전체 프로젝트 문서

GitHub 저장소:

```text
kzh8055G/chatlocalwork
```

## 현재 검증 완료

- Manager Release build: 경고 0 / 오류 0
- Manager에서 Start MCP 성공
- LocalWorkMCP 연결 성공
- `windows_exec`를 통한 `git.exe --version` 성공
- 새 `chatlocalwork` 저장소에서 `git status` 성공
- Docker build 성공
- Docker workmachine health 정상
- Runner runtime LocalAppData mount 정상
- 새 runtime `ready.json` 확인
- 구 `.windows-runner` 제거 후 새 runtime 사용 확인
- 기존 `localworkmcp` 루트 clone 제거

## 현재 Runner runtime 예

```text
%LOCALAPPDATA%\ChatLocalWork\runtime\windows-runner
```

이 폴더에는 소스가 아니라 다음 실행 데이터만 존재한다.

- `state/ready.json`
- `logs/`
- `requests/`
- `responses/`

백업 대상이 아니다.

## 현재 주요 파일

- `README.md`
- `docs/ARCHITECTURE.md`
- `docs/PROJECT_STATE.md`
- `docs/SECURITY.md`
- `docs/TROUBLESHOOTING.md`
- `scripts/windows/StartMCP.cjs`
- `scripts/windows/StopMCP.cjs`
- `windows-runner/runner.cjs`
- `windows-runner/runner-config.json`
- `localworkmcp/src/windows-runner-tools.ts`
- `localworkmcp/tunneling/docker-compose.yml`
- `manager/ChatLocalWork.Manager/`

## 남은 작업

현재 핵심 연결 구조는 동작한다. 이후 작업은 운영성/완성도 개선 성격이다.

- Manager UI/UX 개선
- 설치/배포 패키지 검토
- 오래된 runtime queue 파일 자동 정리 정책
- Start/Stop 오류 메시지와 로그 가독성 개선
- 필요 시 desktop GUI control 기능 검토
- 문서와 설정 간 drift 방지

## 유지해야 할 원칙

Windows 실행 기능을 추가할 때 특정 작업 이름별 action을 만들지 않는다.

범용 인터페이스를 유지한다.

```text
executable
args[]
workdir
timeoutMs
```

새 실행 파일이 필요하면 우선 `windows-runner/runner-config.json`의 allowlist 정책을 검토한다.
