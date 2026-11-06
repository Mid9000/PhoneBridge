# PhoneBridge protocol v1 (contract between Windows server, iOS app, Shortcuts)

Source of truth for routes: SPEC.md §4. This file pins the details both sides must share. If you change anything here, change both sides.

## Transport
- HTTPS (self-signed, fingerprint pinned) on **47821**: all routes. HTTP on **47822**: only `[shortcuts]` routes, private IPs only, `Authorization: Bearer <token>` required, can be disabled.
- mDNS service `_phonebridge._tcp.local`, TXT: `v=1`, `id=<deviceId>`, `name=<pc name>`.
- All JSON UTF-8. Errors: `{ "error": "<code>", "message": "<human text>" }` with proper HTTP status (400, 401, 403, 404, 409, 410, 413, 429, 507). Machine-readable spec: `docs/openapi.yaml`.
- Auth: `Authorization: Bearer <token>` on everything except `POST /v1/pair` and `GET /v1/status` (status without token returns only `{ "name", "version" }`).

## Pairing
QR payload (JSON, UTF-8, shown as QR): 
`{"v":1,"host":"192.168.1.20","port":47821,"certSha256":"<hex lowercase, 64 chars>","pairingSecret":"<base64url 32 bytes>","deviceId":"<guid>","name":"PC d'Arthur"}`

`certSha256` = SHA-256 of the DER-encoded leaf certificate (`X509Certificate2.GetCertHash(SHA256)` on Windows, `SecCertificateCopyData` on iOS), not the SPKI. The iOS app pins on it and skips CA validation.

`POST /v1/pair` body: `{ "pairingSecret": "...", "deviceName": "iPhone d'Arthur", "deviceId": "<guid of the phone>" }`
Response 200: `{ "token": "<base64url 32 bytes>", "serverName": "...", "httpPort": 47822 }`. Secret is single-use, expires after 5 min (410 when expired or already used, 401 when unknown). Rate limit: 5 failures/min/IP -> 429. Re-pairing the same `deviceId` replaces its old token. Auth failures on other routes: 10/min/IP -> 429.

## Routes (all under `/v1`)
| Route | Request | Response |
|---|---|---|
| `GET /status` | - | `{ "name", "version", "features": { "shortcutsHttp": bool, "bluetooth": "off/connected/..." }, "time": ISO8601 }` |
| `POST /files` | body = raw bytes; headers `X-Filename` (URL-encoded UTF-8), `X-Size`, optional `X-Sha256` (hex) | 201 `{ "savedAs": "name (1).jpg", "size": n, "sha256": "hex" }`; 400 on hash or `X-Size` mismatch (nothing kept); 413 too large; 507 disk full |
| `GET /outbox` | - | `{ "items": [ { "id", "kind": "file/clipboard/link/reply", "name?", "size?", "text?", "url?", "to?", "createdAt" } ] }` |
| `GET /outbox/{id}` | supports `Range` | raw bytes (file) or JSON for non-file kinds |
| `DELETE /outbox/{id}` | - | 204 |
| `POST /links` | `{ "url", "title?" }` | 204 (opened on PC; http/https only, else 400) |
| `POST /clipboard` | `{ "kind": "text/url/image", "text?", "imageBase64?" }` | 204 |
| `GET /clipboard` | - | latest PC clipboard `{ "kind", "text?", "imageBase64?", "updatedAt" }` or 204 if none |
| `POST /messages` | `{ "sender", "body", "service": "sms/imessage", "receivedAt": ISO8601 }` | 204 (stored, toast shown, OTP detected) |
| `POST /battery` | `{ "level": 0-100, "charging": bool, "source?": "shortcut/app" }` | 204 |
| `GET /events` (WebSocket upgrade) | token via `Authorization` header or `?token=` | server pushes `{ "type": "outbox", "count": n }` and `{ "type": "status", ... }` |

`[shortcuts]` routes (also on HTTP 47822): `/files`, `/links`, `/clipboard` (GET+POST), `/messages`, `/battery`, plus `GET /outbox*` and `DELETE /outbox/{id}` (needed by the relay Shortcut) and `GET /status`.

## Rules
- File names sanitized server-side (no path separators, Windows-reserved names/chars removed, collisions -> `name (1).ext`). Default dir `Downloads\PhoneBridge\`.
- No message/clipboard content in logs.
- Received files get a Mark-of-the-Web (`Zone.Identifier`, ZoneId=3) so Windows treats them like downloads.
- Tokens are stored on the PC only as SHA-256 hashes.
