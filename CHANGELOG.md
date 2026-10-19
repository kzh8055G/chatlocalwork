# 변경 이력 (CHANGELOG)

ChatLocalWork의 **사용자에게 의미 있는 기능·수정 사항**을 버전별로 기록합니다. 형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.1.0/)의 `Added / Changed / Fixed / Security` 구분을 참고합니다.

> **버전 범위:** 여기의 `0.x.y`는 현재 **Manager 실행 파일의 버전**을 기준으로 작성합니다. LocalWorkMCP의 `package.json` 버전(현재 0.1.0)과 Installer 버전은 아직 통합되어 있지 않습니다. 날짜와 커밋은 Git 기록 기준이며, 기존 버전이 공식 릴리스·태그로 배포되었다는 의미는 아닙니다.

## [Unreleased]

아직 다음 버전 번호를 부여하지 않은 변경을 여기에 추가합니다.

## [0.3.0] - 2026-10-19

### Added
- **범용 Windows stdio MCP**: 서버별 Windows 프로세스 재사용, 허용 도구 조회·호출, Windows Runner 큐 중계.
- Manager에 실행 파일·인수·작업 폴더·환경 변수 이름·허용 도구 편집과 **실행 승인 다이얼로그** 추가. 비밀값을 Docker Registry와 분리한 Windows 호스트 전용 프로필 저장소 도입.
- Codex에서 가져온 일반 stdio MCP 항목을 자동 실행하지 않는 **비활성 초안**으로 등록.

### Security
- 도구 호출마다 Registry와 호스트 프로필의 독립 허용 목록, 승인 여부, Workspace 작업 폴더 및 차단 실행 파일 검증.
- 서버 비활성화·프로필 변경 시 기존 프로세스 자동 종료 및 재승인 정책.

### Verified
- Windows 모의 MCP 직접 테스트, Windows Runner 큐 통합 테스트, 기존 REA/Ghidra 바이너리 실검증 통과. Manager Release 빌드와 LocalWork TypeScript 검사 통과. Linux 회귀 테스트 50개 통과.
- Manager v0.3.0, Docker LocalWork, Windows Runner 실제 재시작 및 운영 중인 REA 분석 호출 검증 완료. 일반 stdio MCP는 격리 테스트 완료, 운영 서버 미등록.


### Added
- Manager의 `Codex 설정 가져오기`: `~/.codex/config.toml`에서 REA 및 로컬 HTTP MCP를 찾아 비활성 Registry 항목으로 가져오는 초기 버전. 비밀 환경 변수와 미승인 stdio 실행 명령은 가져오지 않음. 소스·파서 스모크 테스트·Release 빌드 완료, 실행 중인 Manager 신규 UI 배포는 미확인.

### Fixed
- Manager의 Windows 명령 실행 승인 창을 최상위 창·작업 표시줄에 표시하고 열릴 때 활성화하여 승인 대화상자가 뒤로 숨는 문제를 방지.

### Changed
- Windows Runner 기본 실행 파일 허용 목록에 `mame.exe` 추가. 해당 실행 파일을 별도 승인 없이 실행할 수 있으므로 보안상 변경으로 취급.

- MCP Hub Registry를 Workspace의 숨김 폴더에서 `%LOCALAPPDATA%\ChatLocalWork\config`로 이전하고 Docker 전용 읽기 전용 마운트 구성.
- REA 검증용 진단 자료를 Workspace 외부 런타임 진단 폴더로 정리.

### 문서
- 변경 이력 파일 작성, 버전 갱신·검증 규칙 정의.

## [0.2.0] - 2026-10-19

### Added
- **MCP Hub**: `mcp_list_servers`, `mcp_list_tools`, `mcp_call_tool`을 추가하여 LocalWork 하나로 외부 MCP 도구를 탐색하고 중계.
- Registry 파일 `<Workspace>/.chatlocalwork/mcp-servers.json`을 통해 서버 등록·활성화·허용 도구 목록 관리.
- Manager에 **MCP 서버 관리** 화면 및 REA 등록 템플릿 추가.
- Windows Runner 큐를 사용하는 **REA 6.5.0 + Ghidra 12.1.4** 연동 및 실제 바이너리 분석 검증.
- REA의 **허용 도구 21개**(읽기 중심 20개 + `open_binary`) 정책. 분석 대상은 Workspace 내부 실파일로 제한.
- MCP Hub 개발·사용 문서 `docs/MCP_HUB.md`.

### Verified
- Linux 회귀 테스트 48개 통과, Manager Release 빌드 성공.
- 운영 중인 Docker LocalWork → Windows Runner → REA → Ghidra에서 `sample.exe` 열기, 문서 및 함수 목록 조회 성공.

### Commit
- `5f29a0d` — MCP Hub, Manager UI, REA Runner 전송.

## [0.1.0] - 2026-10-16

**최초 명시적 Manager 버전.** 과거 프로젝트 전체를 0.1.0으로 소급해 태그한 것은 아닙니다.

### Added
- Manager 창 제목·화면·로그에 실행 중인 어셈블리 버전 표기.
- Windows Runner에서 미등록 실행 파일을 사용할 때 Manager 승인 요청, 일회성·세션·해시 기반 신뢰·거부 정책.
- Codex용 REA 설치 및 Ghidra/JDK 연동. 독립 HTTP MCP 브리지와 OAuth 실험용 게이트웨이 구현(별도 공개 서비스로 배포하지 않음).

### Security
- 독립 REA HTTP 브리지는 초기 20개 읽기 중심 도구로 제한.
- REA OAuth 게이트웨이는 **프로토타입**이며 외부 공개·ChatGPT 독립 앱 등록은 완료하지 않음.

### Commits
- `d35a904` — Manager 버전 표시.
- `f6ff7cc` — Windows Runner 실행 승인.
- `c9430b1`, `e4d9290`, `556a4d2`, `9778209` — REA 설정·실험용 브리지·보안 검증.

## 버전 도입 이전 (2026-09 ~ 2026-10-15)

버전 번호가 없었던 개발 기록을 **날짜별 주요 이정표**로 정리합니다. 아래 구간을 별도의 정식 릴리스로 간주하지 않습니다.

### 2026-10-11
- Windows Runner 타임아웃 후 자식 프로세스 종료 지연 복구 강화 (`434e3a3`).

### 2026-10-08
- Manager 환경 설정 분리, Workspace 선택, 버튼·안내문 잘림 수정 (`0bfadbb`, `49c2433`, `42c5557`, `27e6c01`).

### 2026-10-01
- Installer의 의존성 설치와 Manager의 최초 환경 구성을 분리.
- GitHub clone 기반 설치, 경로 분리, 환경 점검 및 시작/종료 자동화 보강.
- 주요 커밋: `231a7bf`, `547ec7f`, `c080480`, `f1b648e`.

### 2026-09
- LocalWorkMCP, Windows Runner, Manager, 스크립트를 ChatLocalWork 프로젝트로 재구성.
- Windows 직접 실행·파일 큐·Docker·Tailscale Funnel 기반 원격 작업 환경 구축.

---

## 버전 관리 규칙

1. **버전 올릴 때:** Manager의 `ChatLocalWork.Manager.csproj`에서 `Version`, `AssemblyVersion`, `FileVersion`을 함께 변경합니다. 다른 컴포넌트의 버전과 동일하다고 자동 가정하지 않습니다.
2. **코드 변경 시:** `Unreleased`에 사용자 관점의 변경점을 `Added / Changed / Fixed / Security / Removed` 등으로 기록합니다. 내부 실험은 완료·배포 여부를 명시합니다.
3. **버전 확정 시:** 해당 변경들을 새 `[x.y.z] - YYYY-MM-DD` 항목으로 이동하고 관련 Git 커밋을 기록합니다.
4. **검증:** 빌드·테스트 결과는 실제 확인한 수치만 기록합니다. 배포 전용 작업과 소스 커밋은 구별합니다.
5. **릴리스 태그:** 정식 릴리스를 만드는 경우 검증된 커밋에 `vX.Y.Z` 태그를 생성합니다. **이번 문서 작성만으로 태그를 생성하지 않습니다.**
6. **버전 원칙:** 초기 개발 단계에서는 `0.MINOR.PATCH`를 사용하고, 기능 추가는 MINOR, 호환 가능한 오류 수정은 PATCH 증가를 기본으로 합니다.
