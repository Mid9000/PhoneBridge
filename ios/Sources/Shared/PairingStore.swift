import Foundation

/// Where the pairing (PC config + token) lives. See ios/README.md, "Plan B".
///
/// 1. If an App Group is usable, the pairing is also written to a Keychain item in that
///    group so the Share Extension sees it.
/// 2. A copy is always kept in the target's own Keychain group, so free signing without
///    App Groups still works for the app; the extension then gets its own copy through
///    "Copier la configuration" (app) -> "Coller" (extension).
enum PairingStore {
    static let defaultAppGroup = "group.com.arthur.phonebridge"
    static let exportPrefix = "phonebridge-config:"
    private static let account = "pairing"
    private static let phoneIdKey = "phoneDeviceId"

    /// AltStore/SideStore rewrite App Group ids when re-signing and list the real ones
    /// under `ALTAppGroups` in Info.plist; fall back to the id from project.yml.
    static var appGroupID: String? {
        var candidates: [String] = []
        for bundle in [Bundle.main, containingAppBundle].compactMap({ $0 }) {
            candidates += bundle.object(forInfoDictionaryKey: "ALTAppGroups") as? [String] ?? []
        }
        candidates.append(defaultAppGroup)
        return candidates.first { FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: $0) != nil }
    }

    static var isShared: Bool { appGroupID != nil }

    static func load() -> StoredPairing? {
        if let group = appGroupID, let shared = read(accessGroup: group) { return shared }
        return read(accessGroup: nil)
    }

    static func save(_ pairing: StoredPairing) throws {
        let data = try JSONEncoder().encode(pairing)
        guard let json = String(data: data, encoding: .utf8) else { throw BridgeError.storage }
        // Local first: a group-less delete would also wipe the shared item.
        guard Keychain.set(json, account: account, accessGroup: nil) else { throw BridgeError.storage }
        if let group = appGroupID {
            Keychain.set(json, account: account, accessGroup: group)
        }
    }

    static func clear() {
        Keychain.delete(account: account, accessGroup: nil)
        if let group = appGroupID { Keychain.delete(account: account, accessGroup: group) }
    }

    /// Stable id of this phone, sent as `deviceId` in `POST /v1/pair`.
    static var phoneDeviceId: String {
        if let id = UserDefaults.standard.string(forKey: phoneIdKey) { return id }
        let id = UUID().uuidString
        UserDefaults.standard.set(id, forKey: phoneIdKey)
        return id
    }

    // MARK: Plan B transfer to the Share Extension

    static func exportString(_ pairing: StoredPairing) -> String? {
        guard let data = try? JSONEncoder().encode(pairing) else { return nil }
        return exportPrefix + data.base64EncodedString()
    }

    static func importString(_ value: String) -> StoredPairing? {
        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
        guard trimmed.hasPrefix(exportPrefix),
              let data = Data(base64Encoded: String(trimmed.dropFirst(exportPrefix.count))),
              var pairing = try? JSONDecoder().decode(StoredPairing.self, from: data) else { return nil }
        pairing.config.certSha256 = Pinning.normalizeFingerprint(pairing.config.certSha256)
        guard Pinning.isValidFingerprint(pairing.config.certSha256), !pairing.token.isEmpty else { return nil }
        return pairing
    }

    // MARK: Private

    private static var containingAppBundle: Bundle? {
        let url = Bundle.main.bundleURL
        guard url.pathExtension == "appex" else { return nil }
        return Bundle(url: url.deletingLastPathComponent().deletingLastPathComponent())
    }

    private static func read(accessGroup: String?) -> StoredPairing? {
        guard let json = Keychain.get(account: account, accessGroup: accessGroup),
              let data = json.data(using: .utf8) else { return nil }
        return try? JSONDecoder().decode(StoredPairing.self, from: data)
    }
}
