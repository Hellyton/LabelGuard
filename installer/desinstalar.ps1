# Remove o LabelGuard e devolve a fila do Papersystem para a impressora. Executar como administrador:
#   powershell -ExecutionPolicy Bypass -File desinstalar.ps1 [-Fila "LABEL"] [-RemoverDados] [-Simular]
# O histórico e a configuração (C:\ProgramData\LabelGuard) são mantidos, a não ser com -RemoverDados.
param(
    [string]$Fila = "LABEL",
    [string]$FilaSaida = "LabelGuard - NAO USAR",
    [string]$Porta = "LabelGuard_9100",
    [string]$Destino = (Join-Path $env:ProgramFiles "LabelGuard"),
    [switch]$RemoverDados,
    [switch]$Simular
)

$ErrorActionPreference = "Stop"
$dados = Join-Path $env:ProgramData "LabelGuard"

function Fazer($texto, [scriptblock]$acao) {
    if ($Simular) {
        Write-Host "  [simulação] $texto" -ForegroundColor Yellow
    }
    else {
        Write-Host "  $texto"
        & $acao
    }
}

try {
    $admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $admin -and -not $Simular) {
        throw "Execute como administrador (botão direito no PowerShell > Executar como administrador)."
    }

    $filaObj = Get-Printer -Name $Fila -ErrorAction SilentlyContinue
    $saidaObj = Get-Printer -Name $FilaSaida -ErrorAction SilentlyContinue

    if ($filaObj -and $filaObj.PortName -eq $Porta) {
        if (-not $saidaObj) {
            throw "Não sei para qual porta devolver '$Fila': a fila '$FilaSaida' não existe. Use Set-Printer -Name ""$Fila"" -PortName ""USB001"" (ajuste a porta)."
        }
        Fazer "Devolver '$Fila' para a porta $($saidaObj.PortName)" {
            Set-Printer -Name $Fila -PortName $saidaObj.PortName
            Get-CimInstance Win32_Printer | Where-Object Name -eq $Fila | Set-CimInstance -Property @{ WorkOffline = $false }
        }
    }
    elseif ($filaObj) {
        Write-Host "  '$Fila' já está na porta $($filaObj.PortName)"
    }

    if (Get-Process -Name "LabelGuard.App" -ErrorAction SilentlyContinue) {
        Fazer "Fechar o LabelGuard" { Get-Process -Name "LabelGuard.App" | Stop-Process -Force; Start-Sleep -Seconds 1 }
    }

    foreach ($pasta in "CommonStartup", "CommonPrograms") {
        $atalho = Join-Path ([Environment]::GetFolderPath($pasta)) "LabelGuard.lnk"
        if (Test-Path $atalho) { Fazer "Remover atalho $atalho" { Remove-Item $atalho -Force } }
    }

    if ($saidaObj) { Fazer "Remover a fila '$FilaSaida'" { Remove-Printer -Name $FilaSaida } }
    if (Get-PrinterPort -Name $Porta -ErrorAction SilentlyContinue) {
        Fazer "Remover a porta '$Porta'" { Remove-PrinterPort -Name $Porta }
    }
    if (Test-Path $Destino) { Fazer "Remover $Destino" { Remove-Item $Destino -Recurse -Force } }

    if ($RemoverDados) {
        if (Test-Path $dados) { Fazer "Remover $dados (histórico e configuração)" { Remove-Item $dados -Recurse -Force } }
    }
    else {
        Write-Host "  Histórico e configuração mantidos em $dados"
    }

    Write-Host ""
    if ($Simular) { Write-Host "Simulação concluída. Nada foi alterado." -ForegroundColor Yellow }
    else { Write-Host "LabelGuard removido. '$Fila' imprime direto na impressora." -ForegroundColor Green }
    $codigo = 0
}
catch {
    Write-Host ""
    Write-Host "ERRO: $($_.Exception.Message)" -ForegroundColor Red
    $codigo = 1
}

if ($Host.Name -eq "ConsoleHost" -and -not [Console]::IsInputRedirected) {
    Read-Host "Pressione Enter para fechar" | Out-Null
}
exit $codigo
