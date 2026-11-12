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
- [ ] **4. Motor de decisão** – imprimir / bloquear / reimpressão autorizada
- [ ] **5. Receptor e envio** – servidor TCP local (porta 9100) e envio para impressora de rede
- [ ] **6. Aplicativo Windows** – ícone na bandeja, janela de alerta, senha de supervisor, envio para impressora USB
- [ ] **7. Instalação** – script PowerShell que cria a impressora protegida, guia de configuração

## Estrutura

```
src/LabelGuard.Core     lógica independente de sistema operacional (testável)
tests/LabelGuard.Tests  testes automatizados (xUnit)
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

## Analisar um trabalho capturado

```bash
dotnet run --project tools/LabelGuard.Inspect -- C:\temp\etiqueta.prn [mais arquivos] [--codes] [--pattern "<regex>"]
```

Mostra cada etiqueta do trabalho, o ID lido e se ele já apareceu antes (no mesmo
arquivo ou em um arquivo anterior da lista). `--codes` mostra o conteúdo dos QR codes.

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
