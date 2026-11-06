import Foundation

// Wire models for docs/protocol.md (v1). Field names must match the JSON exactly.

/// Content of the QR code shown by the PC.
struct PairingPayload: Codable, Equatable {
    var v: Int
    var host: String
    var port: Int
    var certSha256: String
    var pairingSecret: String
    var deviceId: String
    var name: String

    /// Parses and validates a scanned QR code (raw JSON text).
    static func parse(_ text: String) throws -> PairingPayload {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let data = trimmed.data(using: .utf8),
              let payload = try? JSONDecoder().decode(PairingPayload.self, from: data) else {
            throw BridgeError.invalidPairingCode
        }
        return try payload.validated()
    }

    /// Normalizes the fingerprint and rejects anything that cannot be a v1 pairing.
    func validated() throws -> PairingPayload {
        var copy = self
        copy.host = host.trimmingCharacters(in: .whitespacesAndNewlines)
        copy.certSha256 = Pinning.normalizeFingerprint(certSha256)
        copy.pairingSecret = pairingSecret.trimmingCharacters(in: .whitespacesAndNewlines)
        guard copy.v == 1,
              !copy.host.isEmpty,
              (1...65535).contains(copy.port),
              Pinning.isValidFingerprint(copy.certSha256),
              !copy.pairingSecret.isEmpty else {
            throw BridgeError.invalidPairingCode
        }
        return copy
    }
}

struct PairRequest: Codable, Equatable {
    var pairingSecret: String
    var deviceName: String
    var deviceId: String
}

struct PairResponse: Codable, Equatable {
    var token: String
    var serverName: String?
    var httpPort: Int?
}

/// What the phone remembers about the paired PC (the token is stored next to it, in the Keychain).
struct ServerConfig: Codable, Equatable {
    var host: String
    var port: Int
    var certSha256: String
    var deviceId: String
    var name: String
    var httpPort: Int?

    /// Builds `https://host:port/v1<path>`; IPv6 literals get brackets.
    func url(_ path: String, scheme: String = "https") -> URL? {
        let h = host.contains(":") && !host.hasPrefix("[") ? "[\(host)]" : host
        return URL(string: "\(scheme)://\(h):\(port)/v1\(path)")
    }
}

struct StoredPairing: Codable, Equatable {
    var config: ServerConfig
    var token: String
}

struct StatusResponse: Codable, Equatable {
    struct Features: Codable, Equatable {
        var shortcutsHttp: Bool?
        var bluetooth: String?
    }
    var name: String
    var version: String
    var features: Features?
    var time: String?
}

enum OutboxKind: String, Codable, Equatable {
    case file, clipboard, link, reply, unknown

    init(from decoder: Decoder) throws {
        let raw = try decoder.singleValueContainer().decode(String.self)
        self = OutboxKind(rawValue: raw) ?? .unknown
    }
}

struct OutboxItem: Codable, Identifiable, Equatable {
    var id: String
    var kind: OutboxKind
    var name: String?
    var size: Int64?
    var text: String?
    var url: String?
    var to: String?
    var createdAt: String?

    /// `sms:` URL that opens Messages with the reply pre-filled (SPEC §6 piste 3.3).
    var smsURL: URL? {
        guard kind == .reply, let to, let text else { return nil }
        let strict = CharacterSet(charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~")
        let recipient = to.filter { !$0.isWhitespace }
        guard let r = recipient.addingPercentEncoding(withAllowedCharacters: strict.union(CharacterSet(charactersIn: "+@"))),
              let body = text.addingPercentEncoding(withAllowedCharacters: strict) else { return nil }
        return URL(string: "sms:\(r)&body=\(body)")
    }
}

struct OutboxList: Codable, Equatable {
    var items: [OutboxItem]
}

struct ClipboardContent: Codable, Equatable {
    var kind: String
    var text: String?
    var imageBase64: String?
    var updatedAt: String?
}

struct ClipboardPost: Codable, Equatable {
    var kind: String
    var text: String?
    var imageBase64: String?

    static func text(_ value: String) -> ClipboardPost { ClipboardPost(kind: "text", text: value, imageBase64: nil) }
}

struct LinkPost: Codable, Equatable {
    var url: String
    var title: String?
}

struct FileUploadResponse: Codable, Equatable {
    var savedAs: String
    var size: Int64
    var sha256: String
}

struct ServerEvent: Codable, Equatable {
    var type: String
    var count: Int?
}

struct APIErrorBody: Codable, Equatable {
    var error: String
    var message: String?
}
