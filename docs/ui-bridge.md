# UI ↔ host bridge (WebView2)

Contract between the Svelte UI (`windows/ui`) and the C# host (`PhoneBridge.Host`). TypeScript source of truth: `windows/ui/src/lib/types.ts` (`HostMessage`, `UiMessage`). Change both sides together.

## Transport
- UI → host: `window.chrome.webview.postMessage(obj)`. Host reads `e.WebMessageAsJson` in `CoreWebView2.WebMessageReceived`.
- Files dropped/picked in the UI: `chrome.webview.postMessageWithAdditionalObjects({type:'sendFiles', names}, File[])`. Host reads `e.AdditionalObjects` (`CoreWebView2File.Path`), so it gets real paths and the UI never reads file contents.
- Host → UI: `CoreWebView2.PostWebMessageAsJson(json)`. The UI listens with `chrome.webview.addEventListener('message')`.
- Every message is a JSON object `{ "type": "...", ...fields }`. The UI drops anything that is not an object with a known `type` (`parseHostMessage`). Timestamps are ISO 8601 UTC strings, ids are opaque strings.
- No `chrome.webview` (plain browser, `npm run dev`, `design/preview.html`): the UI uses the in-browser mock host (`src/lib/mock.ts`) with a fake dataset.

## Hosting the build
- `npm run build` writes `windows/ui/dist/` with relative paths (`base: './'`).
- Serve it with `CoreWebView2.SetVirtualHostNameToFolderMapping("app.phonebridge", <dist>, CoreWebView2HostResourceAccessKind.DenyCors)` and navigate to `https://app.phonebridge/index.html`. Do not use `file://`: module scripts are blocked there.
- The page ships a CSP (`default-src 'self'`): no network access besides its own origin.
- Tray popup: load `index.html#quick` in a small borderless window; it renders only the quick menu.
- Suggested minimum window size: 900 × 600.
- Set `CoreWebView2Settings.AreBrowserAcceleratorKeysEnabled = false` (otherwise Ctrl+N/Ctrl+P etc. trigger browser actions), and `AreDefaultContextMenusEnabled = false` outside debug builds.

## Window
- Borderless window, DWM rounded corners, Mica backdrop. When Mica is on, send `host.mica = true` in the snapshot: the UI makes `html`/sidebar backgrounds transparent so Mica shows through the sidebar.
- Set `DefaultBackgroundColor` to transparent when Mica is on.
- Drag: toolbar and sidebar top carry `app-region: drag` (enable `CoreWebView2Settings.IsNonClientRegionSupportEnabled`). Fallback when that is unavailable: the UI sends `{type:'window', action:'drag'}` on mouse-down in a drag area (host: `ReleaseCapture` + `WM_NCLBUTTONDOWN/HTCAPTION`) and `action:'maximize'` on double-click.
- Window style (`settings.windowStyle`): `mac` = three round buttons top-left (default), `windows` = caption buttons top-right. Both send the same `window` actions.

## Startup
1. UI loads and sends `{type:'ready'}`.
2. Host answers `{type:'state', snapshot}` (full `Snapshot`). Until then the UI shows "Chargement…"; after 4 s without answer it shows "Le service PhoneBridge ne répond pas" with a retry (sends `ready` again).
3. Host then pushes incremental messages below. Sending a full `state` again at any time is allowed (e.g. after reconnection).

## Snapshot (`state.snapshot`)
| Field | Type | Notes |
|---|---|---|
| `host` | `{ version, mica, maximized }` | |
| `connection` | `{ paired, phoneName, pcName, wifi, bluetooth, bluetoothIssue?, lastSeenAt }` | `wifi`/`bluetooth`: `connected` `connecting` `disconnected` `off` `unavailable`. `bluetooth: 'off'` = disabled in settings (not a degraded link, no banner). `bluetoothIssue`: `noAdapter` `notPaired` `notificationsDenied` `messagesDenied` `unreachable`, sent even while partly connected; the UI words it |
| `battery` | `{ level: 0-100 \| null, charging, updatedAt, source? }` | `null` = never received |
| `conversations` | `Conversation[]` | `{ id, name, handle, service: 'imessage'\|'sms', lastMessage, lastAt, unread }`, newest first |
| `notifications` | `PhoneNotification[]` | `{ id, appId, appName, category, title, subtitle?, body, at, positiveAction?, negativeAction? }` |
| `transfers` | `Transfer[]` | `{ id, name, size, transferred, direction: 'toPc'\|'toPhone', status: 'queued'\|'active'\|'done'\|'failed'\|'cancelled', at, error? }`, newest first |
| `clipboard` | `ClipboardItem[]` | `{ id, kind: 'text'\|'url'\|'image', text?, imageDataUrl? (small thumbnail), origin: 'phone'\|'pc', at }`. Sensitive items (OTP, passwords) are never sent |
| `calls` | `Call[]` | `{ id, name, handle, kind: 'incoming'\|'outgoing'\|'missed', at, durationSec?, ringing? }` |
| `devices` | `PairedDevice[]` | `{ id, name, pairedAt, lastSeenAt, current }` |
| `settings` | `Settings` | `{ windowStyle, theme: 'system'\|'light'\|'dark', toasts, clipboardAuto, otpAutoCopy, keepHistory, helloLock, shortcutsHttp, bluetooth, downloadDir }`. `bluetooth` (default `false`): ANCS/MAP/PBAP on or off |

Threads are not in the snapshot: they are loaded on demand (`openConversation` → `messages`).

## Host → UI
| `type` | Fields | Effect in the UI |
|---|---|---|
| `state` | `snapshot` | Replace everything |
| `connection` | `connection` | Status banner, sidebar dot, iPhone page |
| `battery` | `battery` | Gauges |
| `conversations` | `conversations` | Replace list (send after any change: new message, read state) |
| `messages` | `conversationId, messages[]` | Replace one thread (oldest first) |
| `message` | `message` | Upsert one message by `id` (incoming, or status change of a sent one) |
| `notification` / `notificationRemoved` / `notifications` | `notification` / `id` / `notifications[]` | Add-or-update / remove / replace |
| `transfer` | `transfer` | Upsert by `id` (progress updates: throttle to ~4/s) |
| `clipboard` | `items[]` | Replace history |
| `call` | `call` | Upsert by `id`; `ringing: true` shows the Answer/Decline banner |
| `devices` | `devices[]` | Replace |
| `settings` | `settings` | Authoritative settings (UI applies changes optimistically before) |
| `window` | `maximized` | Toggles restore/maximize icon |
| `navigate` | `section, conversationId?` | Switch section (e.g. user clicked a Windows toast) |
| `sectionError` | `section, message \| null` | Show / clear the error state of `messages`, `notifications`, `files`, `clipboard`, `calls` |
| `toast` | `text, kind?: 'info'\|'error'` | Short in-app confirmation (French text, written by the host) |

`Message`: `{ id, conversationId, fromMe, body, at, service, status?: 'sending'\|'sent'\|'queued'\|'failed', via?: 'bluetooth'\|'relay', error? }`. `queued` = waiting for the Shortcuts relay (SPEC §6 piste 3); the UI says so honestly.

## UI → host
| `type` | Fields | Host should |
|---|---|---|
| `ready` | | Send `state` |
| `window` | `action: 'minimize'\|'maximize'\|'close'\|'drag'` | `maximize` toggles maximize/restore and answers `window`; `close` hides to tray |
| `openConversation` | `conversationId` | Mark read (MAP SetMessageStatus when available), answer `messages` + `conversations`. Treat it as the open thread until another one is opened |
| `sendMessage` | `clientId, conversationId? \| to?, body` | Use `clientId` as the message `id`; push `message` with `status:'sending'`, then `sent` / `queued` / `failed`. With `to` (new message): find or create the conversation and send `navigate` with its id |
| `retryMessage` | `messageId` | Resend a failed message |
| `dismissNotification` | `id` | Remove locally, and ANCS negative action (Clear) on the iPhone when it offers one (never on calls), then `notificationRemoved` |
| `notificationAction` | `id, action: 'positive'\|'negative'` | ANCS PerformNotificationAction; the iPhone then removes it (`notificationRemoved`). Error `toast` when Bluetooth is not connected |
| `clearNotifications` | | Clear list, answer `notifications: []` |
| `sendFiles` | `names[]` + File objects | Queue PC → iPhone transfers (outbox), push `transfer` updates |
| `cancelTransfer` / `retryTransfer` / `revealTransfer` | `id` | Cancel / restart / open Explorer with the file selected |
| `openDownloads` | | Open the reception folder |
| `copyClipboardItem` | `id` | Put the item on the Windows clipboard (excluded from Win+V history), may answer a `toast` |
| `deleteClipboardItem` / `clearClipboard` | `id` / – | Answer `clipboard` |
| `pushClipboard` | | Publish the current PC clipboard to the outbox |
| `openLink` | `url` | Open in the default browser, **http/https only** |
| `callAction` | `id, action: 'answer'\|'decline'` | ANCS positive/negative action on the incoming-call notification (`call.id` = its notification id). Error `toast` when Bluetooth is not connected |
| `startPairing` | | Show the pairing QR flow |
| `revokeDevice` | `id` | Revoke the device token, answer `devices` |
| `setSettings` | `patch: Partial<Settings>` | Persist, apply, answer `settings` |
| `pickDownloadDir` | | Folder picker, then `settings` |
| `retrySection` | `section` | Reload that data, then `sectionError` with `message: null` |
| `openMainWindow` | | From the tray popup: show and focus the main window |

## Keyboard (UI side)
`Ctrl+K` palette · `Ctrl+N` new message · `Ctrl+1…6` sections · `Ctrl+,` settings · arrows in the sidebar, conversation list, palette and segmented controls · `Enter` sends, `Shift+Enter` new line · `Esc` closes palette/popover.

## Preview / dev
- `npm run dev` → http://localhost:5173 with mock data. Query params: `scenario=populated|empty|disconnected|unpaired|error|call`, `theme=light|dark`, `window=mac|windows`, `section=messages|notifications|files|clipboard|calls|phone|settings`, `open=palette|quick`.
- `npm run design` rebuilds `design/preview.html` (single self-contained file, same code + mock, with a floating bar to switch theme, window style and scenario).
