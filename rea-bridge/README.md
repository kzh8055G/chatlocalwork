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

## 원격 공개 전 보안 정책

- REA 6.5.0은 MCP 도구 207개를 제공합니다. 이 브리지는 `start.cjs`의 명시적 20개 읽기 중심 도구만 노출합니다.
- `supergateway --tools` 필터는 `tools/list`뿐 아니라 허용되지 않은 `tools/call`도 거부합니다. 테스트는 `smoke.cjs`에서 `set_comment` 거부까지 확인합니다.
- 20개 도구 제한은 MCP 라우팅 기능 제한이며 파일 시스템 및 REA 프로세스 자체를 OS 수준에서 격리하지 않습니다. 네이티브 분석은 파일 내용을 입력받으므로 신뢰하지 않는 파일을 무분별하게 공급하지 않아야 합니다.
- 현재 localhost API 키 브리지를 인터넷에 직접 공개하지 않습니다. ChatGPT 원격 등록에는 **OAuth 리소스 서버 검증**, CSRF/PKCE/redirect 검증, TLS, 최소권한, 요청 제한, 접근 로그 및 세션 종료 검증이 필요합니다.
- 기존 LocalWork OAuth 토큰을 REA 브리지의 정적 API 키처럼 사용하거나 헤더만 전달하여 인증을 우회시키지 않습니다. 인가 대상(resource/audience)을 분리한 별도 인증 계층이 필요합니다.

## REA 전용 OAuth 게이트웨이: 실험 단계

`oauth-gateway.cjs`는 별도의 포트(기본 4189), 별도의 OAuth 승인 키 및 별도의 토큰 공간을 갖는 **로컬 검증용 프로토타입**입니다. `REA_PUBLIC_ORIGIN`은 예를 들어 `https://example.test`처럼 HTTPS origin이고, 공개 MCP 리소스는 `{origin}/rea/mcp`입니다. 업스트림 REA는 `http://127.0.0.1:4188/mcp`입니다. `REA_OAUTH_APPROVAL_KEY_FILE`과 `REA_BRIDGE_API_KEY_FILE`에는 서로 다른 32자 이상의 키를 저장해야 합니다. LocalWork가 사용하는 인증키를 재사용하지 마세요.

`node --test oauth-gateway.test.cjs`로 인증 코드 PKCE, 리소스 분리, 최초 인증, 갱신 토큰 회전, 잘못된 클라이언트 요청과 업스트림 전달을 검사합니다.

**주의:** 이 게이트웨이는 현재 메모리 기반 토큰/클라이언트 저장소, 세션·감사·자원제한 미완성, 동시성 정책·OAuth 2.1 전체 호환성 미검증 상태입니다. Tailscale Funnel과 공개 HTTPS에 연결하지 마세요. 정식 공개 전에 저장소 지속성 및 토큰 패밀리 무효화, 동적 클라이언트 등록 제한, 승인 흐름 CSRF 방어, Origin/Host 검증, HTTP 스트리밍 및 장시간 세션 테스트, 호출 도구별 최소 권한, 외부 보안 검토가 필요합니다.
