# ChatLocalWork Installer

ChatLocalWork의 설치 단계 전용 bootstrapper입니다.

## 책임

Installer가 담당합니다.

- Git 설치 여부 확인 및 winget 설치
- Docker Desktop 설치 여부 확인 및 winget 설치
- Tailscale 설치 여부 확인 및 winget 설치
- ChatLocalWork 전용 Portable Node LTS 다운로드/배치
- GitHub `kzh8055G/chatlocalwork`의 `main`을 설치 경로에 clone
- 설치 대상 workspace 디렉터리 준비

Manager는 설치가 끝난 뒤 최초 실행에서 기본 경로를 `app-config.json`에 저장하고, Tailscale 로그인 상태와 MCP public URL, `.env` 등 실행 설정을 구성합니다.

## 설치 경로

기본값:

```text
%LOCALAPPDATA%\ChatLocalWork\
├─ app\current\
│  ├─ localworkmcp\
│  ├─ scripts\
│  ├─ windows-runner\
│  └─ .chatlocalwork-version
└─ tools\node\node.exe

Documents\ChatLocalWorkWorkspace\
```

## 사용법

점검만 수행:

```text
ChatLocalWork.Installer.exe --check
```

설치:

```text
ChatLocalWork.Installer.exe --install
```

격리 테스트 또는 개발 검증에서는 실제 LocalAppData를 건드리지 않도록 경로를 바꿀 수 있습니다.

```text
ChatLocalWork.Installer.exe --install ^
  --data-root C:\Temp\ChatLocalWork-Test ^
  --workspace-root C:\Temp\ChatLocalWork-Workspace
```

## 설치 방식

- Git / Docker Desktop / Tailscale: winget
- Portable Node: nodejs.org의 현재 Windows x64 LTS ZIP
- ChatLocalWork app: GitHub 저장소를 `--depth 1 --branch main`으로 staging clone 후 `app\current`로 교체
- clone된 commit hash는 `.chatlocalwork-version`에 기록

현재 Installer는 app 설정을 생성하지 않습니다. `app-config.json`, Tailscale 로그인 및 MCP 환경 설정은 Manager 최초 실행의 책임입니다.
