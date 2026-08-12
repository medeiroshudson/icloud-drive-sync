# Arquitetura

## Princípios

- **TDD**: nenhum código de produção sem teste que falhou antes (RED → GREEN → REFACTOR).
  A implementação foi construída em "tracer bullets" verticais (TB1–TB7), cada um validando
  um comportamento E2E do protocolo real.
- **KISS**: sem camadas genéricas. DI manual no `Program.cs`, retry simples, `SemaphoreSlim`
  para serialização — nada de Polly/MediatR/event bus.
- **DRY**: regras de negócio com fonte única de verdade (`PathRules`, `TimestampRules`,
  `WriteGate`, fixtures de contrato em `tests/`).

## Componentes

| Componente | Responsabilidade |
|---|---|
| `Auth/ICloudSession` | Parse do JSON de sessão do pyicloud (`session_token`, `trust_token`, ...) |
| `Auth/ICloudAuthClient` | `accountLogin` com `dsWebAuthToken` → `WebServices` (drivews + docws); `AuthRequired` sem tentar SRP |
| `Drive/ICloudDriveClient` | Protocolo CloudDocs: listar, fileCount, download, upload (3 passos), createFolders |
| `Drive/DriveNode` | Nó do iCloud (espelha o item JSON do `retrieveItemDetailsInFolders`) |
| `Sync/LocalScanner` | Varre o diretório local → `Dictionary<path, LocalEntry>` (mtime UTC arredondado) |
| `Sync/CloudScanner` | Scan recursivo do iCloud + sanity check de `fileCount` |
| `Sync/DiffEngine` | **Função pura** (sem I/O): árvores → plano de ações |
| `Sync/ActionApplier` | Executa o plano; writes em iCloud serializados (`WriteGate`); writes locais suprimem o coalescer; `dryRun` = shadow |
| `Sync/LocalChangeCoalescer` | Coalesce de eventos do filesystem + supressão aninhada |
| `Sync/SyncLoop` | Ciclo: fileCount → refresh → diff → aplicar; refresh periódico forçado |
| `Sync/PathRules` | Ignora `.DS_Store`, `.com-apple-bird*`, `app_library` |
| `Sync/TimestampRules` | Arredonda a 1s (UTC) — espelha o `_round_seconds` do icloudds |
| `Cli/CliOptions` | Parse dos args (compatível com o icloudds) + `--dry-run` |
| `Cli/NetscapeCookieReader` | Lê cookies no formato Netscape (LWPCookieJar) |

## Fluxo de um ciclo (SyncLoop.RunOnceAsync)

1. `GetRootFileCountAsync` (sanity rápido) — se o `fileCount` não mudou e o período de
   refresh não expirou, termina aqui.
2. `CloudScanner.ScanRootAsync` — scan recursivo (lembra: o nó raiz vem no `[0]` do array).
3. `LocalScanner.Scan` — scan local com paths normalizados (`/`).
4. `DiffEngine.Diff` — decide `Upload/Download/MkDir*/DeleteLocal`.
5. `ActionApplier.ApplyAsync` — ordena pastas primeiro; aplica (ou apenas reporta em dry-run).

## Decisões que evitaram bugs

- **Contrato real do `retrieveItemDetailsInFolders`**: resposta é um **array**; `[0]` é o
  próprio nó pedido (com `items` e `fileCount`). Um rascunho inicial (objeto único `{items}`)
  foi corrigido no TB2 por fidelidade ao pyicloud.
- **Dois webservices**: `drivews` (operações) e `docws` (upload/download) — URLs distintas
  vindas do `accountLogin`.
- **Download é GET por query** (`download/by_id?document_id=`), não POST com body.
- **Trash não ressuscita**: item na lixeira do iCloud não sobe; `DeleteLocal`.
- **Arquivo vazio mais novo não sincroniza** (regra do icloudds: `size > 0`).

## Pendências (roadmap)

- Leitura da trash no ciclo + `FileSystemWatcher` + deploy kustomize — ver README.