# 프로젝트 상태

## 목표

ChatGPT 일반 채팅에서 cokacremote MCP를 통해 Windows 호스트의 개발 도구를 직접 실행한다.

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

- src/windows-runner-tools.ts
- windows-runner/runner.cjs
- windows-runner/runner-config.json
- docs/ARCHITECTURE.md
- docs/SECURITY.md
- docs/TROUBLESHOOTING.md
- /shared/MCP-AutoStart/StartMCP.cjs

## 다음 검증

새 코드는 작성 및 로컬 테스트까지 완료됐지만 현재 실행 중인 MCP 컨테이너는 이전 빌드일 수 있다.

다음 활성화 시:

1. StartMCP.bat 실행
2. StartMCP.cjs가 정식 Windows Runner 자동 시작
3. Docker workmachine 재빌드
4. ChatGPT에서 MCP 도구 새로고침
5. 새 windows_exec 스키마 확인
6. windows_exec로 git.exe --version 실행
7. windows_exec로 LocalMcpManager dotnet build 실행
8. Avast 탐지 여부 확인

성공하면 임시 WindowsRunnerTest.cjs / Start-WindowsRunner-Test.bat / .windows-runner-test 디렉터리를 삭제한다.

## 중요

새 빌드나 프로젝트마다 Runner에 액션 이름을 추가하지 않는다.

항상 범용 인터페이스를 사용한다.

```text
executable
args[]
workdir
timeoutMs
```
