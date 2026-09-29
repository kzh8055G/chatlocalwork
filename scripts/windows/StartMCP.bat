@echo off
setlocal
cd /d "%~dp0"

set "NODE_EXE=C:\nvm4w\nodejs\node.exe"

if not exist "%NODE_EXE%" (
  for /f "delims=" %%I in ('where node 2^>nul') do (
    set "NODE_EXE=%%I"
    goto :node_found
  )
)

:node_found
if not exist "%NODE_EXE%" (
  echo [ERROR] node.exe was not found.
  exit /b 1
)

"%NODE_EXE%" "%~dp0StartMCP.cjs"
set "RC=%ERRORLEVEL%"

echo.
if "%RC%"=="0" (
  echo [OK] MCP startup sequence completed.
) else (
  echo [ERROR] MCP startup sequence failed. ExitCode=%RC%
)

exit /b %RC%
