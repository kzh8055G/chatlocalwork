# ChatLocalWork

ChatLocalWork는 일반 ChatGPT 대화에서 사용자의 로컬 Windows 개발 환경을 안전하게 읽고 수정하고 실행할 수 있도록 연결하는 프로젝트입니다.

## 구조

```text
ChatLocalWork/
├─ localworkmcp/        # MCP 서버 (기존 cokacremote 기반)
├─ manager/             # Windows 관리 UI
├─ windows-runner/      # 허용된 Windows 실행 파일을 직접 실행하는 Runner
├─ windows-bridge/      # 이전/호환 브리지 코드
├─ scripts/             # 전체 환경 Start/Stop 자동화
└─ docs/                # ChatLocalWork 전체 문서
```

## 구성 요소

### LocalWorkMCP

ChatGPT와 로컬 환경 사이의 MCP 서버입니다. Docker에서 실행되며 OAuth, HTTP MCP endpoint, 파일 도구와 Windows Runner 연동 도구를 제공합니다.

### ChatLocalWork Manager

Windows에서 Runner, Docker, Tailscale, Funnel, MCP 상태를 확인하고 전체 환경을 시작/중지합니다.

### Windows Runner

ChatGPT가 Windows 호스트에서 허용된 실행 파일을 `shell:false` 방식으로 직접 실행할 수 있게 합니다.

## 저장소

GitHub: `kzh8055G/chatlocalwork`
