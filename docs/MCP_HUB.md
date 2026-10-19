# ChatLocalWork MCP Hub (v0.2.0)

## 목적

ChatGPT에는 LocalWork 앱 하나만 등록하고, 내부 MCP 서버를 Manager에서 등록·비활성화·삭제합니다. ChatGPT는 고정된 세 개의 MCP 도구를 사용합니다.

- `mcp_list_servers()` — Registry의 서버 ID, 이름, 활성화 상태 조회
- `mcp_list_tools(server)` — 연결된 서버의 도구 및 입력 스키마 동적 조회
- `mcp_call_tool(server, tool, arguments)` — 명시적으로 허용한 도구 호출

서버 목록은 JSON 파일을 매 요청 다시 읽으므로 목록 변경 시 LocalWork 내부의 도구 정의는 달라지지 않습니다. ChatGPT에서 *처음 한 번* 세 도구를 인식하려면 LocalWork 앱 업데이트/도구 새로 고침이 필요할 수 있습니다.

## Registry 및 UI

- Windows Manager → **MCP 서버 관리...**
- Windows Workspace 경로: `<workspaceRoot>/.chatlocalwork/mcp-servers.json`
- Docker 경로: `/shared/.chatlocalwork/mcp-servers.json`
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

## 검증

- Windows: `node rea-bridge/test-windows-queue.cjs`
- Windows: `npm exec --prefix localworkmcp -- vitest run test/mcp-hub.test.ts test/mcp-hub.integration.test.ts`
- Windows Manager: `dotnet build manager/ChatLocalWork.Manager/ChatLocalWork.Manager.csproj -c Release`
- 실제 Docker→Windows Runner→REA→Ghidra 통합 검증: `CPS1NativeRecompiler/build/native/sample.exe`를 `open_binary`로 열고 `list_documents`, `list_procedures` 조회 완료.

## 현재 범위와 후속 개발

1. Windows REA는 실제 작동하지만 이 단계에서만 REA 유형을 위한 전용 stdio 브리지를 제공합니다. 범용 Windows stdio MCP 및 Codex/VS Code 설정 자동 가져오기는 후속 개발 항목입니다.
2. 일반 HTTP MCP에서는 미지원 인증 방식 및 Stateful 세션 지속 사용을 별도로 구현해야 합니다.
3. REA 쓰기·디버깅·임의 명령 도구의 승인 정책은 추후 구현하며 현재 허용하지 않습니다.
4. Manager의 UI는 Registry 관리 기능이며 **임의 패키지를 설치하는 자동 설치기**는 아닙니다.
5. 실행 중인 Windows Runner와 Docker 컨테이너, Manager는 새 바이너리/소스를 적용하도록 재시작 또는 재배포해야 합니다.
