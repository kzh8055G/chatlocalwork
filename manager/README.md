# ChatLocalWork Manager

ChatLocalWork의 Windows 실행 환경을 관리하는 WinForms 프로그램입니다.

## 현재 기능

- Windows Runner 상태 확인
- Docker `workmachine` 상태 확인
- Tailscale 연결 상태 확인
- Tailscale Funnel 상태 확인
- 로컬 MCP `/health` 확인
- `scripts/windows/StartMCP.cjs` 실행
- `scripts/windows/StopMCP.cjs` 실행
- Runner 로그 확인
- ChatLocalWork 프로젝트 폴더 열기

현재 버전은 검증된 Start/Stop 스크립트를 재사용합니다.

## 빌드

```text
dotnet build manager\ChatLocalWork.Manager\ChatLocalWork.Manager.csproj -c Release
```
