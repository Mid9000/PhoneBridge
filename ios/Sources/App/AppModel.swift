import SwiftUI
import UIKit
import UniformTypeIdentifiers
import UserNotifications

enum SettingsKey {
    static let autoApplyClipboard = "autoApplyClipboard"
    static let autoDownloadFiles = "autoDownloadFiles"
    static let autoSendClipboard = "autoSendClipboard"
    static let notifications = "notificationsEnabled"

    static let defaults: [String: Any] = [
        autoApplyClipboard: true,
        autoDownloadFiles: true,
        autoSendClipboard: false, // reading the clipboard can show iOS's "Allow Paste" prompt
        notifications: true,
    ]
}

enum ConnectionState: Equatable {
    case unknown
    case online
    case offline(String)
}

@MainActor
final class AppModel: ObservableObject {
    @Published private(set) var pairing: StoredPairing?
    @Published private(set) var status: StatusResponse?
    @Published private(set) var connection: ConnectionState = .unknown
    @Published private(set) var outbox: [OutboxItem] = []
    @Published private(set) var progress: [String: Double] = [:]
    @Published private(set) var receivedFiles: [URL] = []
    @Published private(set) var lastUpdate: Date?
    @Published var message: String?

    let discovery = Discovery()
    private var client: BridgeClient?
    private var events: EventsConnection?
    private var lastPasteboardChange = -1

    init() {
        UserDefaults.standard.register(defaults: SettingsKey.defaults)
        reloadPairing()
        reloadReceivedFiles()
        discovery.onFound = { [weak self] found in self?.discovered(found) }
    }

    // MARK: Pairing

    func pair(with payload: PairingPayload) async throws {
        let stored = try await BridgeClient.pair(payload: payload, deviceName: UIDevice.current.name,
                                                 phoneId: PairingStore.phoneDeviceId)
        try PairingStore.save(stored)
        reloadPairing()
        _ = try? await UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound])
        await onActive()
    }

    func unpair() {
        events?.stop()
        events = nil
        PairingStore.clear()
        reloadPairing()
        status = nil
        outbox = []
        connection = .unknown
    }

    /// Plan B (no App Group): hand the pairing to the Share Extension through a local-only,
    /// short-lived clipboard item. The PC already knows this token, so the only exposure is
    /// other apps reading the clipboard within 2 minutes (iOS shows a paste prompt for those).
    func copyConfigForExtensions() {
        guard let pairing, let text = PairingStore.exportString(pairing) else { return }
        UIPasteboard.general.setItems([[UTType.plainText.identifier: text]],
                                      options: [.localOnly: true, .expirationDate: Date().addingTimeInterval(120)])
        lastPasteboardChange = UIPasteboard.general.changeCount
        message = String(localized: "Configuration copiée (2 min). Partagez quelque chose vers « Envoyer au PC » et touchez Coller.")
    }

    // MARK: Lifecycle

    func scenePhaseChanged(_ phase: ScenePhase) {
        switch phase {
        case .active:
            Task { await onActive() }
        case .background:
            events?.stop()
            discovery.stop()
        default:
            break
        }
    }

    func onActive() async {
        guard pairing != nil else { return }
        consumePendingAction(requireActive: false)
        if UserDefaults.standard.bool(forKey: SettingsKey.autoSendClipboard) {
            await autoSendClipboard()
        }
        await refresh()
        connectEvents()
    }

    /// Runs the action requested by a widget / Control Center button once the app is in front.
    /// The notification path checks the state; the scene-phase path is already "active".
    func consumePendingAction(requireActive: Bool = true) {
        if requireActive && UIApplication.shared.applicationState != .active { return }
        guard let action = PendingAction.value else { return }
        PendingAction.value = nil
        Task {
            switch action {
            case .sendClipboard: await sendCurrentClipboard()
            case .fetchClipboard: await fetchClipboard()
            }
        }
    }

    func refresh() async {
        guard let client else { return }
        do {
            let status = try await client.status()
            let items = try await client.outbox()
            self.status = status
            outbox = items
            connection = .online
            lastUpdate = Date()
            await process(items)
        } catch {
            connection = .offline(error.localizedDescription)
            if (error as? BridgeError) == .unreachable { discovery.start() }
        }
    }

    // MARK: Clipboard

    func sendCurrentClipboard() async {
        guard let post = ClipboardBridge.currentPost() else {
            message = BridgeError.emptyClipboard.localizedDescription
            return
        }
        lastPasteboardChange = UIPasteboard.general.changeCount
        await sendClipboard(post)
    }

    func sendClipboard(_ post: ClipboardPost) async {
        await run(String(localized: "Envoyé au PC.")) { try await $0.sendClipboard(post) }
    }

    func fetchClipboard() async {
        await run(nil) { client in
            guard let content = try await client.fetchClipboard() else {
                self.message = String(localized: "Le presse-papiers du PC est vide.")
                return
            }
            self.apply(content)
        }
    }

    private func autoSendClipboard() async {
        let change = UIPasteboard.general.changeCount
        guard change != lastPasteboardChange else { return }
        lastPasteboardChange = change
        guard let post = ClipboardBridge.currentPost() else { return }
        await sendClipboard(post)
    }

    private func apply(_ content: ClipboardContent) {
        guard ClipboardBridge.apply(content) else { return }
        lastPasteboardChange = UIPasteboard.general.changeCount // do not echo it back to the PC
        message = String(localized: "Copié depuis le PC.")
    }

    // MARK: Outbox ("Reçu du PC")

    func download(_ item: OutboxItem) async {
        guard let client, item.kind == .file, progress[item.id] == nil else { return }
        progress[item.id] = 0
        // shortcut: no expiration handler, iOS ends the app if the time runs out; the .part file resumes next time.
        let backgroundTask = UIApplication.shared.beginBackgroundTask(withName: "download", expirationHandler: nil)
        defer {
            progress[item.id] = nil
            UIApplication.shared.endBackgroundTask(backgroundTask)
        }
        do {
            let partial = try Self.partialURL(for: item)
            try await client.download(item, to: partial) { [weak self] fraction in
                Task { @MainActor in
                    if self?.progress[item.id] != nil { self?.progress[item.id] = fraction }
                }
            }
            let destination = try Self.uniqueDestination(for: item)
            try FileManager.default.moveItem(at: partial, to: destination)
            try await client.deleteOutbox(id: item.id)
            outbox.removeAll { $0.id == item.id }
            reloadReceivedFiles()
            if UIApplication.shared.applicationState != .active {
                notify(String(localized: "Fichier reçu du PC"), destination.lastPathComponent)
            }
        } catch {
            message = error.localizedDescription
        }
    }

    func open(_ item: OutboxItem) async {
        switch item.kind {
        case .link:
            guard let string = item.url, let url = URL(string: string) else { return }
            _ = await UIApplication.shared.open(url)
            await acknowledge(item)
        case .reply:
            // Not acknowledged here: the user may not press Send in Messages.
            guard let url = item.smsURL else { return }
            _ = await UIApplication.shared.open(url)
        case .clipboard:
            await applyOutboxClipboard(item)
        case .file:
            await download(item)
        case .unknown:
            break
        }
    }

    func acknowledge(_ item: OutboxItem) async {
        await run(nil) { try await $0.deleteOutbox(id: item.id) }
        outbox.removeAll { $0.id == item.id }
    }

    func deleteReceivedFile(_ url: URL) {
        try? FileManager.default.removeItem(at: url)
        reloadReceivedFiles()
    }

    private func process(_ items: [OutboxItem]) async {
        let defaults = UserDefaults.standard
        for item in items {
            switch item.kind {
            case .clipboard where defaults.bool(forKey: SettingsKey.autoApplyClipboard):
                await applyOutboxClipboard(item)
            case .file where defaults.bool(forKey: SettingsKey.autoDownloadFiles) && progress[item.id] == nil:
                Task { await self.download(item) }
            default:
                break
            }
        }
    }

    private func applyOutboxClipboard(_ item: OutboxItem) async {
        guard let client else { return }
        do {
            let content: ClipboardContent
            if let text = item.text {
                content = ClipboardContent(kind: "text", text: text, imageBase64: nil, updatedAt: item.createdAt)
            } else {
                content = try await client.outboxClipboard(id: item.id)
            }
            apply(content)
            try await client.deleteOutbox(id: item.id)
            outbox.removeAll { $0.id == item.id }
        } catch {
            message = error.localizedDescription
        }
    }

    // MARK: Helpers

    private func run(_ success: String?, _ operation: (BridgeClient) async throws -> Void) async {
        guard let client else {
            message = BridgeError.notPaired.localizedDescription
            return
        }
        do {
            try await operation(client)
            if let success { message = success }
        } catch {
            message = error.localizedDescription
            if (error as? BridgeError) == .unreachable { discovery.start() }
        }
    }

    private func reloadPairing() {
        pairing = PairingStore.load()
        client = pairing.map { BridgeClient(config: $0.config, token: $0.token) }
    }

    /// The PC got a new IP: Bonjour found the same deviceId elsewhere, update and retry.
    private func discovered(_ found: Discovery.Found) {
        guard var stored = pairing, !stored.config.deviceId.isEmpty, found.id == stored.config.deviceId else { return }
        discovery.stop()
        guard found.host != stored.config.host || found.port != stored.config.port else { return }
        stored.config.host = found.host
        stored.config.port = found.port
        try? PairingStore.save(stored)
        reloadPairing()
        Task { await onActive() }
    }

    private func connectEvents() {
        guard let client else { return }
        if events == nil {
            events = EventsConnection { [weak self] _ in
                Task { await self?.refresh() }
            }
        }
        events?.start(client: client)
    }

    private func notify(_ title: String, _ body: String) {
        guard UserDefaults.standard.bool(forKey: SettingsKey.notifications) else { return }
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil))
    }

    // MARK: Files on disk

    /// Documents is exposed in the Files app (UIFileSharingEnabled), so received files show up there.
    static func receivedDirectory() throws -> URL {
        let url = try FileManager.default.url(for: .documentDirectory, in: .userDomainMask, appropriateFor: nil, create: true)
            .appendingPathComponent("Reçu du PC", isDirectory: true)
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    private static func partialURL(for item: OutboxItem) throws -> URL {
        try FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true)
            .appendingPathComponent("Partial", isDirectory: true)
            .appendingPathComponent(BridgeClient.pathComponent(item.id) + ".part")
    }

    private static func uniqueDestination(for item: OutboxItem) throws -> URL {
        let directory = try receivedDirectory()
        var name = URL(fileURLWithPath: item.name ?? item.id).lastPathComponent
        if ["", "/", ".", ".."].contains(name) { name = item.id }
        let base = (name as NSString).deletingPathExtension
        let ext = (name as NSString).pathExtension
        var candidate = directory.appendingPathComponent(name)
        var index = 1
        while FileManager.default.fileExists(atPath: candidate.path) {
            let numbered = ext.isEmpty ? "\(base) (\(index))" : "\(base) (\(index)).\(ext)"
            candidate = directory.appendingPathComponent(numbered)
            index += 1
        }
        return candidate
    }

    private func reloadReceivedFiles() {
        guard let directory = try? Self.receivedDirectory(),
              let urls = try? FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: [.contentModificationDateKey],
                                                                       options: [.skipsHiddenFiles]) else { return }
        func date(_ url: URL) -> Date {
            (try? url.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
        }
        receivedFiles = urls.sorted { date($0) > date($1) }
    }
}
