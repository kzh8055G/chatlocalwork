# ChatLocalWork 프로젝트 상태

기준일: 2026-10-19

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

### Bootstrap 기반

- 저장소 외부에서 실행된 Manager도 설치형 기본 경로로 시작 가능
- 설치형 기본값: `%LOCALAPPDATA%\ChatLocalWork\app\current`
- 기본 workspace: 사용자 Documents의 `ChatLocalWorkWorkspace`
- Manager 메인 화면의 `환경 설정...` 버튼에서 별도 환경 설정 창을 열고 App source / Node / Docker / Tailscale / MCP config 상태 확인
- Manager 시작 시 bootstrap READY일 때만 MCP 자동 Start
- Tailscale 로그인이 필요한 경우 자동 `tailscale up`을 실행하지 않고 환경 설정 창의 `Tailscale 로그인` 버튼으로 명시적 사용자 동작을 요구
- 로그인 완료 후 `.env` 재구성/재점검을 수행하고 전체 READY이면 MCP 자동 Start
- 설치형 최초 실행에서 기본 app/workspace/node 경로를 `app-config.json`으로 영속화하고 기존 설정은 덮어쓰지 않음
- Installer의 `.chatlocalwork-version`이 있으면 commit hash를 `appVersion`으로 기록
- Tailscale이 설치·로그인되어 있고 app source가 준비된 경우 `.env`의 `MCP_PUBLIC_URL`과 `SHARED_PATH` 자동 구성
- Installer와 Manager 책임을 분리
- Manager는 환경 점검과 최초 실행 설정만 담당하며 Docker/Tailscale/Node 설치는 수행하지 않음
- 별도 `ChatLocalWork.Installer` 프로젝트 추가
- Installer `--check`로 Git / Docker Desktop / Tailscale / Portable Node / ChatLocalWork app 상태 확인
- Installer `--install`로 누락된 Git / Docker Desktop / Tailscale을 winget으로 설치
- Installer가 nodejs.org에서 현재 Windows x64 LTS를 조회해 Portable Node를 `%LOCALAPPDATA%\ChatLocalWork\tools\node`에 배치
- Installer가 GitHub `kzh8055G/chatlocalwork`의 `main`을 staging clone 후 `%LOCALAPPDATA%\ChatLocalWork\app\current`에 배치
- clone된 commit hash를 `.chatlocalwork-version`에 기록
- `--data-root`, `--workspace-root` override로 실제 사용자 설치 경로를 건드리지 않는 격리 설치 검증 지원

### 설치형 경로 분리

- `%LOCALAPPDATA%\ChatLocalWork\config\app-config.json` 기반 설치형 경로 지원
- `appRoot`와 `workspaceRoot` 분리
- Start/Stop이 `CHATLOCALWORK_WORKSPACE_ROOT`를 통해 동일 workspace 사용
- Windows Runner의 상대 `workdir`은 `windows-runner/`가 아니라 `workspaceRoot` 기준으로 해석하도록 수정
- Runner 명령 timeout 후 ChildProcess `close` 이벤트가 오지 않아 전체 큐가 영구 정지하는 문제를 방지하기 위해 3초 fallback watchdog 추가
- `ready.json`에 `busy` / `activeRequest` 상태 기록
- Manager가 active request의 timeout 초과를 감지해 Warning으로 표시
- MCP bridge가 stale active request를 감지하면 새 `windows_exec`를 오래 대기하지 않고 즉시 stuck 오류 반환
- 일반 개발 프로젝트는 ChatLocalWork 저장소 내부에 둘 필요 없이 workspace의 독립 폴더로 유지 가능
- Docker `SHARED_PATH`를 실행 시 workspaceRoot로 강제 전달하여 로컬 `.env` 절대경로 의존 제거
- ChatLocalWork 전용 portable Node 경로 지원
- 설치형 app clone은 Git HEAD 또는 `appVersion`을 source identity로 사용하여 fast path 유지
- 설정 파일이 없으면 기존 개발 저장소 탐색 방식으로 fallback

### Start/Stop 안정화

- 이미 전체 환경이 READY이고 Git 상태가 동일하면 Start 시 Docker rebuild를 생략하는 fast path
- Windows Runner `ready.json` heartbeat 기반 stale 감지 및 재시작
- 24시간 이상 orphan Runner queue 자동 정리
- `runner.log` / `launcher.log` 크기 제한
- Start 실패 시 StopMCP 자동 rollback
- 정상 Stop 시 runtime queue와 `start-state.json` 정리
- 이미 중지된 Tailscale 서비스에서 불필요한 UAC 요청 방지

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

- Installer Release build: 경고 0 / 오류 0
- Installer 격리 E2E 설치 성공: Git/Docker/Tailscale 기존 설치 감지 → Node.js LTS 다운로드 → GitHub clone → 전체 READY
- 격리 설치 후 `--check` 전체 READY 재확인
- 격리 clone HEAD가 당시 `origin/main` commit과 일치함을 확인
- Manager Release build: 경고 0 / 오류 0
- Tailscale `status --json` 읽기 전용 검증에서 현재 `BackendState=Running` 형식 확인
- Tailscale 로그인 명령은 테스트 중 실행하지 않아 현재 PC 로그인 상태를 변경하지 않음
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
- `installer/ChatLocalWork.Installer/`
- `manager/ChatLocalWork.Manager/`

## 남은 작업

현재 핵심 연결 구조는 동작한다. 이후 작업은 운영성/완성도 개선 성격이다.

- Installer 설치 완료 상태/재부팅 요구 처리 강화
- Installer 업데이트/롤백 정책 정리
- Manager 최초 실행 완료 후 Docker 재부팅/초기 실행 요구 상태 처리 강화
- 설치형 Manager 실제 PC/VM E2E 검증
- 최종 소형 설치 패키지/업데이트 전략 확정
- Start/Stop 오류 메시지와 로그 가독성 추가 개선
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

### Windows Runner 실행 승인 정책 (2026-10-16)

- 기존 whitelist는 유지하고, 미등록 Windows 실행 파일에 대한 Manager 승인 흐름을 추가했습니다.
- 새 파일: `windows-runner/approval-policy.cjs`, `approval-gate.cjs`, `approval.test.cjs`, `manager/ChatLocalWork.Manager/WindowsApprovalService.cs`.
- 한 번 허용 / 동일 명령 세션 허용 / 경로+SHA-256 신뢰 등록 / 거부를 지원합니다. 기존 blocked 목록은 그대로 유지합니다.
- Manager는 로컬 전용 approvals 폴더의 요청을 표시하고 결정을 기록합니다. 승인 시간 초과 시 자동 거부합니다.
- 검증: Windows Manager `dotnet build --no-restore` 성공(경고 0, 오류 0), Node 승인 정책 테스트 4개 통과.
- 실제 사용자 승인 팝업을 통한 종단간 UI 검증은 Runner와 Manager 재시작 후 필요합니다.

### Manager 버전 표기 (2026-10-16)

- Manager의 최초 명시 버전을 `0.1.0`으로 지정했습니다. `.csproj`의 `Version`, `AssemblyVersion`, `FileVersion`을 단일 기준으로 사용합니다.
- 실행 중인 어셈블리의 `AssemblyInformationalVersion`을 읽어 창 제목, 상단 설명, 시작 로그에 표시합니다. 파일 버전과 소스의 버전이 다를 경우 실행 중인 바이너리 버전이 표시됩니다.
- Release 빌드 성공(경고/오류 0), 실행 중인 Release Manager 교체 및 재실행을 확인했습니다.
- 이후 기능 변경에 따라 `.csproj`의 `Version`을 갱신합니다.

### REA 연동 준비 (2026-10-16)

- Codex에 REA 6.5.0 MCP 등록을 완료하고 Ghidra/JDK 경로를 Codex MCP 설정에 영구 반영했습니다.
- 독립 실행 도구는 `F:\Dev\PortableTool\REA` 폴더에 공식 Ghidra 12.1.4 및 Temurin JDK 25.0.4.1을 압축 해제했습니다. 전역 `JAVA_HOME`은 변경하지 않았습니다.
- Windows 환경의 REA `doctor`: Ghidra 12.1.4, JDK, native Windows control 모두 정상 확인.
- `CPS1NativeRecompiler/build/native/sample.exe` 대상으로 `rea inspect --provider ghidra` 검증 성공: 함수 570개, 문자열 286개, 메모리 세그먼트 8개. 분석은 읽기 전용입니다.
- ChatGPT에서 직접 사용하는 독립 HTTPS MCP 연결은 아직 등록 전입니다. 기존 Codex MCP 등록과 분리해 HTTP 브리지의 접근제어 및 원격 경로 매핑을 검증해야 합니다.

### REA 직접 연결용 HTTP 브리지 로컬 검증 (2026-10-16)

- `rea-bridge/`에 별도 Windows REA 6.5.0 stdio → Streamable HTTP 브리지를 구성했습니다. Codex의 기존 REA MCP 설정은 변경하지 않았습니다.
- `supergateway`를 프로젝트 전용 종속성으로 설치했으며 기본 포트 4188에서 `127.0.0.1`만 바인딩하고 호스트 전용 API 키 파일을 요구합니다.
- `rea-bridge/smoke.cjs` 실검증: API 키가 없는 HTTP 요청은 401, API 키가 있는 `initialize` 요청은 HTTP 200 및 REA 6.5.0 serverInfo 반환.
- 아직 외부 HTTPS/Tailscale Funnel로 공개하거나 ChatGPT 앱에 등록하지 않았습니다. 공개 전에 OAuth 인증·인가, tool allowlist, 안정적인 lifecycle, 외부 노출 경계 검증이 필요합니다.

### REA Bridge 읽기 전용 도구 정책 (2026-10-16)

- 207개 기본 도구 중 바이너리 개요·절차·문자열·어셈블리·디컴파일 등을 위한 **20개 읽기 중심 도구**만 노출하도록 `supergateway --tools` 필터를 적용했습니다.
- Windows 실검증: 미인증 401, 인증된 초기화 200, tools/list 20개, 차단된 쓰기 도구 `set_comment` 직접 호출 거부 확인.
- OAuth를 통한 원격 ChatGPT 앱 등록·외부 HTTPS 공개는 별도 인증 검증이 필요해 미실시. LocalWork의 기존 OAuth/Tailscale 동작은 변경하지 않았습니다.

### REA 원격 OAuth 게이트웨이 프로토타입 (2026-10-16)

- `rea-bridge/oauth-gateway.cjs`에 별도 OAuth2 PKCE 승인/클라이언트 등록/액세스 토큰·리프레시 토큰/보호 리소스 메타데이터 및 REA MCP 프록시를 구현했습니다. loopback:4189에서만 수신합니다.
- `node --test rea-bridge/oauth-gateway.test.cjs` 로컬 자동 테스트 통과: 미인증 401, 잘못된 PKCE 및 resource 거부, 코드 1회 사용, 리프레시 회전, 인증된 업스트림 요청 전달.
- 아직 프로토타입으로 인가 서버/토큰 메모리 지속성 등 추가 보안 검증이 필수이므로 **공개 HTTPS 경로에 연결하지 않았습니다.** ChatGPT 앱에 등록하지 않았으며 기존 LocalWork OAuth, Funnel, Manager는 그대로 유지했습니다.

### LocalWork 범용 MCP Hub v0.2.0 (2026-10-19)

- `docs/MCP_HUB.md`에 아키텍처와 사용 방법 기록.
- MCP Registry 도입 당시 `/shared/.chatlocalwork/mcp-servers.json`을 사용했으나, 현재는 `%LOCALAPPDATA%\ChatLocalWork\config\mcp-servers.json`(Docker: 읽기 전용 `/chatlocalwork-config/mcp-servers.json`)으로 이전했습니다. 서버 조회·도구 조회·도구 호출 3개를 고정 MCP 도구로 제공. HTTP Streamable 및 Windows REA 큐 전용 전송 방식 제공.
- Manager에 MCP 서버 관리 화면 및 REA 등록 템플릿 추가, 실행 어셈블리 버전 0.2.0.
- Windows Runner가 REA stdio MCP 프로세스를 유지하며, Docker↔Windows 큐로 21개 지정 도구 호출. 바이너리 선택은 Workspace 내부 실파일로 제한.
- 검증: HTTP 모의 MCP 중계 테스트, Registry 유효성 검사, Windows 격리 큐 스모크 테스트 및 Docker LocalWork→Windows Runner→REA 6.5.0→Ghidra 12.1.4→sample.exe 실제 함수 목록 조회 성공.
- 미구현: 범용 Windows stdio 서버 자동 가져오기, 일반 HTTP MCP 상태 유지, REA 위험 도구 개별 승인, 설치/업데이트 자동화.


### MCP Hub Registry 경로 이전 및 Codex 설정 가져오기 (2026-10-19)

**현재 기준 정보:** 이 항목이 앞선 2026-10-19 MCP Hub v0.2.0 초기 기록보다 최신이며, 경로·지원 상태가 충돌하면 이 항목과 `docs/MCP_HUB.md`를 우선합니다.

- 기존 Workspace의 `.chatlocalwork/mcp-servers.json`을 `%LOCALAPPDATA%\ChatLocalWork\config\mcp-servers.json`으로 이전했습니다. `StartMCP.cjs`는 신규 파일이 없을 때 기존 파일을 한 번 복사하도록 구성했고, Docker는 호스트 config 디렉터리를 별도 읽기 전용 bind mount로 사용합니다.
- 기존 `.rea-diagnostics`는 `%LOCALAPPDATA%\ChatLocalWork\runtime\diagnostics`로 백업한 후 Workspace에서 정리했습니다.
- LocalWork 실행 환경에서 새 Registry와 REA 활성화 상태를 확인했습니다. Manager의 Registry 경로 수정 빌드 및 재시작 확인 완료.
- Windows Runner의 허용 목록에 `mame.exe`를 추가하고 Manager의 실행 승인 대화상자를 전면 표시하도록 변경했습니다. Manager Release 빌드 통과. 사용자 화면에서 승인 동작을 클릭하는 실제 UI E2E 검증은 별도입니다.
- Manager에 **Codex 설정 가져오기** 버튼을 구현했습니다. `%USERPROFILE%\.codex\config.toml`의 MCP 등록을 조회해 기존 REA 연결과 로컬 HTTP MCP에 한하여 **비활성 Registry 항목**으로 가져옵니다. 현재 입력 범위는 일반적인 단일 행 문자열·배열·env 섹션입니다.
- Codex 가져오기 구현은 `manager/ChatLocalWork.Manager/CodexMcpImporter.cs`, `McpHubForm.cs`에 있습니다. 파서 스모크 테스트와 Manager Release 빌드는 모두 통과했습니다. **최초 구현 시점에는 운영 배포 전이었으며, 이후 v0.3.0 Manager 실행 파일로 교체했습니다. 실제 버튼 클릭 UI E2E 검증은 별도입니다.**
- **미구현:** 범용 Windows stdio MCP 중계/실행 승인, Codex·VS Code의 모든 MCP 종류 가져오기, 완전한 TOML 문법 파싱, HTTP MCP 장기 세션·원격 인증, REA의 쓰기 등 추가 위험 도구 승인.

### 문서 갱신 원칙

- `README.md`: 현재 사용 가능한 기능, 경로 및 시작 방법 요약.
- `docs/PROJECT_STATE.md`: 현재 구현/테스트/배포 상태, 제약 및 다음 작업. 이전 기록은 남기되 최신 변경에서 정정하고 기준일을 갱신.
- `docs/MCP_HUB.md`: MCP Registry 규격, 도구 동작, Windows REA/Codex 가져오기 절차와 권한 정책.
- `CHANGELOG.md`: 최신 기능 변경을 `Unreleased`에 추가하고, 릴리스 시 버전별 항목으로 이동.
- 기능 작업 커밋 전후로 위 문서들의 표현과 실제 코드의 일치 여부를 확인합니다. Git 커밋 본문은 한글·영문 병기합니다.


### 범용 Windows stdio MCP Hub v0.3.0 (2026-10-19)

- `localworkmcp/src/mcp-hub.ts`에 `windowsStdio` 전송 방식을 추가했습니다. ChatGPT 노출 도구 이름/인자(`mcp_list_servers`, `mcp_list_tools`, `mcp_call_tool`)는 변경하지 않았습니다.
- `windows-runner/stdio-mcp-client.cjs`가 stdio 서버별 연결·요청·세션 수명주기를 관리하고, `stdio-hub.cjs`가 승인된 서버 프로필과 도구 호출을 중계합니다. 기존 REA 브리지는 공통 클라이언트를 사용하도록 변경했으며 읽기 제한 정책을 유지했습니다.
- Manager의 MCP 관리 화면에 **Windows stdio MCP** 유형, 실행 파일·인수·Workspace 폴더·환경 변수 이름·도구 목록, 명시적인 실행 승인 절차를 추가했습니다. 실행 설정은 Docker 공유 폴더 외부의 `%LOCALAPPDATA%\ChatLocalWork\private\mcp-stdio-profiles.json`에 저장합니다.
- Codex에서 일반 stdio MCP를 가져오면 이름·ID만 비활성 초안으로 등록합니다. 임의 명령·인수·비밀 환경 변수는 자동 가져오지 않습니다. 실행 시 사용자가 별도로 명령을 구성하고 승인해야 합니다.
- Windows 단위 테스트와 Windows Runner 큐 통합 테스트에서 허용 도구 조회·호출 성공, 차단 도구·미승인 프로필·비활성 서버·Workspace 밖 경로·차단된 exe 거부를 검증했습니다. 기존 REA → Ghidra 12.1.4에서 `sample.exe` 열기·함수 목록 회귀 테스트 통과, Manager Release 빌드 및 TypeScript 검사 통과.
- **운영 배포 확인:** Manager v0.3.0 실행 파일을 백업 후 교체하고 새 프로세스 실행을 확인했습니다. Docker LocalWork를 재빌드·재생성했고, Windows Runner도 새 코드로 재시작했습니다. 운영 허브에서 REA `mcp_list_tools`, `open_binary(sample.exe)`, `list_documents`, `list_procedures` 호출이 모두 성공했습니다. **일반 stdio 서버의 운영 등록은 아직 없으며**, 그 경로는 격리된 Windows Runner 큐 통합 테스트로 검증했습니다.
- **후속:** VS Code MCP 설정 가져오기, 정교한 실행 인수/환경 변수 편집, MCP 서버별 감사 로그, HTTP MCP 상태 유지/원격 인증, 더 세밀한 위험 도구 승인 정책. 현재 설정은 서버 프로세스에 Windows 실행 권한을 부여하므로 신뢰한 서버에만 사용해야 합니다.


### Windows stdio MCP 도구 조회 기능 v0.4.0 (2026-10-19)

- Manager에 `도구 조회...` 버튼과 `McpStdioInspector.cs`를 추가했습니다. 사용자 확인을 받은 다음 별도 Windows `.exe` MCP 프로세스를 시작하고 `initialize`·`tools/list`만 실행하여 도구 이름을 확인하며, 사용자가 선택한 도구만 허용 목록에 추가합니다. 조회만으로 실행 프로필을 승인하거나 활성화하지 않습니다.
- `manager/McpStdioInspector.Smoke`에서 모의 MCP 서버로 도구 목록을 받아오고 `cmd.exe` 같은 차단된 실행 파일을 거부하는 테스트 통과. Manager Release 빌드 통과.
- **Codex node_repl 실서버 미검증:** Codex 설정에 `node_repl` MCP가 등록된 것은 확인했으나, Codex 런타임 실행 파일을 확인/실행하는 로컬 도구 요청이 안전 검사에 차단돼 실제 호출은 진행하지 않았습니다. 차단 우회 없이 향후 Manager의 사용자 수동 승인/테스트를 통해 확인해야 합니다.
- **운영 배포 상태:** Manager v0.4.0 실행 파일 5개를 교체했고, 이전 빌드는 `%LOCALAPPDATA%\ChatLocalWork\runtime\diagnostics\manager-before-v0.4.0`에 백업했습니다. 새 Manager 프로세스 정상 시작 확인. GUI 버튼을 사람이 클릭하는 종단간 UI 테스트와 Codex `node_repl` 실서버 호출은 수행하지 않았습니다.


### 독립 MCP 실서버 테스트 코드 추가 (2026-10-19)

- `windows-runner/everything-stdio.integration.test.cjs`를 추가했습니다. 별도 설치된 공식 `@modelcontextprotocol/server-everything@1.0.0`에 대해 Windows의 기존 `StdioMcpClient`로 `initialize`, `tools/list`, `echo`, `get-sum(17,25)`를 검증하는 재현 가능한 테스트입니다.
- 설치 대상은 `%LOCALAPPDATA%\ChatLocalWork\tools\mcp-everything`이며 실제 Windows 런타임의 Node.js를 사용합니다. 서버 실행 경로나 실행 프로필에 개인 사용자 이름을 하드코딩하지 않습니다.
- **상태: 검증 완료.** 기존 테스트 파일을 Windows에서 직접 실행해 Everything MCP 1.0.0의 `tools/list` 13개 도구, `echo`, `get-sum(17,25)=42` 호출을 확인했습니다. 추가로 격리된 Windows Runner 큐와 LocalWork Hub의 `mcp_list_tools`/`mcp_call_tool`을 통해 동일 서버를 호출하는 E2E 테스트도 통과했습니다.
- 운영 `mcp-servers.json`과 Windows 승인 프로필을 변경하지 않았고, 기존 REA 설정도 유지했습니다. 향후 승인된 실행 흐름에서 테스트 성공을 확인한 후에만 운영 Registry에서 `echo`, `get-sum` 등의 최소 허용 도구를 수동 등록합니다.


### LocalWork 안전 검사 오탐 대응 정리 (2026-10-19)

- 최근 Git/파일 작업 자체가 막힌 원인을 점검했습니다. `write_file`/삭제, `git status`, 단순 `node -e`, `%LOCALAPPDATA%` 존재 확인은 모두 정상 동작했습니다.
- `exec_command`의 “unrestricted shell command” 설명과 파일 도구의 광범위한 범위는 최근 추가된 것이 아니라 초기 저장소부터 존재했습니다. 최근 MCP Hub 변경으로 새로 넓어진 권한이 아닙니다.
- 차단이 집중된 시점은 Codex/OpenAI 앱 전용 런타임 경로를 자동 탐색·실행하려는 요청과, 그 작업을 inline 스크립트에서 설정 탐색·프로세스 실행·후속 파일/Git 작업까지 묶은 시점이었습니다. 안전 검사 내부 판정 사유는 제공되지 않으므로 정확한 규칙 자체는 확인할 수 없습니다.
- 재발 방지로 루트 `AGENTS.md`에 프로젝트 작업 규칙을 추가했습니다. 파일 수정은 전용 파일 도구, Git은 `git.exe` 직접 호출, Windows 런타임 상태 확인은 `scripts/windows/InspectRuntimeState.cjs`를 우선 사용합니다.
- `InspectRuntimeState.cjs`는 Registry/승인 프로필/Runner 상태의 비민감 메타데이터만 표시하고 실행 경로·인수·환경 변수 값·비밀값을 출력하지 않습니다. 실제 운영 환경에서 정상 실행을 확인했습니다.
- 현재 Registry의 `node_repl` 항목은 `windowsStdio` 타입의 **비활성 초안**이며 허용 도구가 0개입니다. 호스트 전용 stdio 승인 프로필 파일은 존재하지 않아 자동 실행 상태가 아닙니다. 기존 REA는 활성화 상태를 유지합니다.


### 공식 Everything MCP 실서버 검증 완료 (2026-10-19)

이 항목은 위의 "독립 MCP 실서버 테스트 코드 추가"에서 남아 있는 미완료 표현보다 최신 상태입니다.

- 공식 `@modelcontextprotocol/server-everything@1.0.0`을 Windows에서 실제 실행했습니다.
- 직접 stdio 검증에서 `tools/list` 13개 도구를 확인했고 `echo`, `get-sum(17,25)=42` 호출이 성공했습니다.
- 격리된 Windows Runner 큐에서 동일 서버를 실행하여 `echo`, `get-sum` 호출과 미허용 도구 거부를 확인했습니다.
- LocalWork Hub의 실제 `mcp_list_tools` / `mcp_call_tool` 코드를 포함한 독립 E2E 테스트도 통과했습니다.
- E2E는 임시 디렉터리와 임시 Registry를 사용했고 테스트 종료 시 정리했습니다. 운영 Registry에는 Everything MCP를 추가하지 않았으며 기존 REA 설정도 유지했습니다.
- 검증 파일: `windows-runner/everything-stdio.integration.test.cjs`, `windows-runner/stdio-runner-real.integration.test.cjs`, `windows-runner/everything-localwork-hub.e2e.mjs`.


### 저장소 줄바꿈 정책 정리 (2026-10-19)

- Windows 전역 Git 설정이 `core.autocrlf=true`인 상태에서 Docker/Linux 도구가 LF로 파일을 기록하면, 실제 내용 diff가 없어도 Windows Git이 수십 개 파일을 수정으로 표시하는 현상을 확인했습니다.
- 루트 `.gitattributes`에 `* text=auto eol=lf`를 추가해 추적 텍스트 파일의 작업 트리 줄바꿈을 LF로 고정했습니다. 기존 Dockerfile/쉘 스크립트 LF 정책도 유지합니다.
- 목적은 Windows/컨테이너 양쪽에서 같은 파일을 편집해도 불필요한 CRLF↔LF 변환이 반복되지 않게 하는 것입니다.


### ChatGPT 도구 새로 고침 영속 진단 v0.4.1 (2026-10-20)

- 간헐적으로 발생하는 ChatGPT의 `도구 새로 고침` 실패를 사후 분석할 수 있도록 MCP/OAuth 접근 진단을 영속화했습니다.
- Docker의 `/chatlocalwork-diagnostics/mcp-access.ndjson`를 Windows 호스트의 `%LOCALAPPDATA%\ChatLocalWork\runtime\diagnostics\mcp-access\mcp-access.ndjson`에 bind mount합니다. 컨테이너를 재생성해도 로그가 유지됩니다.
- 기록 항목은 시간, 요청 ID, HTTP method/path, MCP RPC method, 도구 이름, MCP protocol version, User-Agent, HTTP status, 처리 시간, 완료/중단 여부입니다. Authorization 헤더, access/refresh token, OAuth code, 도구 arguments 및 요청 본문은 기록하지 않습니다.
- Manager v0.4.1에 **ChatGPT 진단 로그** 버튼을 추가했습니다. 최근 영속 로그를 읽어 로컬 시간 기준으로 표시하고 마지막 `openai-*` User-Agent 요청을 요약합니다.
- 로그는 8 MiB에 도달하면 `.1` 파일로 best-effort 회전합니다.
- 실제 운영 컨테이너에서 영속 파일 생성과 `openai-mcp/1.0.0`의 `initialize`, `notifications/initialized`, `tools/call` 기록을 확인했습니다. 호스트 diagnostics bind mount도 확인했습니다.
- Linux 격리 전체 회귀는 **11개 파일 / 51개 테스트 통과**, LocalWork TypeScript build 통과, Manager Release build는 경고 0 / 오류 0으로 확인했습니다.
- 작업 중 한 차례 LocalWork 클라이언트 호출이 `Connection failed`로 종료됐지만, 연결 복구 후 확인한 영속 MCP 로그에는 해당 실패 호출에 대응하는 서버 수신 기록이 없었습니다. 따라서 적어도 일부 간헐 실패는 LocalWork의 HTTP request handler에 도달하기 전 연결 계층에서 발생할 가능성이 있습니다. 다음 실패 때 이 로그를 기준으로 OpenAI 요청 도달 여부를 즉시 구분할 수 있습니다.
