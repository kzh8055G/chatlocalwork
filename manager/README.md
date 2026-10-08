# ChatLocalWork Manager

ChatLocalWork의 Windows 실행 환경을 관리하는 .NET 8 WinForms 프로그램입니다.

## 역할

Manager는 자체적으로 MCP 로직을 구현하지 않고 검증된 lifecycle 스크립트를 실행하고 각 구성 요소의 상태를 표시합니다.

주요 기능:

- Manager 실행 시 환경 bootstrap READY인 경우에만 MCP 자동 Start
- Manager 종료 시 MCP 자동 Stop(기본 ON)
- 수동 Start MCP
- 수동 Stop MCP
- Windows Runner 상태 확인
- Docker `workmachine` 상태 확인
- Tailscale 상태 확인
- Tailscale Funnel 상태 확인
- Local MCP `/health` 확인
- 설치형 환경 bootstrap 점검(App package / Node / Docker / Tailscale / MCP config)
- 메인 화면의 `환경 설정...` 버튼으로 별도 환경 설정 창 열기
- 환경 설정 창에서 현재 Workspace와 App source / Node / Docker / Tailscale / MCP config 상태 확인
- 설치형 최초 실행에서 `app-config.json` 자동 생성(기존 설정은 보존)
- `.chatlocalwork-version`이 있으면 `appVersion`에 반영
- Tailscale 미로그인 시 자동 Start 중단
- 환경 설정 창의 `Tailscale 로그인` 버튼을 사용자가 눌렀을 때만 `tailscale up` 실행
- Tailscale 로그인 완료 시 MCP `.env`의 public URL/workspace 설정 자동 생성 후 READY이면 MCP 자동 Start
- 환경 설정 창의 `Workspace 변경`으로 로컬 workspace를 선택하고 `manager-settings.json`에 override 저장
- workspace 변경은 Manager 재시작 후 Windows Runner/Docker `/shared`에 적용
- 외부 의존성 설치는 수행하지 않으며 `installer/`가 담당
- Runner 로그 확인
- 실행 로그 지우기 및 자동 길이 제한
- Start/Stop 작업 중 중복 실행 방지
- 자동 상태 새로고침 중복 방지
- Start/Stop 진행 중 구성 요소 상태를 완료 단계마다 즉시 갱신
- 전체 상태/마지막 확인 시각 표시
- Start/Stop 성공·실패·소요 시간 요약
- Start/Stop 현재 단계 실시간 표시
- 실패 시 실패 단계와 핵심 오류를 작업 상태 영역에 표시
- stale Runner ready.json 감지
- `Manager 종료 시 MCP도 종료` 옵션(기본 ON, LocalAppData에 저장)
- 종료 시 ChatLocalWork가 실행 중이면 StopMCP 자동 실행
- 자동 Stop 실패 시 `다시 시도 / Manager만 종료 / 취소` 선택 제공
- ChatLocalWork 앱 폴더 열기

## 사용 스크립트

```text
scripts/windows/StartMCP.cjs
scripts/windows/StopMCP.cjs
```

Manager는 Node.js로 위 스크립트를 직접 실행합니다.

## Runner 상태 경로

Manager가 확인하는 Windows Runner runtime:

```text
%LOCALAPPDATA%\ChatLocalWork\runtime\windows-runner
```

주요 파일:

```text
state\ready.json
logs\runner.log
logs\launcher.log
```

## 빌드

저장소 루트에서:

```text
dotnet build manager\ChatLocalWork.Manager\ChatLocalWork.Manager.csproj -c Release
```

Release 출력 예:

```text
manager\ChatLocalWork.Manager\bin\Release\net8.0-windows\ChatLocalWork.Manager.exe
```

## 경로 설정

설치형 환경에서는 다음 설정 파일을 우선 사용합니다.

```text
%LOCALAPPDATA%\ChatLocalWork\config\app-config.json
```

설정 항목:

- `appRoot`: Installer가 GitHub에서 clone한 ChatLocalWork 앱 루트
- `workspaceRoot`: Windows Runner와 Docker `/shared`가 사용할 작업 공간
- `nodePath`: ChatLocalWork 전용 portable Node 경로(선택)
- `appVersion`: Installer가 기록한 source commit. Docker fast path의 source identity로 사용

예시는 `docs/app-config.example.json`을 참고합니다.

Workspace를 UI에서 변경하면 `%LOCALAPPDATA%\\ChatLocalWork\\manager-settings.json`의 `workspaceRoot`가 `app-config.json` 또는 개발 모드 기본 workspace보다 우선합니다. 변경값은 Manager 재시작 후 적용됩니다.

설정 파일이 없으면 개발 환경 호환을 위해 Manager 실행 위치의 상위 디렉터리에서 다음 두 파일을 찾아 저장소 루트를 자동 탐색합니다.

```text
scripts/windows/StartMCP.cjs
localworkmcp/tunneling/docker-compose.yml
```
