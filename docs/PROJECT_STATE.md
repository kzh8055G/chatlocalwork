# 프로젝트 상태

## 목표

ChatGPT 일반 채팅에서 localworkmcp MCP를 통해 Windows 호스트의 개발 도구를 직접 실행한다.

## 확정된 실행 구조

```text
ChatGPT
  -> MCP Server (Docker)
  -> shared file queue
  -> Windows Node Runner
  -> direct process execution (shell:false)
```

PowerShell/cmd 기반 실행 경로는 폐기했다.

## 2026-09-28 완료

- PowerShell Bridge 방식의 Avast IDP.HELU.PSE91 문제 조사
- 파일 큐 방식 PoC 성공
- git.exe --version 실제 Windows 실행 성공
- dotnet.exe build 실제 Windows 실행 성공
- LocalMcpManager 실제 빌드 오류 83개 수집
- DockerService.cs 문자열 오류 수정
- LocalMcpManager 재빌드 성공: 경고 0 / 오류 0
- 정식 범용 Windows Runner 구현
- MCP windows_exec를 executable + args[] + workdir + timeoutMs 구조로 변경
- HTTP Bridge URL/토큰 제거
- StartMCP.cjs에서 Runner 자동 시작 구현
- 실행 파일 allowlist / blocklist 적용
- workspace 경로 제한 적용
- timeout / 출력 제한 / 로그 적용
- npm typecheck 성공
- npm test 성공
- npm build 성공

## 현재 주요 파일

- localworkmcp/src/windows-runner-tools.ts
- windows-runner/runner.cjs
- windows-runner/runner-config.json
- docs/ARCHITECTURE.md
- docs/SECURITY.md
- docs/TROUBLESHOOTING.md
- scripts/windows/StartMCP.cjs
- scripts/windows/StopMCP.cjs

## 현재 검증 상태

- StartMCP/StopMCP 재시작 후 localwork 정상
- Windows Runner를 통한 `git.exe --version` 정상
- PC 재부팅 후 StartMCP 실행 및 localwork 복구 정상
- Docker host port 충돌 시 사용 가능한 포트 자동 선택
- Tailscale 서비스 및 클라이언트 자동 시작
- 선택된 host port로 Tailscale Funnel 자동 구성

## 중요

새 빌드나 프로젝트마다 Runner에 액션 이름을 추가하지 않는다.

항상 범용 인터페이스를 사용한다.

```text
executable
args[]
workdir
timeoutMs
```
