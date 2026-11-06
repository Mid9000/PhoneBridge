import SwiftUI
import UIKit
import UniformTypeIdentifiers

/// Principal class of the Share Extension (NSExtensionPrincipalClass = ShareViewController).
@objc(ShareViewController)
final class ShareViewController: UIViewController {
    private var model: ShareModel?

    override func viewDidLoad() {
        super.viewDidLoad()
        let model = ShareModel(context: extensionContext)
        self.model = model
        let host = UIHostingController(rootView: ShareView(model: model))
        addChild(host)
        host.view.frame = view.bounds
        host.view.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        view.addSubview(host.view)
        host.didMove(toParent: self)
        model.start()
    }
}

@MainActor
final class ShareModel: ObservableObject {
    enum Phase: Equatable {
        case preparing
        case needsPairing
        case sending
        case background(Int)
        case done(String)
        case failed(String)
    }

    @Published var phase: Phase = .preparing
    @Published var progress: Double = 0

    private weak var context: NSExtensionContext?
    private var totalBytes: Int64 = 0
    private var doneBytes: Int64 = 0
    private var backgroundFinished = 0
    private var backgroundFailed = 0

    init(context: NSExtensionContext?) {
        self.context = context
    }

    func start() {
        Task { await run() }
    }

    func close() {
        context?.completeRequest(returningItems: nil, completionHandler: nil)
    }

    func cancel() {
        context?.cancelRequest(withError: NSError(domain: NSCocoaErrorDomain, code: NSUserCancelledError))
    }

    /// Plan B: the config copied from the app (Réglages → Feuille de partage).
    func importConfig(_ strings: [String]) {
        guard let text = strings.first, let pairing = PairingStore.importString(text) else {
            phase = .failed(String(localized: "Ce n'est pas une configuration PhoneBridge. Copiez-la depuis l'app : Réglages → Feuille de partage."))
            return
        }
        do {
            try PairingStore.save(pairing)
            start()
        } catch {
            phase = .failed(error.localizedDescription)
        }
    }

    private func run() async {
        guard let pairing = PairingStore.load() else {
            phase = .needsPairing
            return
        }
        phase = .preparing
        let client = BridgeClient(config: pairing.config, token: pairing.token)
        let providers = (context?.inputItems as? [NSExtensionItem] ?? []).flatMap { $0.attachments ?? [] }

        // Background uploads need the files inside the App Group container.
        let group = PairingStore.appGroupID
        let uploadsDir = group.flatMap { BackgroundUploader.uploadsDirectory(groupID: $0) }
        let workDir = uploadsDir ?? FileManager.default.temporaryDirectory.appendingPathComponent("Share", isDirectory: true)

        let items: [SharedItem]
        do {
            items = try await SharedItem.load(providers, into: workDir)
        } catch {
            phase = .failed(error.localizedDescription)
            return
        }
        guard !items.isEmpty else {
            phase = .failed(BridgeError.nothingToSend.localizedDescription)
            return
        }

        phase = .sending
        totalBytes = max(1, items.reduce(0) { $0 + $1.byteCount })
        let hasLink = items.contains { if case .link = $0 { return true } else { return false } }
        var uploader: BackgroundUploader?
        var queued = 0
        var lastError: String?

        for item in items {
            do {
                switch item {
                case .link(let url):
                    try await client.sendLink(url: url.absoluteString)
                case .text(let text):
                    if hasLink { continue } // Safari adds the page title as text next to the URL
                    try await client.sendClipboard(.text(text))
                case .file(let url, let size):
                    let request = try client.makeUploadRequest(fileURL: url, filename: url.lastPathComponent)
                    if let group, uploadsDir != nil {
                        if uploader == nil {
                            uploader = BackgroundUploader.shared(pinned: pairing.config.certSha256, groupID: group)
                            observe(uploader)
                        }
                        uploader?.enqueue(request, file: url)
                        queued += 1
                    } else {
                        let base = doneBytes
                        _ = try await client.uploadFile(at: url, filename: url.lastPathComponent) { [weak self] fraction in
                            Task { @MainActor in self?.setProgress(base + Int64(fraction * Double(size))) }
                        }
                        try? FileManager.default.removeItem(at: url.deletingLastPathComponent())
                        doneBytes += size
                        setProgress(doneBytes)
                    }
                }
            } catch {
                lastError = error.localizedDescription
            }
        }

        if queued > 0 {
            phase = .background(queued)
            checkBackground() // uploads may already be done
            return
        }
        finish(error: lastError)
    }

    private func observe(_ uploader: BackgroundUploader?) {
        let base = doneBytes
        uploader?.onUpdate = { [weak self] sent, finished, failed in
            Task { @MainActor in
                guard let self else { return }
                self.setProgress(base + sent)
                self.backgroundFinished = finished
                self.backgroundFailed = failed
                self.checkBackground()
            }
        }
    }

    private func checkBackground() {
        guard case .background(let queued) = phase, backgroundFinished + backgroundFailed >= queued else { return }
        let failed = backgroundFailed
        finish(error: failed > 0 ? String(localized: "\(failed) fichier(s) non envoyé(s).") : nil)
    }

    private func setProgress(_ bytes: Int64) {
        progress = min(1, Double(bytes) / Double(totalBytes))
    }

    private func finish(error: String?) {
        if let error {
            phase = .failed(error)
            return
        }
        progress = 1
        phase = .done(String(localized: "Envoyé au PC."))
        Task {
            try? await Task.sleep(nanoseconds: 1_200_000_000)
            close()
        }
    }
}

/// One shared attachment, materialized for upload.
enum SharedItem {
    case file(URL, Int64)
    case link(URL)
    case text(String)

    var byteCount: Int64 {
        if case .file(_, let size) = self { return size }
        return 0
    }

    static func load(_ providers: [NSItemProvider], into directory: URL) async throws -> [SharedItem] {
        var items: [SharedItem] = []
        for provider in providers {
            // One unreadable attachment should not block the other 49 photos.
            if let item = try? await load(provider, into: directory) { items.append(item) }
        }
        return items
    }

    private static func load(_ provider: NSItemProvider, into directory: URL) async throws -> SharedItem? {
        let types = provider.registeredTypeIdentifiers.compactMap { UTType($0) }

        // 1. Real file content (photos, videos, PDFs...): copy the file representation.
        if let fileType = types.first(where: { $0.conforms(to: .data) && !$0.conforms(to: .text) && !$0.conforms(to: .url) }) {
            if let item = try? await loadFileRepresentation(provider, type: fileType, into: directory) {
                return item
            }
            let value = try await provider.loadItem(forTypeIdentifier: fileType.identifier)
            return try materialize(value, suggestedName: provider.suggestedName, type: fileType, into: directory)
        }
        // 2. URL: a file URL (Files app) is a file, anything else is a link to open on the PC.
        if let urlType = types.first(where: { $0.conforms(to: .fileURL) }) ?? types.first(where: { $0.conforms(to: .url) }) {
            let value = try await provider.loadItem(forTypeIdentifier: urlType.identifier)
            return try materialize(value, suggestedName: provider.suggestedName, type: nil, into: directory)
        }
        // 3. Plain text -> PC clipboard.
        if let textType = types.first(where: { $0.conforms(to: .text) }) {
            let value = try await provider.loadItem(forTypeIdentifier: textType.identifier)
            return try materialize(value, suggestedName: provider.suggestedName, type: nil, into: directory)
        }
        return nil
    }

    private static func materialize(_ value: NSSecureCoding, suggestedName: String?, type: UTType?,
                                    into directory: URL) throws -> SharedItem? {
        if let url = value as? URL {
            if url.isFileURL { return try copy(url, into: directory) }
            return .link(url)
        }
        if let text = value as? String {
            if let url = URL(string: text), let scheme = url.scheme?.lowercased(), scheme == "http" || scheme == "https" {
                return .link(url)
            }
            return text.isEmpty ? nil : .text(text)
        }
        if let image = value as? UIImage, let data = image.pngData() {
            return try write(data, name: (suggestedName ?? "image") + ".png", into: directory)
        }
        if let data = value as? Data {
            if type == nil, let text = String(data: data, encoding: .utf8) { return .text(text) }
            let ext = type?.preferredFilenameExtension.map { "." + $0 } ?? ""
            return try write(data, name: (suggestedName ?? "fichier") + ext, into: directory)
        }
        return nil
    }

    private static func loadFileRepresentation(_ provider: NSItemProvider, type: UTType, into directory: URL) async throws -> SharedItem {
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<SharedItem, Error>) in
            _ = provider.loadFileRepresentation(forTypeIdentifier: type.identifier) { url, error in
                // The temporary file is deleted when this handler returns: copy it now.
                guard let url else {
                    continuation.resume(throwing: error ?? BridgeError.nothingToSend)
                    return
                }
                continuation.resume(with: Result { try copy(url, into: directory) })
            }
        }
    }

    /// Copies into a fresh UUID folder (keeps the original name). Folders are zipped by
    /// NSFileCoordinator's `.forUploading`, so "share a folder" sends a .zip.
    private static func copy(_ source: URL, into directory: URL) throws -> SharedItem {
        let scoped = source.startAccessingSecurityScopedResource()
        defer { if scoped { source.stopAccessingSecurityScopedResource() } }
        var coordinatorError: NSError?
        var result: Result<SharedItem, Error> = .failure(BridgeError.nothingToSend)
        NSFileCoordinator().coordinate(readingItemAt: source, options: .forUploading, error: &coordinatorError) { readable in
            result = Result {
                let folder = directory.appendingPathComponent(UUID().uuidString, isDirectory: true)
                try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
                let destination = folder.appendingPathComponent(readable.lastPathComponent)
                try FileManager.default.copyItem(at: readable, to: destination)
                let size = (try? destination.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0
                return .file(destination, Int64(size))
            }
        }
        if let coordinatorError { throw coordinatorError }
        return try result.get()
    }

    private static func write(_ data: Data, name: String, into directory: URL) throws -> SharedItem {
        let folder = directory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let safeName = URL(fileURLWithPath: name).lastPathComponent
        let destination = folder.appendingPathComponent(safeName.isEmpty ? "fichier" : safeName)
        try data.write(to: destination)
        return .file(destination, Int64(data.count))
    }
}

struct ShareView: View {
    @ObservedObject var model: ShareModel

    var body: some View {
        NavigationStack {
            VStack(spacing: 18) {
                switch model.phase {
                case .preparing:
                    ProgressView("Préparation…")
                case .needsPairing:
                    Image(systemName: "link.badge.plus").font(.system(size: 40)).foregroundStyle(.tint)
                    Text("Cette extension n'a pas encore l'appairage.")
                        .font(.headline)
                    Text("Ouvrez PhoneBridge → Réglages → Feuille de partage → « Copier la configuration », puis revenez ici et touchez Coller. Si l'app n'est pas appairée, scannez d'abord le QR code du PC dans l'app.")
                        .multilineTextAlignment(.center)
                        .foregroundStyle(.secondary)
                    PasteButton(payloadType: String.self) { strings in
                        Task { @MainActor in model.importConfig(strings) }
                    }
                case .sending:
                    ProgressView(value: model.progress)
                    Text("Envoi au PC…").foregroundStyle(.secondary)
                case .background(let count):
                    ProgressView(value: model.progress)
                    Text("\(count) fichier(s) en cours d'envoi. Vous pouvez fermer : l'envoi continue en arrière-plan.")
                        .multilineTextAlignment(.center)
                        .foregroundStyle(.secondary)
                case .done(let text):
                    Image(systemName: "checkmark.circle.fill").font(.system(size: 44)).foregroundStyle(.green)
                    Text(text).font(.headline)
                case .failed(let text):
                    Image(systemName: "exclamationmark.triangle.fill").font(.system(size: 40)).foregroundStyle(.orange)
                    Text(text).multilineTextAlignment(.center)
                }
            }
            .padding(24)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .navigationTitle("Envoyer au PC")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button(model.phase == .sending ? LocalizedStringKey("Annuler") : LocalizedStringKey("Fermer")) {
                        if model.phase == .sending { model.cancel() } else { model.close() }
                    }
                }
            }
        }
    }
}
