# Protocolo CloudDocs (iCloud Drive)

Documentação do protocolo privado usado pelo sync. Fonte primária: `pyicloud`
(`services/drive.py` e `base.py`) e comportamento validado do `gordonaspin/icloudds`.

> A Apple não oferece API pública para o iCloud Drive. CloudDocs é protocolo privado;
> CloudKit só acessa containers do próprio app. Mitigações adotadas: testes de contrato com
> fixtures gravadas do iCloud real, 1 réplica, credenciais via `infra-env`/BWS, alerta de
> sessão expirada.

## Autenticação (sessão injetada — sem SRP)

```
POST https://setup.icloud.com/setup/ws/1/accountLogin
Body JSON:
  dsWebAuthToken      = session_token (do session JSON injetado)
  accountCountryCode  = account_country
  trustToken          = trust_token
Headers:
  X-Apple-OAuth-Client-Id / X-Apple-Widget-Key = d39ba9916b...815d (widget key)
  X-Apple-Session-Token / X-Apple-ID-Session-Id / X-Apple-TwoSV-Trust-Token
Resposta 200:
  webservices.drivews.url  → operações (DriveWS)
  webservices.docws.url    → upload/download (docws)
Resposta de erro / inválida → sessão expirada: ALERTAR e parar. Nunca SRP/senha.
```

## Operações

### Listar filhos de uma pasta

```
POST {drivews}/retrieveItemDetailsInFolders
Body: [ { "drivewsid": "FOLDER::com.apple.CloudDocs::<id>", "partialData": false } ]
Resposta: [ { ...nó pedido..., "fileCount": N, "items": [ {filho}, ... ] } ]
  - raiz:    FOLDER::com.apple.CloudDocs::root
  - lixeira: TRASH::com.apple.CloudDocs::root
  - nó FILE: drivewsid, docwsid, parentDrivewsid, etag, extension, name, type,
             size, dateChanged, dateModified
```

### Download

```
GET {docws}/ws/com.apple.CloudDocs/download/by_id?document_id=<docwsid>&clientId=...
Resposta: { "data_token": { "url": ... }, "package_token": { "url": ... } }
  → GET na url (data_token tem prioridade)
```

### Upload (3 passos)

```
1) POST {docws}/ws/com.apple.CloudDocs/upload/web?clientId=...&token=<X-APPLE-WEBAUTH-VALIDATE>
   Body: { "filename", "type": "FILE", "content_type", "size" }
   Resposta: [ { "document_id", "url" } ]
2) POST na url (multipart/form-data, campo = nome do arquivo)
   Resposta: { "singleFile": { "fileChecksum", "wrappingKey", "referenceChecksum", "size", "receipt"? } }
3) POST {docws}/ws/com.apple.CloudDocs/update/documents?clientId=...
   Header: Content-Type: text/plain
   Body: {
     "data": { "signature", "wrapping_key", "reference_signature", "size", "receipt"? },
     "command": "add_file",
     "document_id",
     "path": { "starting_point": "MAIN_DATABASE", "root": "<pasta pai>",
               "path_components": [ nome ] },
     "allow_conflict": true,
     "file_flags": ["IS_WIDGET_COMPATIBLE"],
     "mtime"/"btime": epoch millis
   }
```

### Criar pasta

```
POST {drivews}/createFolders
Body: { "folders": [ { "destinationDriveWsId": "<pai>", "name": "<nome>" } ] }
```

## Comportamentos observados

- **ZONE_BUSY**: writes concorrentes no CloudDocs retornam erro → fila serializada
  (`WriteGate`, 1 por vez).
- **Timestamps**: UTC; o iCloud trunca para segundos no upload → comparar sempre com
  arredondamento a 1s (evita loop).
- **Arquivos vazios**: upload de vazio recém-criado sobe; atualização de vazio mais novo
  não sobe (regra do icloudds: `size > 0`).
- **Lixeira "dentro" do root**: o `fileCount` não diminui com trash (sanity check usa o
  contador, não a árvore completa).
- **Ignorar**: `app_library`, `.DS_Store`, `.com-apple-bird*`.
- **`.app`**: vira pasta expandida no iCloud (não sincronizamos bundle como arquivo único).