# PhoneBridge iOS

SwiftUI app (iOS 17+) + Share Extension + Widgets/Control extension. No third-party dependency. Built only on GitHub Actions (no Mac): see `.github/workflows/ios.yml`, install steps in [docs/ios-install.md](../docs/ios-install.md).

## Layout
| Path | Target(s) | Content |
|---|---|---|
| `project.yml` | all | XcodeGen spec. Info.plist / entitlements are generated into `Generated/` (gitignored) |
| `Sources/Shared` | app, share, tests | Protocol models, `BridgeClient` (pinned URLSession), `Pinning`, `Keychain`, `PairingStore`, `BackgroundUploader` |
| `Sources/WidgetShared` | app, widgets | `OpenBridgeActionIntent` used by widget and Control Center buttons |
| `Sources/App` | app | Screens, `AppModel`, Bonjour `Discovery`, `/events` WebSocket, App Intents |
| `Sources/ShareExtension` | share | "Envoyer au PC" share sheet |
| `Sources/Widgets` | widgets | Home screen widget (iOS 17) + 2 Control Center controls (iOS 18, `#available`) |
| `Tests` | tests | Unhosted XCTest: models, pinning helpers, client against a URLProtocol stub |
| `Resources` | app, share, widgets | `Localizable.strings`: keys are French (dev language), `en.lproj` translates |

Build locally on a Mac (if one ever appears): `brew install xcodegen && mkdir -p Generated && xcodegen generate && open PhoneBridge.xcodeproj`.

## Security
- `certSha256` = SHA-256 of the DER leaf certificate, lowercase hex (docs/protocol.md). `PinningDelegate` accepts the server only if it matches; no CA validation (the PC cert is self-signed). An invalid/empty pin fails closed.
- Token + PC config stored as one Keychain item (`AfterFirstUnlockThisDeviceOnly`, so intents and background uploads work while locked).
- ATS: `NSAllowsLocalNetworking` only. The app never uses the Shortcuts HTTP port (47822).

## Plan B: free signing without App Groups (SPEC §11.6, R6)
Correctness never depends on the App Group:
1. `PairingStore.appGroupID` looks for a usable group: first the ids AltStore/SideStore write into `ALTAppGroups` (they rename groups when re-signing), then `group.com.arthur.phonebridge`. "Usable" = `containerURL(forSecurityApplicationGroupIdentifier:)` is non-nil.
2. The pairing is always saved in the target's own Keychain group, and additionally in the App Group's Keychain group when one is usable.
3. If the Share Extension finds no pairing (no shared group), it says so and offers a **Coller** button. In the app, *Réglages → Feuille de partage → Copier la configuration* puts a `phonebridge-config:` string on the clipboard (local-only, expires after 2 min). The extension stores its own copy. If the PC revokes the token, the user re-pairs the app (QR) and copies again.
4. Background uploads (share sheet can close during a 500 MB video) need a shared container, so they are only used when the App Group works. Otherwise uploads run in the foreground and the sheet must stay open.
5. Widgets and controls hold no config at all: their buttons run `OpenBridgeActionIntent` (`openAppWhenRun`), which opens the app; the app does the clipboard work in the foreground (iOS refuses clipboard access to background processes).

## App Intents (Shortcuts, Siri, Back Tap)
`SendClipboardToPCIntent` (optional `Texte`), `FetchClipboardFromPCIntent` (returns the text), `SendToPCIntent` (file / link / text), `SendPendingRepliesIntent` (returns `PendingReply` entities) + `MarkReplySentIntent` (acknowledge one reply after the Shortcut sent it). For Back Tap, the Shortcut passes "Obtenir le presse-papiers" into `Texte`, since the intent itself may not read the clipboard in the background.

## Known risks (not compiled locally)
- Code was written without a compiler; the first CI run is the real check.
- `openAppWhenRun` from interactive widget / control buttons: expected to launch the app and run the intent there. If iOS runs it in the widget process instead, the app opens but does nothing: fall back to `widgetURL`/`Link` with a URL scheme.
- Free accounts: app + 2 extensions = 3 App IDs; AltStore/SideStore may ask to drop extensions to fit the 3-active-app limit (keep the Share Extension).
