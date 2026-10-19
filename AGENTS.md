# ChatLocalWork 작업 지침

이 파일은 `/shared/AGENTS.md`보다 ChatLocalWork 저장소에 더 가까운 프로젝트 전용 지침입니다.

## 기본 원칙

- 사용자 파일과 기존 설정을 보존합니다.
- 작업 전후에 Git 상태를 확인하고, 해당 작업 파일만 명시적으로 stage 합니다.
- `git add -A`, `git reset --hard`, 광범위한 정리 명령은 사용하지 않습니다.
- 커밋 제목은 Conventional Commit 영문으로 작성하고, 본문은 `[한국어]` / `[English]` 두 섹션으로 기록합니다.
- 기능·배포 상태가 바뀌면 `README.md`, `docs/PROJECT_STATE.md`, 관련 기술 문서와 `CHANGELOG.md`를 실제 검증 범위에 맞게 갱신합니다.

## LocalWork 도구 사용

- 파일 읽기·쓰기·치환에는 가능하면 전용 파일 도구를 사용합니다. 단순 파일 편집을 위해 긴 shell 명령이나 `node -e`를 만들지 않습니다.
- Git 작업은 가능한 한 `git.exe`를 직접 실행하고 shell 래퍼를 만들지 않습니다.
- Windows 상태 확인은 우선 `scripts/windows/InspectRuntimeState.cjs`를 사용합니다. 이 스크립트는 서버 ID, 활성화 여부, 허용 도구 수, Runner 상태만 출력하며 실행 명령·인수·환경 변수 값·비밀값은 출력하지 않습니다.
- 여러 단계를 한 호출에 섞지 않습니다. 특히 **앱 전용 설정 탐색 + 실행 파일 탐색 + 프로세스 실행 + 파일 수정/Git**을 하나의 inline script에 결합하지 않습니다.

## Codex / 외부 MCP

- `%LOCALAPPDATA%\OpenAI\Codex` 같은 앱 전용 런타임 내부를 자동으로 탐색하거나 직접 실행하는 방식에 의존하지 않습니다.
- Codex에서 가져온 stdio MCP는 ID/이름의 비활성 초안으로 취급하고, 실행 명령과 도구 권한은 Manager에서 사용자가 별도로 검토·승인합니다.
- 외부 MCP는 신뢰 가능한 독립 설치 위치 또는 명시적으로 승인된 실행 파일만 사용합니다.
- 도구 목록 조회는 `initialize` + `tools/list`까지만 수행하고, `tools/call`은 허용 목록과 실행 프로필 승인이 완료된 뒤 검증합니다.
- 안전 검사에서 요청이 거부되면 동일한 실행을 다른 우회 경로로 반복하지 않습니다. 요청을 더 좁은 전용 도구로 분리하고, 완료/미완료 상태를 문서에 기록합니다.

## 검증과 배포

- 코드 빌드 성공, 자동 테스트 통과, 실제 운영 배포, GUI 수동 E2E는 서로 다른 상태로 기록합니다.
- Manager 교체 전에는 기존 실행 파일을 `%LOCALAPPDATA%\ChatLocalWork\runtime\diagnostics` 아래에 백업합니다.
- Windows Runner / Docker 재시작이 필요한 변경은 실제 재시작 후 health와 핵심 도구 호출을 확인합니다.
