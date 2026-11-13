# Instala o LabelGuard nesta máquina. Executar como administrador, dentro da pasta do pacote:
#   powershell -ExecutionPolicy Bypass -File instalar.ps1 [-Fila "LABEL"] [-Simular]
#
# O que faz:
#   1. Encontra a fila em que o Papersystem imprime e a porta (USB) dela.
#   2. Cria a porta local do LabelGuard e a fila de saída na mesma porta USB.
#   3. Copia o programa, prepara C:\ProgramData\LabelGuard e faz o LabelGuard abrir com o Windows.
#   4. Liga a fila do Papersystem ao LabelGuard e abre o LabelGuard.
# Pode ser rodado de novo para atualizar: reconhece uma máquina já configurada.
# -Simular mostra o que seria feito sem mudar nada.
param(
    [string]$Fila = "LABEL",
    [string]$FilaSaida = "LabelGuard - NAO USAR",
    [string]$Porta = "LabelGuard_9100",
    [int]$NumeroPorta = 9100,
    [string]$Destino = (Join-Path $env:ProgramFiles "LabelGuard"),
    [switch]$Simular
)

$ErrorActionPreference = "Stop"
$exeNome = "LabelGuard.App.exe"
$exeOrigem = Join-Path $PSScriptRoot $exeNome
$exeDestino = Join-Path $Destino $exeNome
$dados = Join-Path $env:ProgramData "LabelGuard"
$config = Join-Path $dados "config.json"

function Info($texto) { Write-Host $texto }
function Fazer($texto, [scriptblock]$acao) {
    if ($Simular) {
        Write-Host "  [simulação] $texto" -ForegroundColor Yellow
    }
    else {
        Write-Host "  $texto"
        & $acao
    }
}

function Atalho($caminho, $alvo) {
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($caminho)
    $link.TargetPath = $alvo
    $link.WorkingDirectory = Split-Path $alvo
    $link.Description = "LabelGuard - controle de etiquetas duplicadas"
    $link.Save()
}

try {
    $admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $admin -and -not $Simular) {
        throw "Execute como administrador (botão direito no PowerShell > Executar como administrador)."
    }
    if (-not (Test-Path $exeOrigem)) {
        throw "$exeNome não encontrado em $PSScriptRoot. Rode o script de dentro da pasta do pacote."
    }

    Info "== Fila do Papersystem"
    $filaObj = Get-Printer -Name $Fila -ErrorAction SilentlyContinue
    if (-not $filaObj) {
        Info "Fila '$Fila' não encontrada. Filas instaladas:"
        Get-Printer | ForEach-Object { Info "  $($_.Name)   (porta $($_.PortName))" }
        throw "Rode de novo indicando a fila: -Fila ""<nome>""."
    }

    if ($filaObj.PortName -eq $Porta) {
        $saidaObj = Get-Printer -Name $FilaSaida -ErrorAction SilentlyContinue
        if (-not $saidaObj) {
            throw "A fila '$Fila' já aponta para o LabelGuard, mas a fila '$FilaSaida' não existe. Rode desinstalar.ps1 e instale de novo."
        }
        $portaImpressora = $saidaObj.PortName
        Info "  Já configurada antes: '$Fila' -> LabelGuard; impressora na porta $portaImpressora"
    }
    else {
        $portaImpressora = $filaObj.PortName
        Info "  '$Fila' (driver '$($filaObj.DriverName)') está na porta $portaImpressora"
    }

    Info "== Porta e fila de saída"
    if (Get-PrinterPort -Name $Porta -ErrorAction SilentlyContinue) {
        Info "  Porta '$Porta' já existe"
    }
    else {
        Fazer "Criar porta '$Porta' (127.0.0.1:$NumeroPorta)" {
            Add-PrinterPort -Name $Porta -PrinterHostAddress "127.0.0.1" -PortNumber $NumeroPorta
        }
    }

    $saidaObj = Get-Printer -Name $FilaSaida -ErrorAction SilentlyContinue
    if (-not $saidaObj) {
        Fazer "Criar fila '$FilaSaida' na porta $portaImpressora" {
            Add-Printer -Name $FilaSaida -DriverName $filaObj.DriverName -PortName $portaImpressora
        }
    }
    elseif ($saidaObj.PortName -ne $portaImpressora) {
        Fazer "Mover fila '$FilaSaida' para a porta $portaImpressora" {
            Set-Printer -Name $FilaSaida -PortName $portaImpressora
        }
    }
    else {
        Info "  Fila '$FilaSaida' já existe na porta $portaImpressora"
    }

    Info "== Programa"
    if (Get-Process -Name "LabelGuard.App" -ErrorAction SilentlyContinue) {
        Fazer "Fechar o LabelGuard aberto (para atualizar)" {
            Get-Process -Name "LabelGuard.App" | Stop-Process -Force
            Start-Sleep -Seconds 1
        }
    }
    Fazer "Copiar $exeNome para $Destino" {
        New-Item -ItemType Directory -Force -Path $Destino | Out-Null
        Copy-Item $exeOrigem $exeDestino -Force
        $script = Join-Path $PSScriptRoot "desligar-reimpressao.ps1"
        if (Test-Path $script) { Copy-Item $script $Destino -Force }
    }

    Info "== Dados e configuração ($dados)"
    Fazer "Criar a pasta e permitir que os usuários gravem nela (histórico, log)" {
        New-Item -ItemType Directory -Force -Path $dados | Out-Null
        & icacls $dados /grant "*S-1-5-32-545:(OI)(CI)M" /T /Q | Out-Null
    }
    if (Test-Path $config) {
        $atual = Get-Content $config -Raw | ConvertFrom-Json
        Fazer "Atualizar filas e porta em config.json (senha e histórico mantidos)" {
            $atual | Add-Member -NotePropertyName inputPrinter -NotePropertyValue $Fila -Force
            $atual | Add-Member -NotePropertyName outputPrinter -NotePropertyValue $FilaSaida -Force
            $atual | Add-Member -NotePropertyName port -NotePropertyValue $NumeroPorta -Force
            [IO.File]::WriteAllText($config, ($atual | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
        }
        if ($atual.saveJobsTo) {
            Write-Host "  Atenção: 'saveJobsTo' está ligado ($($atual.saveJobsTo)) e grava todas as impressões. Para desligar, deixe null em $config." -ForegroundColor Yellow
        }
    }
    else {
        Fazer "Criar config.json (a senha do supervisor é pedida na primeira abertura)" {
            $novo = [ordered]@{ inputPrinter = $Fila; outputPrinter = $FilaSaida; port = $NumeroPorta }
            [IO.File]::WriteAllText($config, ($novo | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
        }
    }

    Info "== Abrir com o Windows"
    $inicializar = Join-Path ([Environment]::GetFolderPath("CommonStartup")) "LabelGuard.lnk"
    $menu = Join-Path ([Environment]::GetFolderPath("CommonPrograms")) "LabelGuard.lnk"
    Fazer "Atalho em Inicializar (todos os usuários) e no menu Iniciar" {
        Atalho $inicializar $exeDestino
        Atalho $menu $exeDestino
    }

    Info "== Impressora"
    Fazer "Desligar a reimpressão automática da impressora (SET REPRINT OFF)" {
        & (Join-Path $PSScriptRoot "desligar-reimpressao.ps1") -Impressora $FilaSaida -SemPausa | Out-Null
    }

    Info "== Ligar a fila '$Fila' ao LabelGuard"
    Fazer "Abrir o LabelGuard (como o usuário conectado)" {
        Start-Process explorer.exe -ArgumentList "`"$exeDestino`""
        Start-Sleep -Seconds 3
    }
    if ($filaObj.PortName -ne $Porta) {
        Fazer "Trocar a porta de '$Fila' para '$Porta'" {
            Set-Printer -Name $Fila -PortName $Porta
        }
    }
    Fazer "Garantir que '$Fila' não está como 'Usar impressora offline'" {
        Get-CimInstance Win32_Printer | Where-Object Name -eq $Fila | Set-CimInstance -Property @{ WorkOffline = $false }
    }

    Write-Host ""
    if ($Simular) {
        Write-Host "Simulação concluída. Nada foi alterado." -ForegroundColor Yellow
    }
    else {
        Write-Host "LabelGuard instalado." -ForegroundColor Green
        Write-Host "- O Papersystem continua imprimindo em '$Fila'."
        Write-Host "- Nunca imprima direto em '$FilaSaida'."
        Write-Host "- Na primeira abertura, o LabelGuard pede para criar a senha do supervisor."
        Write-Host "- Para desfazer: desinstalar.ps1"
    }
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
