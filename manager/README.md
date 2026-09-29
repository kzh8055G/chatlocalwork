# LocalWorkMCP Manager

Windows에서 LocalWorkMCP 실행 환경을 관리하는 WinForms 관리 프로그램입니다.

## 현재 기능

- Windows Runner 상태 확인
- Docker `workmachine` 상태 확인
- Tailscale 연결 상태 확인
- Tailscale Funnel 상태 확인
- 로컬 MCP `/health` 확인
- 기존 `scripts/windows/StartMCP.cjs` 실행
- 기존 `scripts/windows/StopMCP.cjs` 실행
- Runner 로그 확인
- LocalWorkMCP 저장소 폴더 열기

현재 버전은 검증된 Start/Stop 스크립트를 재사용합니다. 향후 스크립트의 핵심 로직을 Manager 서비스 계층으로 단계적으로 옮길 수 있습니다.

## 빌드

```text
dotnet build manager\LocalWorkMCP.Manager\LocalWorkMCP.Manager.csproj -c Release
```
