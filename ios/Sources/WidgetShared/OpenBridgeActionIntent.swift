import AppIntents
import Foundation

// Compiled into the app AND the widget extension. iOS only lets a foreground app touch the
// clipboard, so widget and Control Center buttons open the app, which then does the work.

enum BridgeAction: String, AppEnum, CaseIterable {
    case sendClipboard
    case fetchClipboard

    static let typeDisplayRepresentation: TypeDisplayRepresentation = "Action PhoneBridge"
    static let caseDisplayRepresentations: [BridgeAction: DisplayRepresentation] = [
        .sendClipboard: "Envoyer le presse-papiers au PC",
        .fetchClipboard: "Récupérer le presse-papiers du PC",
    ]
}

/// Hand-off from an intent run to the app UI (same process when `openAppWhenRun` is true).
enum PendingAction {
    static let notification = Notification.Name("PhoneBridge.pendingAction")
    @MainActor static var value: BridgeAction?
}

struct OpenBridgeActionIntent: AppIntent {
    static let title: LocalizedStringResource = "Action PhoneBridge"
    static let openAppWhenRun = true
    static let isDiscoverable = false

    @Parameter(title: "Action")
    var action: BridgeAction

    init() {}

    init(action: BridgeAction) {
        self.action = action
    }

    @MainActor
    func perform() async throws -> some IntentResult {
        PendingAction.value = action
        NotificationCenter.default.post(name: PendingAction.notification, object: nil)
        return .result()
    }
}
