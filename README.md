# LabelGuard – Controle de etiquetas duplicadas

Gerenciador de impressão para Windows que impede que uma etiqueta com o mesmo
número identificador seja impressa duas vezes sem autorização.

## O problema

O Papersystem imprime etiquetas de produtos e paletes. Quando a bobina acaba no
meio de uma impressão, depois da troca o Windows ou a impressora repete a última
etiqueta. O operador nem sempre percebe e cola duas etiquetas com o mesmo
identificador, o que prejudica rastreabilidade e movimentação de estoque.

## Como vai funcionar

```
Papersystem ──► Impressora "Etiquetas (Protegida)"      (fila do Windows, porta 127.0.0.1:9100)
                        │
                        ▼
                  LabelGuard (este programa)
                   1. recebe o trabalho de impressão
                   2. extrai o número identificador de cada etiqueta
                   3. consulta o histórico (SQLite)
                        ├── número novo     → envia para a impressora real e registra
                        └── número repetido → BLOQUEIA e mostra alerta
                                               (reimpressão só com senha de supervisor)
                        │
                        ▼
                  Impressora real (USB ou rede)
```

Exemplo:

| Etiqueta | Situação            | Resultado            |
|----------|---------------------|----------------------|
| 1196924  | primeira impressão  | imprime              |
| 1196925  | primeira impressão  | imprime              |
| 1196924  | enviada novamente   | bloqueia + alerta    |

### Limitação importante

Algumas impressoras térmicas (Zebra, TSC, Argox…) reimprimem sozinhas a última
etiqueta depois de um erro (fim de bobina, por exemplo). Essa cópia sai da memória
da própria impressora e **não passa pelo Windows**, então nenhum programa no PC
consegue barrar. Essa função precisa ser desligada na impressora:

- Zebra (ZPL): `^XA^JZN^XZ`
- TSC (TSPL): `SET REPRINT OFF`

## Etapas de desenvolvimento

- [x] **1. Estrutura** – solução .NET 8, projetos `Core` e `Tests`, repositório git
- [x] **2. Extração do identificador** – separar etiquetas de um trabalho (ZPL, TSPL) e achar o ID por expressão regular configurável
- [x] **3. Histórico** – banco SQLite com etiquetas impressas e log de eventos
- [x] **4. Motor de decisão** – imprimir / bloquear / reimpressão autorizada
- [x] **5. Receptor e envio** – servidor TCP local (porta 9100) e envio RAW para a impressora USB pelo spooler
- [ ] **6. Aplicativo Windows** – ícone na bandeja, janela de alerta, senha de supervisor, envio para impressora USB
- [ ] **7. Instalação** – script PowerShell que cria a impressora protegida, guia de configuração

## Estrutura

```
src/LabelGuard.Core     lógica independente de sistema operacional (testável)
tests/LabelGuard.Tests  testes automatizados (xUnit)
src/LabelGuard.Windows  envio RAW para filas do Windows (winspool)
src/LabelGuard.Server   executável de console: receptor + motor + envio
tools/LabelGuard.Inspect  analisa trabalhos capturados em arquivo (.prn)
```

## Histórico

`LabelHistory` guarda em SQLite (Microsoft.Data.Sqlite):

| Tabela   | Conteúdo                                                                 |
|----------|--------------------------------------------------------------------------|
| `prints` | cada impressão: ID, data/hora (UTC), supervisor que autorizou, trabalho  |
| `events` | log: impressa, bloqueada, reimpressão autorizada, sem ID, erro           |

Um índice único parcial (`label_id` onde `authorized_by IS NULL`) garante no próprio
banco que cada ID tem só uma impressão sem autorização. `TryRecordPrint` retorna
`false` para um ID já impresso, mesmo com trabalhos chegando ao mesmo tempo.
Reimpressões liberadas entram com o nome do supervisor.

## Motor de decisão

`PrintGuard` processa cada trabalho etiqueta por etiqueta, na ordem:

| Situação                                   | Resultado        |
|--------------------------------------------|------------------|
| ID novo                                    | imprime          |
| ID já impresso (antes ou no mesmo trabalho) | bloqueia         |
| etiqueta pede mais de 1 cópia              | bloqueia (*)     |
| ID não encontrado                          | bloqueia (*)     |
| envio para a impressora falhou             | ID liberado, resto do trabalho não é enviado |

(*) configurável em `GuardOptions`.

O ID é reservado no histórico antes do envio e liberado se o envio falhar: uma
etiqueta que não saiu não fica marcada como impressa. Etiquetas bloqueadas podem
ser liberadas com `AuthorizeAsync`, que registra o supervisor.

## Testar com a impressora

O Papersystem continua imprimindo na fila `LABEL`, com o mesmo driver e configuração
(o driver desenha a etiqueta como imagem, e qualquer diferença de configuração muda o
resultado). Só a porta dela passa a ser o LabelGuard. Uma segunda fila na USB recebe as
etiquetas liberadas em modo RAW, que não passa pelo driver:

```
Papersystem -> "LABEL" (porta 127.0.0.1:9100) -> LabelGuard.Server -> "LabelGuard - NAO USAR" (USB001, RAW) -> impressora
```

1. Configurar as filas (PowerShell como administrador):

   ```powershell
   Add-PrinterPort -Name "LabelGuard_9100" -PrinterHostAddress "127.0.0.1" -PortNumber 9100
   Add-Printer -Name "LabelGuard - NAO USAR" -DriverName "LABEL" -PortName "USB001"
   Set-Printer -Name "LABEL" -PortName "LabelGuard_9100"
   ```

2. Iniciar o LabelGuard:

   ```bash
   dotnet run --project src/LabelGuard.Server -- --impressora "LabelGuard - NAO USAR"
   ```

3. Imprimir pelo Papersystem normalmente, **na fila `LABEL`**. As repetidas aparecem em
   vermelho no console. Nunca imprimir direto na `LabelGuard - NAO USAR`: a etiqueta sai sem
   proteção e com a configuração padrão do driver (layout errado).
   Com o LabelGuard fechado, os trabalhos ficam parados na fila `LABEL` e saem quando ele abrir.

`--salvar <pasta>` grava cada trabalho recebido, para comparar com uma captura direta.

Se a etiqueta sair desalinhada mesmo com o trabalho idêntico à captura original, imprimir
uma vez sem o LabelGuard (`Set-Printer -Name "LABEL" -PortName "USB001"`). Se também sair
errado, o problema é a impressora (calibração do sensor de gap ou defeito), não o LabelGuard.
O histórico fica em `C:\ProgramData\LabelGuard\historico.db`.

Para desfazer:

```powershell
Set-Printer -Name "LABEL" -PortName "USB001"
Remove-Printer -Name "LabelGuard - NAO USAR"
Remove-PrinterPort -Name "LabelGuard_9100"
```

## Analisar um trabalho capturado

```bash
dotnet run --project tools/LabelGuard.Inspect -- C:\temp\etiqueta.prn [mais arquivos] [--codes] [--pattern "<regex>"]
```

Mostra cada etiqueta do trabalho, o ID lido e se ele já apareceu antes (no mesmo
arquivo ou em um arquivo anterior da lista). `--codes` mostra o conteúdo dos QR codes.

Para simular o LabelGuard completo, com histórico gravado entre execuções:

```bash
dotnet run --project tools/LabelGuard.Inspect -- C:\temp\etiqueta.prn --db C:\temp\historico.db [--saida C:\temp\liberadas.prn]
```

Na primeira vez as etiquetas saem como IMPRESSA; repetindo o comando, saem como
BLOQUEADA. `--saida` grava o que seria enviado à impressora. Apague o `.db` para recomeçar.

## Extração do identificador

`LabelSplitter` detecta a linguagem do trabalho e separa as etiquetas:

| Linguagem | Detecção          | Fim de cada etiqueta | Cópias        |
|-----------|-------------------|----------------------|---------------|
| ZPL       | contém `^XA`      | `^XZ`                | `^PQn`        |
| TSPL      | linha `PRINT m`   | linha `PRINT m[,n]`  | `m × n`       |
| outra     | -                 | trabalho inteiro     | 1             |

Os bytes originais de cada etiqueta são mantidos para reenvio sem alteração.

### Impressora POS-9210 (driver Windows)

O driver envia TSPL com a etiqueta inteira como imagem (`BITMAP`), sem texto legível.
O identificador é lido do QR code impresso pelo Papersystem:

```
https://app.papersystem.com.br/d/etiqueta?pallet=3931&fornecedor=...&largura=1500&...
```

`BarcodeScanner` converte o bitmap e decodifica o QR (ZXing.Net). `IdExtractor` aplica
uma expressão regular com o grupo nomeado `id`, primeiro no conteúdo dos códigos e depois
nos comandos de texto. Padrão inicial: `[?&]pallet=(?<id>\d+)` (número do pallet).

Para capturar um trabalho real: nas propriedades da impressora, aba Portas, adicionar
uma Local Port com o caminho de um arquivo (ex.: `C:\temp\etiqueta.prn`), imprimir e
depois voltar a porta original.

## Compilar e testar

```bash
dotnet build
dotnet test
```
