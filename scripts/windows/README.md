# Windows MCP Start/Stop

현재 파일
---------

StartMCP.bat
    일반 권한으로 MCP 전체 환경 시작.

StartMCP.cjs
    Windows Runner, Docker Desktop, workmachine,
    동적 Docker host port, Tailscale 서비스/클라이언트/연결/Funnel을 시작하고 상태를 확인.

StopMCP.bat
    일반 권한으로 MCP 전체 환경 종료.

StopMCP.cjs
    Tailscale, Docker, Windows Runner를 종료하고 최종 상태를 확인.

동작 원칙
---------

StartMCP.bat / StopMCP.bat 자체는 일반 권한으로 실행합니다.

Tailscale Windows 서비스 start/stop이 일반 권한에서 거부될 경우에만
UAC 승인 팝업을 띄워 해당 sc.exe 명령만 관리자 권한으로 실행합니다.

즉 전체 스크립트를 관리자 권한으로 실행할 필요는 없습니다.

Start 흐름
----------

1. Windows Runner 확인 / 시작
2. Docker Desktop 확인 / 시작
3. 기본 포트 3999 상태 확인
4. 3999가 예약/사용 중이면 4000~4999에서 사용 가능한 포트 자동 선택
5. 선택한 포트를 MCP_HOST_PORT로 Docker Compose에 전달
6. workmachine build / force recreate / start
7. 선택 포트의 /health 로컬 게이트웨이 확인
8. Tailscale 서비스 확인 / 필요 시 UAC 요청
9. Tailscale client(tailscale-ipn.exe) 실행 여부 확인 / 미실행 시 자동 시작
10. Tailscale 연결 확인 / up
11. Funnel을 선택 포트로 자동 구성
12. ChatGPT MCP 공개 endpoint 상태 확인

Stop 흐름
---------

1. Tailscale Funnel reset
2. Tailscale down
3. Tailscale UI 종료
4. Tailscale 서비스 stop
5. Tailscale 서비스 stop이 권한 부족이면 UAC 요청
6. workmachine docker compose down
7. Docker Desktop 종료
8. Docker WSL 배포판 종료
9. Docker 관련 잔여 프로세스 정리
10. Windows Runner 종료
11. 최종 상태 확인

인코딩
------

sc.exe의 한국어 OEM 출력은 사용자에게 그대로 표시하지 않습니다.
대신 스크립트가 영문 상태 메시지만 출력합니다.
