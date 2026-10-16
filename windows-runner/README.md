# Windows Runner

Windows Runner는 LocalWorkMCP의 `windows_exec` 요청을 받아 Windows 호스트에서 허용된 실행 파일을 직접 실행하는 컴포넌트입니다.

자세한 전체 흐름은 `docs/ARCHITECTURE.md`를 참고합니다.

## 소스

```text
windows-runner/
├─ runner.cjs
├─ runner-config.json
└─ README.md
```

## 런타임

실행 중 생성되는 상태/로그/queue는 소스 폴더가 아니라 다음 위치에 저장됩니다.

```text
%LOCALAPPDATA%\ChatLocalWork\runtime\windows-runner
```

구조:

```text
windows-runner/
├─ state/
│  └─ ready.json
├─ logs/
│  ├─ runner.log
│  └─ launcher.log
├─ requests/
└─ responses/
```

이 런타임 폴더는 백업 대상이 아닙니다.

## 실행 방식

Runner는 Node.js `child_process.spawn`을 사용하고 `shell:false`로 실행합니다.

실행 파일 정책은:

```text
windows-runner/runner-config.json
```

에서 관리합니다.

## 일반 실행

직접 실행하기보다:

```text
scripts\windows\StartMCP.bat
```

또는 ChatLocalWork Manager가 자동으로 실행하는 방식을 사용합니다.

## 수동 실행 예

```bat
node runner.cjs ^
  --workspace-root C:\Users\<user>\Documents\chat_local_workspace ^
  --queue-dir C:\Users\<user>\AppData\Local\ChatLocalWork\runtime\windows-runner ^
  --config runner-config.json
```

실제 환경에서는 `%LOCALAPPDATA%`를 사용해 queue 경로를 구성하는 것이 권장됩니다.

## workspace와 workdir

`workspaceRoot`는 Windows Runner가 접근 가능한 프로젝트 작업공간의 루트입니다.

- 절대 `workdir`은 반드시 `workspaceRoot` 내부여야 합니다.
- 상대 `workdir`은 Runner 소스 폴더가 아니라 `workspaceRoot`를 기준으로 해석합니다.
- 따라서 일반 프로젝트를 `windows-runner/` 아래에 둘 필요가 없습니다.

예:

```text
workspaceRoot = C:\Users\<user>\Documents\chat_local_workspace
workdir       = MediaDownloaderApp

=> C:\Users\<user>\Documents\chat_local_workspace\MediaDownloaderApp
```

## Docker 연결

Windows runtime 폴더는 Docker에 다음 경로로 bind mount됩니다.

```text
/chatlocalwork-runtime/windows-runner
```

LocalWorkMCP는 같은 queue를 읽고 request/response JSON으로 Runner와 통신합니다.

## Runtime 안정성

- `ready.json`의 `heartbeatAt`을 약 2초마다 갱신합니다.
- heartbeat가 10초 이상 stale이면 StartMCP가 Runner를 재시작합니다.
- 24시간 이상 남은 orphan `requests/` / `responses/` 파일은 자동 삭제합니다.
- `runner.log`와 `launcher.log`는 약 4MB를 넘으면 최근 약 2MB만 유지합니다.
- 정상 Stop에서는 request/response queue와 `start-state.json`을 정리합니다.

## 명령 실행 승인 (하이브리드 정책)

- `runner-config.json`의 `allowedExecutables` 및 workspace 내부 절대경로 `.exe` 실행은 기존과 동일하게 처리합니다.
- `blockedExecutables`에 있는 셸/스크립트 실행기는 계속 차단합니다.
- 기타 프로그램은 Manager에 **이번만 허용 / 동일 명령 세션 허용 / whitelist 추가 / 거부** 선택 창이 표시됩니다.
- whitelist 추가는 Windows 호스트의 `%LOCALAPPDATA%\ChatLocalWork\runtime\approvals\trusted.json`에 **실제 실행 파일 절대 경로 + SHA-256**을 저장합니다. 프로그램이 업데이트되어 해시가 달라지면 다시 승인받습니다.
- 세션 허용은 실행 파일 경로, SHA-256, 인수 및 작업 디렉터리가 모두 동일한 경우에만 유효하며 Runner 재시작 시 사라집니다.
- 승인 요청/응답은 호스트 전용 `runtime/approvals/`에 저장하며 Docker 공유 queue에 노출하지 않습니다. Manager가 실행되지 않거나 사용자가 응답하지 않으면 최대 90초 후 거부합니다.
- `shell:false`, 작업 디렉터리 workspace 제한 및 실행 파일 차단 정책은 그대로 유지됩니다.
- 이 승인은 Windows Runner의 `windows_exec` 요청에만 적용됩니다. 기존 LocalWork의 다른 명령 실행 도구를 통한 시스템 접근을 막는 보안 경계는 아닙니다.

변경 사항을 적용하려면 Manager를 종료하고 실행 중인 Runner를 새 버전으로 재시작한 후 Manager를 다시 실행해야 합니다.
