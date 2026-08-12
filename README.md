# icloud-drive-sync

Daemon de sincronização bidirecional do **iCloud Drive** em .NET (net10.0), seguindo TDD/KISS/DRY.
Substitui o deployment `icloud-sync` (gordonaspin/icloudds — Python) na infraestrutura do Hudson.

> ⚠️ **Autenticação**: este projeto **nunca usa senha/SRP** com o Apple ID
> `medeiroshudson@outlook.com` (provável bloqueio imediato da conta — `-20209`/`-20101`).
> Usa apenas **sessão injetada** (cookies + `session_token` coletados do browser, valem meses).
> Se a sessão expirar, o daemon **alerta e para** — não re-autentica sozinho.

## Como funciona

```
LocalScanner  ──► LocalTree ──┐
                              ├──► DiffEngine (função pura) ──► plano de ações ──► ActionApplier (serializado)
CloudScanner  ──► RemoteTree ─┘                                                      │
   (fileCount sanity check + scan recursivo)                                          ▼
                                                                              iCloud Drive (CloudDocs)
```

- **Check periódico**: sanity check do `fileCount` do root a cada `--icloud-check-period` (60s);
  refresh completo (scan recursivo + diff) quando o contador muda ou a cada `--icloud-refresh-period` (600s).
- **Writes serializados**: 1 operação de escrita por vez (`WriteGate`) — o CloudDocs responde
  `ZONE_BUSY` em concorrência.
- **Timestamps**: mtime/`dateModified` normalizados para UTC e arredondados a 1s (evita loop de sync).
- **Regras**: arquivo vazio mais novo não sincroniza; mesmo timestamp + tamanho diferente = sem ação;
  `.DS_Store`, `.com-apple-bird*` e `app_library` são ignorados; lixeira do iCloud remove a cópia local.
- **`--dry-run` (shadow)**: reporta o plano de ações sem escrever nada (modo de validação).

## Uso

```
icloud-drive-sync -d /drive \
  --cookie-directory /cookies \
  --account medeiroshudson@outlook.com \
  --icloud-refresh-period 600 \
  --icloud-check-period 60 \
  [--ignore-regexes '\.tmp$|\.DS_Store'] \
  [--dry-run]
```

O `--cookie-directory` deve conter (mesmo formato do pyicloud):
- `<conta.sem.@>.session` — JSON com `session_token`, `session_id`, `trust_token`, `account_country`
- `<conta.sem.@>.cookiejar` — cookies no formato Netscape (LWPCookieJar)

## Desenvolvimento

```bash
dotnet test ICloudDriveSync.slnx      # 65 testes — TDD (RED → GREEN → REFACTOR)
```

- `src/ICloudDriveSync/` — `Auth/` (sessão + accountLogin), `Drive/` (protocolo CloudDocs),
  `Sync/` (scan, diff, applier, coalescer, loop), `Cli/` (args + cookies Netscape)
- `tests/ICloudDriveSync.Tests/` — xUnit; protocolo via `HttpMessageHandler` fake com fixtures reais;
  diff/scanners/testes de loop com diretórios reais (sem mock)
- `docs/arquitetura.md` — decisões e mapa do código
- `docs/protocolo-icloud.md` — endpoints e fluxos do CloudDocs usados (fonte: pyicloud)

## Docker / CI

- `Dockerfile` multi-stage (runtime puro, sem ASP.NET)
- Workflow GHCR: testes → push `ghcr.io/medeiroshudson/icloud-drive-sync` em `main`/tags

## Roadmap

- [ ] Leitura da lixeira do iCloud no ciclo (`TRASH::com.apple.CloudDocs::root`) → DeleteLocal
- [ ] `FileSystemWatcher` (watcher local → sync imediato, em vez de esperar o polling)
- [ ] Deploy kustomize no HMedeiros.Infra (substitui o deployment `icloud-sync`, Recreate)