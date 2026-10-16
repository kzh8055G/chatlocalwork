# REA MCP Bridge (Windows, localhost)

목표: Codex용 REA 등록을 변경하지 않고, REA 6.5.0 stdio MCP를 별도 로컬 Streamable HTTP MCP로 노출합니다.

현재 단계는 **로컬 테스트 전용**입니다. 인터넷/Tailscale Funnel 공개 금지. 원격 ChatGPT 직접 연결에는 별도 HTTPS 라우팅과 OAuth 2.1 또는 승인된 강력한 인증 및 접근 제어가 필요합니다. API key만으로는 ChatGPT 사용자 인증 구성이 완성된 것이 아닙니다.

- `npm install`을 Windows `rea-bridge/`에서 수행합니다.
- `GHIDRA_INSTALL_DIR`: 설치된 Ghidra 12.1.4 폴더
- `JAVA_HOME`: JDK 25 폴더
- `REA_BRIDGE_API_KEY_FILE`: 호스트 전용 경로에 저장한 강력한 랜덤 키 파일(한 줄에 하나)
- `REA_NPX_CMD`: npx.cmd의 실제 Windows 경로 (기본 npx)
- `REA_BRIDGE_PORT`: 로컬 수신 포트 (기본 4188)
- `node start.cjs` 실행, 로컬에서 `http://127.0.0.1:4188/mcp`에서 접근 (기본적으로 127.0.0.1에만 바인딩)

도구 207개를 직접 인터넷에 노출하기 전에 최소 권한·감사 기록·OAuth 인가·도구 필터링 정책을 별도로 검증해야 합니다.

## 재현 가능한 검증

`node smoke.cjs`는 임시 랜덤 키 파일을 생성한 뒤 `127.0.0.1:4188/mcp`에서 미인증 401 및 인증된 REA MCP `initialize` 응답을 확인합니다. Windows Workspace 기본 경로와 기존 Ghidra/JDK 설치 경로를 기준으로 하는 개발 스모크 테스트입니다. 외부 공개는 하지 않습니다.
