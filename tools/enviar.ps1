# Envia um arquivo .prn ao LabelGuard pela porta local, como se viesse da fila LABEL.
# Uso: powershell -ExecutionPolicy Bypass -File tools\enviar.ps1 C:\temp\teste3.prn [porta]
param(
    [Parameter(Mandatory = $true)][string]$Arquivo,
    [int]$Porta = 9100
)

$dados = [IO.File]::ReadAllBytes((Resolve-Path $Arquivo))
$cliente = New-Object Net.Sockets.TcpClient('127.0.0.1', $Porta)
try {
    $stream = $cliente.GetStream()
    $stream.Write($dados, 0, $dados.Length)
    $cliente.Client.Shutdown([Net.Sockets.SocketShutdown]::Send)
    # Espera o LabelGuard terminar de ler e fechar a conexão.
    $null = $stream.Read((New-Object byte[] 1), 0, 1)
}
finally {
    $cliente.Close()
}
"Enviado: $Arquivo ($($dados.Length) bytes) para 127.0.0.1:$Porta"
