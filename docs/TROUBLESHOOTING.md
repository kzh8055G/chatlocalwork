# Windows Runner 문제 해결

## Runner 상태

`.windows-runner/state/ready.json`을 확인한다.

정상 예:

```json
{
  "ok": true,
  "platform": "win32",
  "runtime": "node",
  "executionMode": "direct-process",
  "shell": false
}
```

## 로그

- `.windows-runner/logs/runner.log`
- `.windows-runner/logs/launcher.log`

## windows_exec 응답이 timeout 되는 경우

1. Runner 프로세스가 살아 있는지 확인
2. ready.json 확인
3. Docker에 /shared가 정상 마운트됐는지 확인
4. requests에 요청 JSON이 생성됐는지 확인
5. responses에 응답 JSON이 생성됐는지 확인

## Avast

PowerShell 기반 구형 Bridge는 IDP.HELU.PSE91 탐지를 유발했다.

새 Runner는 PowerShell/cmd를 거치지 않고 실행 파일을 직접 호출한다. git.exe --version, dotnet build, LocalMcpManager 실제 빌드/오류 수정/재빌드까지 테스트했으며 해당 테스트에서는 Avast 탐지가 발생하지 않았다.
