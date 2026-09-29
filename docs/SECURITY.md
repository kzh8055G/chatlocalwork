# Windows Runner 보안

## 허용 범위

기본 실행 파일은 windows-runner/runner-config.json의 allowedExecutables에 명시한다.

현재 목적은 개발 도구 실행이다.

- git.exe
- dotnet.exe
- cmake.exe
- ninja.exe
- msbuild.exe
- cl.exe
- node.exe
- python.exe
- docker.exe
- tailscale.exe

프로젝트에서 직접 빌드한 exe는 chat_local_workspace 내부의 절대 경로일 때만 실행할 수 있다.

## 명시적 차단

다음 계열은 Runner에서 차단한다.

- powershell / pwsh
- cmd
- wscript / cscript
- mshta
- rundll32
- regsvr32

## 경로 제한

workdir는 반드시 Windows의 chat_local_workspace 내부여야 한다.

Runner 자체도 workspace 외부의 임의 exe를 절대 경로로 실행하지 않는다.

## 프로세스 실행

항상 Node child_process.spawn을 shell:false로 호출한다.

timeout 발생 시 taskkill.exe를 직접 실행해 자식 프로세스 트리를 종료한다.
