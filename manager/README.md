# ChatLocalWork Manager

ChatLocalWork의 Windows 실행 환경을 관리하는 .NET 8 WinForms 프로그램입니다.

## 역할

Manager는 자체적으로 MCP 로직을 구현하지 않고 검증된 lifecycle 스크립트를 실행하고 각 구성 요소의 상태를 표시합니다.

주요 기능:

- Start MCP
- Stop MCP
- Windows Runner 상태 확인
- Docker `workmachine` 상태 확인
- Tailscale 상태 확인
- Tailscale Funnel 상태 확인
- Local MCP `/health` 확인
- Runner 로그 확인
- ChatLocalWork 프로젝트 폴더 열기

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

## 구조 변경 시 주의

Manager는 저장소 내부에서 다음 두 경로를 찾아 ChatLocalWork 프로젝트 루트를 판별합니다.

```text
scripts/windows/StartMCP.cjs
localworkmcp/tunneling/docker-compose.yml
```

프로젝트 디렉터리 구조를 변경할 때는 `AppPaths.cs`도 함께 확인해야 합니다.
