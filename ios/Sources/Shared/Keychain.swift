import Foundation
import Security

/// Minimal generic-password wrapper. `accessGroup == nil` means the target's own default group.
enum Keychain {
    static let service = "com.arthur.phonebridge"

    @discardableResult
    static func set(_ value: String, account: String, accessGroup: String?) -> Bool {
        var query = baseQuery(account: account, accessGroup: accessGroup)
        SecItemDelete(query as CFDictionary)
        query[kSecValueData as String] = Data(value.utf8)
        // AfterFirstUnlock: intents and background uploads run while the phone is locked.
        query[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        return SecItemAdd(query as CFDictionary, nil) == errSecSuccess
    }

    static func get(account: String, accessGroup: String?) -> String? {
        var query = baseQuery(account: account, accessGroup: accessGroup)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess,
              let data = result as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    /// Without an access group this deletes the item from every group the target can see.
    static func delete(account: String, accessGroup: String?) {
        SecItemDelete(baseQuery(account: account, accessGroup: accessGroup) as CFDictionary)
    }

    private static func baseQuery(account: String, accessGroup: String?) -> [String: Any] {
        var query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
        ]
        if let accessGroup { query[kSecAttrAccessGroup as String] = accessGroup }
        return query
    }
}
