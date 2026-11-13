# Gera o pacote de instalação em dist\LabelGuard (e dist\LabelGuard.zip):
# executável autocontido (não precisa de .NET na máquina) + scripts de instalação.
#   powershell -ExecutionPolicy Bypass -File tools\publicar.ps1
$ErrorActionPreference = "Stop"
$raiz = Split-Path $PSScriptRoot -Parent
$saida = Join-Path $raiz "dist\LabelGuard"
$zip = Join-Path $raiz "dist\LabelGuard.zip"

if (Test-Path $saida) { Remove-Item $saida -Recurse -Force }

dotnet publish (Join-Path $raiz "src\LabelGuard.App") -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $saida
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou" }

Copy-Item (Join-Path $raiz "installer\*") $saida
Copy-Item (Join-Path $raiz "tools\desligar-reimpressao.ps1") $saida

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $saida "*") -DestinationPath $zip
Write-Host "Pacote: $saida"
Write-Host "Zip:    $zip"
