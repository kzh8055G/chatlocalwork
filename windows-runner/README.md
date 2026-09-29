# Windows Runner

자세한 구조는 `docs/ARCHITECTURE.md`를 참고한다.

실행 예:

```bat
node runner.cjs --workspace-root C:\Users\...\chat_local_workspace --queue-dir C:\Users\...\chat_local_workspace\.windows-runner --config runner-config.json
```

일반적으로 직접 실행하지 않고 `scripts\windows\StartMCP.bat`이 자동 시작한다.
