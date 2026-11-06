import CryptoKit
import Foundation
import Security

/// Certificate pinning: `certSha256` is the SHA-256 of the DER-encoded leaf certificate,
/// lowercase hex, 64 chars (docs/protocol.md). The PC certificate is self-signed, so the
/// pin replaces normal chain validation entirely.
enum Pinning {
    static func sha256Hex(_ data: Data) -> String {
        hex(SHA256.hash(data: data))
    }

    /// Streams the file in 1 MiB chunks so a 500 MB video does not land in memory.
    static func sha256Hex(ofFile url: URL) throws -> String {
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }
        var hasher = SHA256()
        while true {
            let more: Bool = try autoreleasepool {
                guard let chunk = try handle.read(upToCount: 1 << 20), !chunk.isEmpty else { return false }
                hasher.update(data: chunk)
                return true
            }
            if !more { break }
        }
        return hex(hasher.finalize())
    }

    /// Accepts `AB:CD:...` or spaced forms as shown on screens; validity is checked separately.
    static func normalizeFingerprint(_ value: String) -> String {
        value.lowercased().filter { $0 != ":" && !$0.isWhitespace }
    }

    static func isValidFingerprint(_ value: String) -> Bool {
        let digits: ClosedRange<Character> = "0"..."9"
        let letters: ClosedRange<Character> = "a"..."f"
        return value.count == 64 && value.allSatisfy { digits.contains($0) || letters.contains($0) }
    }

    static func leafCertificateSHA256(_ trust: SecTrust) -> String? {
        guard let chain = SecTrustCopyCertificateChain(trust) as? [SecCertificate],
              let leaf = chain.first else { return nil }
        return sha256Hex(SecCertificateCopyData(leaf) as Data)
    }

    private static func hex<D: Sequence>(_ digest: D) -> String where D.Element == UInt8 {
        digest.map { String(format: "%02x", $0) }.joined()
    }
}

/// Session delegate that only trusts the pinned PC certificate.
class PinningDelegate: NSObject, URLSessionTaskDelegate {
    let pinnedSha256: String
    private let lock = NSLock()
    private var failed = false

    /// True once a server presented a certificate that did not match the pin.
    var pinFailed: Bool {
        lock.lock(); defer { lock.unlock() }
        return failed
    }

    init(pinnedSha256: String) {
        self.pinnedSha256 = Pinning.normalizeFingerprint(pinnedSha256)
        super.init()
    }

    func urlSession(_ session: URLSession,
                    didReceive challenge: URLAuthenticationChallenge,
                    completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void) {
        guard challenge.protectionSpace.authenticationMethod == NSURLAuthenticationMethodServerTrust,
              let trust = challenge.protectionSpace.serverTrust else {
            completionHandler(.performDefaultHandling, nil)
            return
        }
        if Pinning.isValidFingerprint(pinnedSha256), Pinning.leafCertificateSHA256(trust) == pinnedSha256 {
            completionHandler(.useCredential, URLCredential(trust: trust))
        } else {
            lock.lock(); failed = true; lock.unlock()
            completionHandler(.cancelAuthenticationChallenge, nil)
        }
    }
}
