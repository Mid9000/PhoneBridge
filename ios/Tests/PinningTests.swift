import XCTest

final class PinningTests: XCTestCase {
    func testSHA256KnownVector() {
        XCTAssertEqual(Pinning.sha256Hex(Data("abc".utf8)),
                       "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")
    }

    func testFileHashMatchesInMemoryHashAcrossChunks() throws {
        let data = Data((0..<(3 * 1024 * 1024 + 17)).map { UInt8($0 % 251) })
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try data.write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }
        XCTAssertEqual(try Pinning.sha256Hex(ofFile: url), Pinning.sha256Hex(data))
    }

    func testEmptyFileHash() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try Data().write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }
        XCTAssertEqual(try Pinning.sha256Hex(ofFile: url),
                       "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
    }

    func testFingerprintValidation() {
        XCTAssertTrue(Pinning.isValidFingerprint(String(repeating: "0f", count: 32)))
        XCTAssertFalse(Pinning.isValidFingerprint(String(repeating: "0F", count: 32)), "must be normalized first")
        XCTAssertFalse(Pinning.isValidFingerprint(String(repeating: "0", count: 63)))
        XCTAssertFalse(Pinning.isValidFingerprint(String(repeating: "０", count: 64)), "fullwidth digits")
        XCTAssertEqual(Pinning.normalizeFingerprint("AB:cd ef"), "abcdef")
    }

    func testDelegateWithInvalidPinNeverTrusts() {
        // An empty/garbage pin must fail closed (e.g. background session relaunch without a pairing).
        XCTAssertFalse(Pinning.isValidFingerprint(PinningDelegate(pinnedSha256: "").pinnedSha256))
    }
}
