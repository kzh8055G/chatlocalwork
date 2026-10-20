# ChatLocalWork MCP Hub (v0.4.1)

## 목적

ChatGPT에는 LocalWork 앱 하나만 등록하고, 내부 MCP 서버를 Manager에서 등록·비활성화·삭제합니다. ChatGPT는 고정된 세 개의 MCP 도구를 사용합니다.

- `mcp_list_servers()` — Registry의 서버 ID, 이름, 활성화 상태 조회
- `mcp_list_tools(server)` — 연결된 서버의 도구 및 입력 스키마 동적 조회
- `mcp_call_tool(server, tool, arguments)` — 명시적으로 허용한 도구 호출

서버 목록은 JSON 파일을 매 요청 다시 읽으므로 목록 변경 시 LocalWork 내부의 도구 정의는 달라지지 않습니다. ChatGPT에서 *처음 한 번* 세 도구를 인식하려면 LocalWork 앱 업데이트/도구 새로 고침이 필요할 수 있습니다.

## Registry 및 UI

- Windows Manager → **MCP 서버 관리...**
- Windows 호스트 설정 경로: `%LOCALAPPDATA%\ChatLocalWork\config\mcp-servers.json`
- Docker 경로: `/chatlocalwork-config/mcp-servers.json`
- 기존 Workspace의 `.chatlocalwork/mcp-servers.json`은 StartMCP가 새 설정이 없는 경우에 한하여 이전합니다. 정상 검증 뒤 기존 폴더를 정리할 수 있습니다.
- Docker는 호스트 `config` 폴더만 별도 **읽기 전용 마운트**합니다.
- 서버 최대 32개, ID는 고유한 소문자/숫자/`_`/`-`를 사용합니다.
- 서버 등록은 사용자가 `허용 도구`를 명시적으로 설정한 경우에만 `mcp_call_tool`로 연결됩니다.

### 일반 Streamable HTTP MCP

```json
{
  "version": 1,
  "servers": [
    {
      "id": "example",
      "name": "Example MCP",
      "enabled": false,
      "transport": "streamableHttp",
      "url": "http://127.0.0.1:4200/mcp",
      "allowedTools": ["safe_read_tool"]
    }
  ]
}
```

보안을 위해 LocalWork의 HTTP registry는 루프백 또는 `host.docker.internal`의 HTTP 주소만 허용합니다. HTTP MCP는 현재 연결 시 클라이언트를 초기화하고 요청 후 종료하므로 *상태 유지가 필요한 HTTP MCP*에는 별도 세션 관리가 필요합니다.

### Windows REA stdio MCP

Manager의 **REA 자동 설정** 버튼은 Windows Runner용 REA 서버 등록 템플릿(21개 허용 도구)을 작성합니다. Windows Runner는 REA 6.5.0 프로세스를 **Windows에서 유지**하고, Docker의 LocalWork와는 이미 존재하는 요청/응답 공유 큐를 통하여 통신합니다. 별도의 인터넷용 REA OAuth 서버·Funnel은 필요하지 않습니다.

```json
{
  "id": "rea",
  "name": "Reverse Engineer Anything",
  "enabled": true,
  "transport": "windowsRea",
  "allowedTools": ["open_binary", "list_documents", "list_procedures"]
}
```

REA의 Ghidra 및 Node npm CLI 위치는 **Docker 공유 Registry가 아닌** Windows 호스트 전용 `%LOCALAPPDATA%\ChatLocalWork\config\rea-provider.json`에 보관합니다. 이 파일은 `rea-bridge/configure-windows-hub.cjs`를 사용하여 생성합니다.

필요한 설정: `ghidraInstallDir`, `javaHome`, `npxCli`는 모두 Windows 절대 경로여야 합니다. 설치 환경을 다른 PC로 옮기면 이 설정을 다시 생성해야 합니다.

**REA 정책:** 현재 REA의 207개 MCP 도구 중 실제 Windows Runner에서 허용한 도구는 21개(기존 읽기 중심 20개와 `open_binary`)입니다. `open_binary`는 Workspace 내부 실파일에만 접근할 수 있고 `snapshot_path`는 차단합니다. 그 외 도구를 활성화하려면 별도의 승인 정책이 필요합니다. Manager에 다른 도구 이름을 적어도 Windows Runner의 독립 허용 목록을 통과하지 못합니다.

## 범용 Windows stdio MCP (v0.3.0)

이제 REA 외에 Windows에서 실행되는 **임의의 stdio MCP 서버**를 Manager의 **MCP 서버 관리...**에서 등록할 수 있습니다. 서버는 Docker가 아니라 Windows Runner에서 실행하고, 서버별 프로세스를 재사용합니다. 일반 MCP 서버를 자동 설치하거나 임의로 실행하지 않습니다.

등록 순서:

1. Manager에서 서버 유형 **Windows stdio MCP**를 선택합니다. Codex에서 가져온 항목은 서버 ID와 이름만 저장된 **비활성 초안**이므로 실행 구성을 별도로 입력해야 합니다.
2. `실행 파일`에 존재하는 **Windows 절대 경로의 `.exe`**를 지정합니다. `cmd.exe`, PowerShell 등 차단된 쉘 실행기는 허용되지 않습니다. 패키지 CLI를 사용하는 경우 Node.exe 등을 지정하고 각 CLI 인수를 줄별로 입력합니다.
3. `작업 폴더`는 Workspace 내부로 지정합니다. `인수 (줄별)`에는 인수 하나씩, `환경 변수 이름`에는 Windows Runner가 실행할 때 **이미 환경에 존재하는** 변수 이름만 입력합니다. 비밀값과 Codex `env` 값은 저장하거나 Docker Registry에 복사하지 않습니다.
4. `허용 도구`에는 실제 MCP 도구 이름을 줄별로 입력합니다. 저장 버튼에서 **실행 파일·인수·작업 폴더·허용 도구를 확인하고 명시 승인**해야 Windows 호스트 전용 프로필이 승인됩니다.
5. 활성화 후 `mcp_list_tools(server)`와 `mcp_call_tool(server, tool, arguments)`를 사용할 수 있습니다. 등록·승인은 곧 임의 도구 호출 허용을 뜻하지 않으며, 도구는 Registry와 Windows 호스트 프로필의 **양쪽 허용 목록**에 들어 있어야 합니다.

공유 Registry에는 다음 항목만 보관합니다:

```json
{
  "id": "my-server",
  "name": "My Windows MCP",
  "enabled": true,
  "transport": "windowsStdio",
  "allowedTools": ["ping"]
}
```

실행 명령·인수·환경 변수 **이름**은 Docker에서 읽을 수 없는 Windows 호스트 전용 `%LOCALAPPDATA%\ChatLocalWork\private\mcp-stdio-profiles.json`에 분리해 저장합니다. Docker는 공유 Registry의 서버 ID만 큐로 보내며, Windows Runner가 호스트 프로필의 승인 여부와 두 허용 도구 목록을 다시 검사합니다. 서버를 비활성화하거나 실행 구성을 변경하면 새 호출은 거부되고, 이미 실행 중인 프로세스도 짧은 간격으로 정리됩니다.

**보안 제약:** `.exe`를 통한 실행은 임의 Windows 코드를 실행할 수 있는 권한입니다. 신뢰할 수 있는 MCP 소스만 명시적으로 승인해야 합니다. 인수에 비밀번호·API 키를 직접 넣지 마세요. Windows Runner의 환경 변수를 사용할 경우 Runner 재시작이 필요할 수 있습니다. 자동 패키지 설치·원격 OAuth·위험 도구 세부 권한 프롬프트는 별도 작업입니다.

## 검증

- Windows: `node --test windows-runner/stdio-hub.test.cjs windows-runner/stdio-runner.integration.test.cjs`
- Windows: `node rea-bridge/test-windows-queue.cjs` (공통 stdio 클라이언트로 변경된 REA 회귀 검증)
- Windows: `npm exec --prefix localworkmcp -- vitest run test/mcp-hub.test.ts test/mcp-hub.integration.test.ts`
- Windows Manager: `dotnet build manager/ChatLocalWork.Manager/ChatLocalWork.Manager.csproj -c Release`
- 실제 Docker→Windows Runner→REA→Ghidra 통합 검증: `CPS1NativeRecompiler/build/native/sample.exe`를 `open_binary`로 열고 `list_documents`, `list_procedures` 조회 완료.

## Windows stdio MCP 도구 조회 기능 (v0.4.0)

Manager에서 **Windows stdio MCP**를 선택한 다음 실행 파일·인수·작업 폴더·환경 변수 이름을 채우고 **도구 조회...**를 누르면, **해당 프로그램을 한 번 실행하는 데 대한 경고와 승인 대화상자**가 표시됩니다. 동의하면 최대 45초 동안 MCP `initialize`와 `tools/list`만 실행하여 도구 이름을 읽습니다. 실제 도구 실행(`tools/call`)은 하지 않으며, 프로세스를 종료합니다.

- 조회된 도구는 기본적으로 **모두 미선택**입니다. 사용자가 선택한 도구 이름만 입력창에 추가할 수 있습니다.
- **추가 / 저장**을 누를 때에는 기존 Windows stdio 서버 실행 승인과 프로필 저장 절차가 별도로 유지됩니다. 즉, 조회만으로 MCP가 자동 등록·활성화되지는 않습니다.
- 저장할 실행 파일과 도구 목록은 별도로 검토해야 하며, 실행 파일 자체가 악의적인 경우 목록 조회만으로도 부작용이 발생할 수 있습니다.
- 테스트: `dotnet run --project manager/McpStdioInspector.Smoke/McpStdioInspector.Smoke.csproj -c Release -- <node.exe 절대 경로>` — 모의 stdio MCP 서버와 차단된 셸 실행 거부 확인.
- Codex의 `node_repl`은 등록 항목이 확인됐으나 **실행 파일 직접 호출이 안전 검사에 막혀서 실제 도구 조회·호출 검증은 미완료**입니다. 이를 우회하지 않았고, 독립 실행 가능 여부와 보안 정책 검토가 필요합니다. v0.4.0 Manager는 실행 파일 교체·재시작을 확인했으나 신규 GUI 기능의 직접 클릭 E2E 검증은 별도입니다.

## v0.3.0 배포 상태 (2026-10-19)

- Manager 실행 파일 v0.3.0 교체·재시작, Windows Runner 재시작, Docker LocalWork 새 코드 배포 확인.
- Docker → Windows Runner → 기존 REA/Ghidra로 `sample.exe` 열기·문서·함수 목록 조회 성공.
- 범용 Windows stdio MCP는 격리된 모의 서버와 Windows Runner 큐에서 검증했으나, **실제 사용자의 다른 stdio MCP를 운영 Registry에 등록하지는 않았습니다.** 사용자 승인과 도구 목록 설정 후 사용할 수 있습니다.

## 현재 범위와 후속 개발

1. 일반 Windows stdio MCP는 Manager에서 **명시 승인한 .exe + 인수 기반 실행**을 지원합니다. Codex 설정 가져오기는 일반 stdio의 메타데이터 초안 등록까지 가능하지만, 명령·인수·비밀 환경 변수 복사 및 자동 승인은 하지 않습니다. VS Code 설정 가져오기는 아직 미구현입니다.
2. 일반 HTTP MCP에서는 미지원 인증 방식 및 Stateful 세션 지속 사용을 별도로 구현해야 합니다.
3. REA 쓰기·디버깅·임의 명령 도구의 승인 정책은 추후 구현하며 현재 허용하지 않습니다.
4. Manager의 UI는 Registry 관리 기능이며 **임의 패키지를 설치하는 자동 설치기**는 아닙니다.
5. 실행 중인 Windows Runner와 Docker 컨테이너, Manager는 새 바이너리/소스를 적용하도록 재시작 또는 재배포해야 합니다.

## Codex 등록 설정 가져오기 (개발 단계)

Manager의 **Codex 설정 가져오기** 버튼은 사용자 홈 `.codex/config.toml`에 있는 `mcp_servers` 설정을 읽습니다. 현재 구현은 한 줄 형태의 일반적인 TOML 키/문자열 배열, `mcp_servers.<name>.env` 섹션을 해석합니다. 전체 TOML 규격을 지원하는 파서는 아직 아니므로 다중 행 배열/복잡한 테이블은 후속 개선 사항입니다.

- REA 기존 브리지를 인식하거나, 로컬 HTTP(S 아닌 `http://localhost`·`127.0.0.1`·`host.docker.internal`) 서버를 **비활성 상태**로만 가져옵니다.
- Codex 설정의 비밀 환경 변수와 임의의 명령줄은 Registry에 복사하지 않습니다.
- 이미 등록한 ID는 덮어쓰지 않고 건너뜁니다.
- 새 MCP 활성화와 도구 허용 목록은 사용자가 별도로 설정해야 합니다.
- 일반 stdio MCP는 Manager에서 수동 설정·승인한 뒤 실행할 수 있습니다. Codex stdio 실행 명령의 **자동 복사 및 자동 실행은 미지원**이며, 비밀값도 가져오지 않습니다.

파서 스모크 테스트: `dotnet run --project manager/CodexMcpImporter.Smoke/CodexMcpImporter.Smoke.csproj`.

검증 상태: Codex 설정 파서 스모크 테스트 통과, Manager Release 빌드 경고 0/오류 0. **v0.3.0 Manager 실행 파일로 배포 완료**했습니다. 다만 Codex 가져오기 버튼을 직접 클릭하는 수동 UI E2E 검증은 별도로 남아 있습니다.


## 안전 검사와 진단 실행 원칙 (2026-10-19)

최근 외부 stdio MCP 검증 중 일부 요청이 플랫폼의 실행 안전 검사에서 거부되었습니다. 점검 결과 일반 파일 API, Git, 단순 Node 실행은 정상이며, LocalWork의 `exec_command` 권한 범위도 최근 MCP Hub 작업에서 새로 넓어진 것이 아닙니다.

재발 방지를 위해 다음 원칙을 적용합니다.

- 일반 파일 편집은 전용 파일 도구를 우선 사용하고 긴 shell/inline Node 스크립트에 섞지 않습니다.
- Git은 가능한 한 `git.exe`를 직접 호출합니다.
- 런타임 상태는 `scripts/windows/InspectRuntimeState.cjs`로 확인합니다. 이 스크립트는 실행 경로·인수·환경 변수 값·비밀값을 출력하지 않습니다.
- Codex/OpenAI 앱 전용 런타임 내부를 자동 탐색·실행하지 않습니다. 가져온 stdio MCP는 비활성 초안으로 두고 Manager에서 사용자가 실행 구성과 도구 권한을 검토·승인합니다.
- 안전 검사에 거부된 실행은 다른 경로로 우회하지 않고 작업을 더 작은 전용 단계로 분리합니다.

현재 운영 Registry에서 `node_repl`은 비활성 `windowsStdio` 초안이며 허용 도구는 0개이고, 승인된 stdio 실행 프로필은 없습니다. 따라서 자동 실행 상태가 아닙니다.


## 공식 외부 Windows stdio MCP 검증 완료 (2026-10-19)

범용 Windows stdio 중계가 mock 서버뿐 아니라 독립 배포된 실제 MCP 서버에서도 동작하는지 확인하기 위해 `@modelcontextprotocol/server-everything@1.0.0`을 사용했습니다.

검증 결과:

1. **직접 stdio** — 기존 `StdioMcpClient`로 서버 초기화 및 `tools/list`를 수행해 13개 도구를 확인했습니다. `echo`와 `get-sum(17,25)=42` 호출 성공.
2. **Windows Runner 큐** — 임시 Registry와 임시 host 프로필을 사용해 `windowsStdio` 경로로 동일 서버를 실행했습니다. 허용한 `echo`, `get-sum`만 노출·호출되고 미허용 도구는 거부됨.
3. **LocalWork Hub E2E** — 실제 `registerHubTools`의 `mcp_list_tools` / `mcp_call_tool`을 통해 임시 Windows Runner 큐와 Everything MCP까지 연결했습니다. 허용 도구 조회, `echo`, 합계 42, 미허용 도구 거부 모두 통과.

검증은 테스트 전용 임시 디렉터리에서 수행했고 종료 시 정리합니다. **운영 `mcp-servers.json`, 기존 REA 설정, 사용자 승인 상태는 변경하지 않았습니다.**

관련 테스트:

- `windows-runner/everything-stdio.integration.test.cjs`
- `windows-runner/stdio-runner-real.integration.test.cjs`
- `windows-runner/everything-localwork-hub.e2e.mjs`

따라서 v0.4.0 기준으로 범용 Windows stdio MCP의 핵심 중계 경로는 실제 외부 MCP 서버까지 검증된 상태입니다. 남은 운영 과제는 Manager GUI에서 실제 서버를 사용자가 등록·승인하는 수동 E2E와 설치/업데이트 lifecycle 자동화입니다.


## ChatGPT 도구 새로 고침 진단 (v0.4.1)

간헐적인 `앱을 업데이트할 수 없습니다. 다시 시도하세요` 문제를 추적하기 위해 ChatGPT-facing MCP/OAuth 요청을 영속 NDJSON으로 기록합니다.

호스트 로그:

`%LOCALAPPDATA%\ChatLocalWork\runtime\diagnostics\mcp-access\mcp-access.ndjson`

Docker 경로:

`/chatlocalwork-diagnostics/mcp-access.ndjson`

기록하는 값은 요청 시각, request ID, HTTP method/path, MCP RPC method, tool name, protocol version, User-Agent, HTTP status, duration, outcome입니다. 인증 헤더·토큰·OAuth code·도구 arguments·요청 본문은 저장하지 않습니다.

Manager v0.4.1의 **ChatGPT 진단 로그** 버튼은 최근 로그를 읽고 마지막 OpenAI 요청을 요약합니다. 따라서 사용자가 `도구 새로 고침`을 누른 직후 다음과 같이 판별할 수 있습니다.

- `openai-mcp` / OAuth 요청이 기록됨 → OpenAI 요청이 LocalWork까지 도달. status와 RPC 단계에서 서버/프로토콜/OAuth 문제를 추적합니다.
- 해당 시각에 요청 기록이 없음 → 요청이 LocalWork HTTP handler까지 오지 않았으므로 ChatGPT/OpenAI backend, 네트워크, Funnel 등 앞단을 우선 조사합니다.
- `server/discover` 400 후 legacy `initialize`/ `tools/list` 200 → 최신 프로토콜 시도 후 호환 fallback이 성공한 경우입니다.

컨테이너 재생성으로 증거가 사라지지 않도록 Windows 호스트 bind mount를 사용합니다.
