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
