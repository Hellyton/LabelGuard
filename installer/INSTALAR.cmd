@echo off
rem Duplo clique: pede permissao de administrador e roda instalar.ps1 desta pasta.
rem Para outra fila do Papersystem, use o PowerShell: instalar.ps1 -Fila "NOME"
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0instalar.ps1"
