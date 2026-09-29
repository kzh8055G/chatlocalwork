# Windows Runner 아키텍처

## 목적

일반 ChatGPT 채팅의 MCP 도구가 Windows 호스트에서 Git, .NET, CMake 같은 개발 도구를 직접 실행할 수 있게 한다.

## 최종 구조

```text
ChatGPT
  -> cokacremote MCP (Docker)
  -> /shared/.windows-runner/requests/<id>.json
  -> Windows Node Runner
  -> executable + args[] (shell:false)
  -> /shared/.windows-runner/responses/<id>.json
  -> MCP
  -> ChatGPT
```

## 기존 PowerShell Bridge를 폐기한 이유

기존 방식은 MCP 요청을 Windows에서 PowerShell 프로세스로 전달했다. Avast가 이 실행 경로를 IDP.HELU.PSE91로 반복 탐지했다.

검증 과정에서 PowerShell/cmd를 사용하지 않고 Node의 child_process.spawn(..., { shell:false })로 git.exe와 dotnet.exe를 직접 실행하면 Avast 탐지가 발생하지 않았다.

실제 LocalMcpManager 빌드에서도 다음 흐름을 검증했다.

1. ChatGPT가 MCP를 통해 빌드 요청 생성
2. Windows Runner가 dotnet.exe를 직접 실행
3. 컴파일 오류 stdout 수집
4. ChatGPT가 소스 수정
5. 다시 빌드
6. 경고 0 / 오류 0 확인

따라서 Windows 실행 경로는 direct-process 방식으로 통일한다.

## 설계 원칙

- PowerShell과 cmd.exe를 사용하지 않는다.
- Windows Runner는 shell:false로만 실행한다.
- MCP와 Runner 간 통신은 HTTP 포트가 아니라 공유 디렉터리 파일 큐를 사용한다.
- 작업 디렉터리는 chat_local_workspace 내부로 제한한다.
- 일반 실행 파일은 allowlist로 관리한다.
- workspace 내부에서 빌드된 exe는 절대 경로로 실행할 수 있다.
- timeout과 stdout/stderr 출력 크기 제한을 적용한다.
- 요청마다 UUID를 사용한다.

## 주요 파일

- src/windows-bridge.ts
  - MCP의 windows_exec 구현
  - 요청 JSON 생성 및 응답 대기
- windows-runner/runner.cjs
  - Windows 호스트 프로세스 실행
- windows-runner/runner-config.json
  - 실행 파일 allowlist / blocklist / timeout 설정
- /shared/MCP-AutoStart/StartMCP.cjs
  - Windows Runner, Docker, Tailscale Funnel 자동 시작
- tunneling/docker-compose.yml
  - MCP 컨테이너에 동일한 /shared 큐 마운트

## 큐 구조

```text
.windows-runner/
  requests/
  responses/
  state/
    ready.json
  logs/
    runner.log
    launcher.log
```

정상 처리된 요청/응답 파일은 MCP가 결과를 읽은 뒤 제거한다.

## windows_exec 인터페이스

```text
executable: string
args: string[]
workdir: string
timeoutMs: number
```

예:

```text
executable = dotnet.exe
args = ["build", "LocalMcpManager.csproj", "-c", "Release"]
workdir = C:\Users\...\chat_local_workspace\LocalMcpManager
```

새 프로젝트를 빌드할 때 Runner에 별도 액션을 추가하지 않는다. executable과 args만 변경한다.
