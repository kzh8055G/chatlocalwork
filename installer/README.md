# ChatLocalWork Installer

ChatLocalWork의 설치 단계 전용 bootstrapper입니다.

## 책임

Installer가 담당합니다.

- Docker Desktop 설치 여부 확인 및 설치
- Tailscale 설치 여부 확인 및 설치
- ChatLocalWork 전용 Portable Node LTS 다운로드/배치
- 이후 단계에서 ChatLocalWork app package 배치

Manager가 담당하지 않습니다.

Manager는 설치가 끝난 뒤 최초 실행에서 workspace, Tailscale 로그인 상태, MCP public URL, .env, app-config 등 실행 설정을 구성합니다.

## 현재 사용법

점검만 수행:

```text
ChatLocalWork.Installer.exe --check
```

누락 의존성 설치:

```text
ChatLocalWork.Installer.exe --install
```

Docker Desktop과 Tailscale은 winget을 사용합니다. Portable Node는 nodejs.org의 현재 Windows x64 LTS ZIP을 받아 다음 위치에 배치합니다.

```text
%LOCALAPPDATA%\ChatLocalWork\tools\node\node.exe
```

현재 단계에서는 ChatLocalWork app package 자체의 다운로드/배치는 아직 구현하지 않았습니다.
