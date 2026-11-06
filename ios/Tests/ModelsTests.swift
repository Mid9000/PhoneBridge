import XCTest

final class ModelsTests: XCTestCase {
    private let fingerprint = String(repeating: "ab", count: 32)

    private func qr(v: Int = 1, port: Int = 47821, cert: String? = nil) -> String {
        """
        {"v":\(v),"host":"192.168.1.20","port":\(port),"certSha256":"\(cert ?? fingerprint)","pairingSecret":"c2VjcmV0","deviceId":"6f1c1e3a-0000-4000-8000-000000000001","name":"PC d'Arthur"}
        """
    }

    func testParsesProtocolQRCode() throws {
        let payload = try PairingPayload.parse(qr())
        XCTAssertEqual(payload.host, "192.168.1.20")
        XCTAssertEqual(payload.port, 47821)
        XCTAssertEqual(payload.certSha256, fingerprint)
        XCTAssertEqual(payload.name, "PC d'Arthur")
    }

    func testNormalizesColonSeparatedUppercaseFingerprint() throws {
        let shown = stride(from: 0, to: 64, by: 2).map { _ in "AB" }.joined(separator: ":")
        XCTAssertEqual(try PairingPayload.parse(qr(cert: shown)).certSha256, fingerprint)
    }

    func testRejectsInvalidQRCodes() {
        for bad in [qr(v: 2), qr(port: 0), qr(cert: "abcd"), qr(cert: String(repeating: "g", count: 64)), "not json", "{}"] {
            XCTAssertThrowsError(try PairingPayload.parse(bad)) { error in
                XCTAssertEqual(error as? BridgeError, .invalidPairingCode)
            }
        }
    }

    func testServerURLs() {
        var config = ServerConfig(host: "192.168.1.20", port: 47821, certSha256: fingerprint, deviceId: "id", name: "PC", httpPort: nil)
        XCTAssertEqual(config.url("/status")?.absoluteString, "https://192.168.1.20:47821/v1/status")
        XCTAssertEqual(config.url("/events", scheme: "wss")?.absoluteString, "wss://192.168.1.20:47821/v1/events")
        config.host = "fe80::1"
        XCTAssertEqual(config.url("/status")?.absoluteString, "https://[fe80::1]:47821/v1/status")
    }

    func testOutboxDecodingToleratesUnknownKindsAndMissingFields() throws {
        let json = """
        {"items":[
          {"id":"1","kind":"file","name":"photo.jpg","size":1234,"createdAt":"2026-11-06T10:00:00Z"},
          {"id":"2","kind":"reply","to":"+33 6 12 34 56 78","text":"J'arrive dans 5 min","createdAt":"2026-11-06T10:00:00Z"},
          {"id":"3","kind":"hologram"}
        ]}
        """
        let items = try JSONDecoder().decode(OutboxList.self, from: Data(json.utf8)).items
        XCTAssertEqual(items.map(\.kind), [.file, .reply, .unknown])
        XCTAssertEqual(items[0].size, 1234)
        XCTAssertNil(items[2].createdAt)
    }

    func testReplySMSURLEncodesRecipientAndBody() throws {
        let item = OutboxItem(id: "2", kind: .reply, name: nil, size: nil, text: "J'arrive & toi ?", url: nil, to: "+33 6 12 34 56 78", createdAt: nil)
        XCTAssertEqual(item.smsURL?.absoluteString, "sms:+33612345678&body=J%27arrive%20%26%20toi%20%3F")
        XCTAssertNil(OutboxItem(id: "1", kind: .file, name: "a", size: 1, text: nil, url: nil, to: nil, createdAt: nil).smsURL)
    }

    func testPlanBConfigExportRoundTrip() throws {
        let pairing = StoredPairing(config: ServerConfig(host: "10.0.0.2", port: 47821, certSha256: fingerprint,
                                                         deviceId: "id", name: "PC", httpPort: 47822),
                                    token: "dG9rZW4")
        let exported = try XCTUnwrap(PairingStore.exportString(pairing))
        XCTAssertTrue(exported.hasPrefix(PairingStore.exportPrefix))
        XCTAssertEqual(PairingStore.importString("  \(exported)\n"), pairing)
        XCTAssertNil(PairingStore.importString("hello"))
        XCTAssertNil(PairingStore.importString(PairingStore.exportPrefix + "!!!"))
    }

    func testFilenameHeaderIsASCIIPercentEncoded() {
        XCTAssertEqual(BridgeClient.encodeFilename("photo é (1).jpg"), "photo%20%C3%A9%20%281%29.jpg")
        XCTAssertEqual(BridgeClient.encodeFilename("a/b.txt"), "a%2Fb.txt")
    }
}
