import AppIntents
import Foundation
import UIKit

// Actions for Shortcuts, Siri, Spotlight and Back Tap (via a Shortcut). They run in the app
// process without opening it. iOS may refuse clipboard access in the background, so the
// Shortcuts-friendly way is: "Presse-papiers" action -> pass it as `Texte` (docs: GuideView).

struct SendClipboardToPCIntent: AppIntent {
    static let title: LocalizedStringResource = "Envoyer le presse-papiers au PC"
    static let description = IntentDescription("Envoie un texte (ou, s'il est vide, le presse-papiers de l'iPhone) vers le presse-papiers du PC.")

    @Parameter(title: "Texte", description: "Laisser vide pour lire le presse-papiers de l'iPhone.")
    var text: String?

    func perform() async throws -> some IntentResult & ProvidesDialog {
        let post: ClipboardPost
        if let text, !text.isEmpty {
            post = .text(text)
        } else if let current = await ClipboardBridge.currentPost() {
            post = current
        } else {
            throw BridgeError.emptyClipboard
        }
        try await BridgeClient.paired().sendClipboard(post)
        return .result(dialog: "Envoyé au PC.")
    }
}

struct FetchClipboardFromPCIntent: AppIntent {
    static let title: LocalizedStringResource = "Récupérer le presse-papiers du PC"
    static let description = IntentDescription("Renvoie le dernier texte copié sur le PC et le place dans le presse-papiers de l'iPhone si iOS le permet.")

    func perform() async throws -> some IntentResult & ReturnsValue<String> & ProvidesDialog {
        guard let content = try await BridgeClient.paired().fetchClipboard() else {
            return .result(value: "", dialog: "Le presse-papiers du PC est vide.")
        }
        await ClipboardBridge.apply(content)
        return .result(value: content.text ?? "", dialog: "Presse-papiers du PC récupéré.")
    }
}

struct SendToPCIntent: AppIntent {
    static let title: LocalizedStringResource = "Envoyer au PC"
    static let description = IntentDescription("Envoie un fichier, un lien ou un texte au PC.")

    @Parameter(title: "Fichier")
    var file: IntentFile?

    @Parameter(title: "Lien")
    var url: URL?

    @Parameter(title: "Texte")
    var text: String?

    func perform() async throws -> some IntentResult & ProvidesDialog {
        let client = try BridgeClient.paired()
        var sent = false
        if let file {
            try await upload(file, with: client)
            sent = true
        }
        if let url {
            try await client.sendLink(url: url.absoluteString)
            sent = true
        }
        if let text, !text.isEmpty {
            try await client.sendClipboard(.text(text))
            sent = true
        }
        guard sent else { throw BridgeError.nothingToSend }
        return .result(dialog: "Envoyé au PC.")
    }

    private func upload(_ file: IntentFile, with client: BridgeClient) async throws {
        let name = file.filename.isEmpty ? "fichier" : file.filename
        if let source = file.fileURL {
            let scoped = source.startAccessingSecurityScopedResource()
            defer { if scoped { source.stopAccessingSecurityScopedResource() } }
            _ = try await client.uploadFile(at: source, filename: name)
            return
        }
        // No file URL: the data is already in memory, write it out for an upload-from-file.
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let temp = folder.appendingPathComponent(name)
        try file.data.write(to: temp)
        _ = try await client.uploadFile(at: temp, filename: name)
    }
}

/// A reply typed on the PC, waiting to be sent from the iPhone (SPEC §6 piste 3 "relais").
struct PendingReply: AppEntity {
    static let typeDisplayRepresentation: TypeDisplayRepresentation = "Réponse en attente"
    static let defaultQuery = PendingReplyQuery()

    let id: String

    @Property(title: "Destinataire")
    var to: String

    @Property(title: "Texte")
    var text: String

    var displayRepresentation: DisplayRepresentation {
        DisplayRepresentation(title: "\(to)", subtitle: "\(text)")
    }

    init(id: String, to: String, text: String) {
        self.id = id
        self.to = to
        self.text = text
    }

    static func fetchAll() async throws -> [PendingReply] {
        try await BridgeClient.paired().outbox()
            .filter { $0.kind == .reply }
            .compactMap { item in
                guard let to = item.to, let text = item.text else { return nil }
                return PendingReply(id: item.id, to: to, text: text)
            }
    }
}

struct PendingReplyQuery: EntityQuery {
    func entities(for identifiers: [String]) async throws -> [PendingReply] {
        try await PendingReply.fetchAll().filter { identifiers.contains($0.id) }
    }

    func suggestedEntities() async throws -> [PendingReply] {
        try await PendingReply.fetchAll()
    }
}

struct SendPendingRepliesIntent: AppIntent {
    static let title: LocalizedStringResource = "Envoyer les réponses du PC"
    static let description = IntentDescription("Renvoie les réponses écrites sur le PC. Dans le Raccourci : « Répéter avec chaque élément » → « Envoyer un message » (Destinataire, Texte) → « Marquer la réponse comme envoyée ».")

    func perform() async throws -> some IntentResult & ReturnsValue<[PendingReply]> & ProvidesDialog {
        let replies = try await PendingReply.fetchAll()
        if replies.isEmpty {
            return .result(value: replies, dialog: "Aucune réponse en attente.")
        }
        return .result(value: replies, dialog: "\(replies.count) réponse(s) à envoyer.")
    }
}

/// Acknowledges a reply only after the Shortcut actually sent it, so nothing is lost if sending fails.
struct MarkReplySentIntent: AppIntent {
    static let title: LocalizedStringResource = "Marquer la réponse comme envoyée"
    static let description = IntentDescription("Retire la réponse de la file d'attente du PC.")

    @Parameter(title: "Réponse")
    var reply: PendingReply

    func perform() async throws -> some IntentResult {
        try await BridgeClient.paired().deleteOutbox(id: reply.id)
        return .result()
    }
}

struct PhoneBridgeShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(intent: SendClipboardToPCIntent(),
                    phrases: ["Envoyer le presse-papiers au PC avec \(.applicationName)"],
                    shortTitle: "Presse-papiers → PC",
                    systemImageName: "arrow.up.doc")
        AppShortcut(intent: FetchClipboardFromPCIntent(),
                    phrases: ["Récupérer le presse-papiers du PC avec \(.applicationName)"],
                    shortTitle: "Presse-papiers du PC",
                    systemImageName: "arrow.down.doc")
        AppShortcut(intent: SendToPCIntent(),
                    phrases: ["Envoyer au PC avec \(.applicationName)"],
                    shortTitle: "Envoyer au PC",
                    systemImageName: "desktopcomputer")
        AppShortcut(intent: SendPendingRepliesIntent(),
                    phrases: ["Envoyer les réponses du PC avec \(.applicationName)"],
                    shortTitle: "Réponses du PC",
                    systemImageName: "bubble.left.and.text.bubble.right")
    }
}
