@echo off
rem Duplo clique: pede permissao de administrador e roda desinstalar.ps1 desta pasta.
rem O historico fica em C:\ProgramData\LabelGuard (para apagar: desinstalar.ps1 -RemoverDados).
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0desinstalar.ps1"
