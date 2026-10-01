# ChatLocalWork Manager

ChatLocalWork의 Windows 실행 환경을 관리하는 .NET 8 WinForms 프로그램입니다.

## 역할

Manager는 자체적으로 MCP 로직을 구현하지 않고 검증된 lifecycle 스크립트를 실행하고 각 구성 요소의 상태를 표시합니다.

주요 기능:

- Manager 실행 시 MCP 자동 Start
- Manager 종료 시 MCP 자동 Stop(기본 ON)
- 수동 Start MCP
- 수동 Stop MCP
- Windows Runner 상태 확인
- Docker `workmachine` 상태 확인
- Tailscale 상태 확인
- Tailscale Funnel 상태 확인
- Local MCP `/health` 확인
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

- `appRoot`: GitHub Release에서 설치된 ChatLocalWork 앱 루트
- `workspaceRoot`: Windows Runner와 Docker `/shared`가 사용할 작업 공간
- `nodePath`: ChatLocalWork 전용 portable Node 경로(선택)
- `appVersion`: 설치된 Release 버전. Docker fast path의 source identity로 사용

예시는 `docs/app-config.example.json`을 참고합니다.

설정 파일이 없으면 개발 환경 호환을 위해 Manager 실행 위치의 상위 디렉터리에서 다음 두 파일을 찾아 저장소 루트를 자동 탐색합니다.

```text
scripts/windows/StartMCP.cjs
localworkmcp/tunneling/docker-compose.yml
```
