import XCTest

/// Network-free tests: requests go through a URLProtocol stub, never to a real host.
final class BridgeClientTests: XCTestCase {
    private let config = ServerConfig(host: "192.168.1.20", port: 47821, certSha256: String(repeating: "ab", count: 32),
                                      deviceId: "pc", name: "PC", httpPort: nil)
    private var client: BridgeClient!

    override func setUp() {
        super.setUp()
        StubURLProtocol.reset()
        let cfg = URLSessionConfiguration.ephemeral
        cfg.protocolClasses = [StubURLProtocol.self]
        client = BridgeClient(config: config, token: "tok", session: URLSession(configuration: cfg))
    }

    func testStatusSendsBearerTokenAndDecodes() async throws {
        StubURLProtocol.respond(200, json: #"{"name":"PC d'Arthur","version":"0.1.0","features":{"shortcutsHttp":true,"bluetooth":"off"},"time":"2026-11-06T10:00:00Z"}"#)
        let status = try await client.status()
        XCTAssertEqual(status.name, "PC d'Arthur")
        XCTAssertEqual(status.features?.bluetooth, "off")
        let request = try XCTUnwrap(StubURLProtocol.requests.first)
        XCTAssertEqual(request.url?.absoluteString, "https://192.168.1.20:47821/v1/status")
        XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer tok")
    }

    func testPairPostsSecretWithoutToken() async throws {
        let unpaired = BridgeClient(config: config, token: nil, session: stubSession())
        StubURLProtocol.respond(200, json: #"{"token":"newtoken","serverName":"PC d'Arthur","httpPort":47822}"#)
        let response = try await unpaired.requestPairing(secret: "s3cret", deviceName: "iPhone", phoneId: "phone-1")
        XCTAssertEqual(response, PairResponse(token: "newtoken", serverName: "PC d'Arthur", httpPort: 47822))
        let request = try XCTUnwrap(StubURLProtocol.requests.first)
        XCTAssertEqual(request.httpMethod, "POST")
        XCTAssertNil(request.value(forHTTPHeaderField: "Authorization"))
        let body = try JSONDecoder().decode(PairRequest.self, from: StubURLProtocol.bodies[0])
        XCTAssertEqual(body, PairRequest(pairingSecret: "s3cret", deviceName: "iPhone", deviceId: "phone-1"))
    }

    func testErrorsMapToBridgeErrors() async {
        StubURLProtocol.respond(401, json: #"{"error":"unauthorized","message":"bad token"}"#)
        await assertThrows(.unauthorized) { _ = try await self.client.status() }

        StubURLProtocol.respond(410, json: #"{"error":"expired","message":"Secret expired"}"#)
        await assertThrows(.server(status: 410, code: "expired", message: "Secret expired")) { _ = try await self.client.outbox() }
    }

    func testFetchClipboard204IsNil() async throws {
        StubURLProtocol.respond(204, json: "")
        let content = try await client.fetchClipboard()
        XCTAssertNil(content)
    }

    func testSendClipboardBody() async throws {
        StubURLProtocol.respond(204, json: "")
        try await client.sendClipboard(.text("bonjour"))
        let body = try JSONDecoder().decode(ClipboardPost.self, from: StubURLProtocol.bodies[0])
        XCTAssertEqual(body, ClipboardPost(kind: "text", text: "bonjour", imageBase64: nil))
        XCTAssertEqual(StubURLProtocol.requests[0].value(forHTTPHeaderField: "Content-Type"), "application/json")
    }

    func testUploadRequestHeaders() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try Data("abc".utf8).write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }
        let request = try client.makeUploadRequest(fileURL: url, filename: "été.txt")
        XCTAssertEqual(request.url?.path, "/v1/files")
        XCTAssertEqual(request.value(forHTTPHeaderField: "X-Filename"), "%C3%A9t%C3%A9.txt")
        XCTAssertEqual(request.value(forHTTPHeaderField: "X-Size"), "3")
        XCTAssertEqual(request.value(forHTTPHeaderField: "X-Sha256"), "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")
    }

    func testDownloadResumesWithRange() async throws {
        let partial = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".part")
        try Data("hello ".utf8).write(to: partial)
        defer { try? FileManager.default.removeItem(at: partial) }
        StubURLProtocol.respond(206, body: Data("world".utf8), headers: ["Content-Range": "bytes 6-10/11"])
        let item = OutboxItem(id: "f1", kind: .file, name: "a.txt", size: 11, text: nil, url: nil, to: nil, createdAt: nil)
        try await client.download(item, to: partial)
        XCTAssertEqual(StubURLProtocol.requests[0].value(forHTTPHeaderField: "Range"), "bytes=6-")
        XCTAssertEqual(try String(contentsOf: partial, encoding: .utf8), "hello world")
    }

    func testDownloadRestartsWhenServerIgnoresRange() async throws {
        let partial = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".part")
        try Data("stale".utf8).write(to: partial)
        defer { try? FileManager.default.removeItem(at: partial) }
        StubURLProtocol.respond(200, body: Data("fresh".utf8))
        let item = OutboxItem(id: "f1", kind: .file, name: "a.txt", size: 5, text: nil, url: nil, to: nil, createdAt: nil)
        try await client.download(item, to: partial)
        XCTAssertEqual(try String(contentsOf: partial, encoding: .utf8), "fresh")
    }

    // MARK: Helpers

    private func stubSession() -> URLSession {
        let cfg = URLSessionConfiguration.ephemeral
        cfg.protocolClasses = [StubURLProtocol.self]
        return URLSession(configuration: cfg)
    }

    private func assertThrows(_ expected: BridgeError, file: StaticString = #filePath, line: UInt = #line,
                              _ body: () async throws -> Void) async {
        do {
            try await body()
            XCTFail("expected \(expected)", file: file, line: line)
        } catch {
            XCTAssertEqual(error as? BridgeError, expected, file: file, line: line)
        }
    }
}

final class StubURLProtocol: URLProtocol {
    private static let lock = NSLock()
    private static var response: (status: Int, body: Data, headers: [String: String]) = (200, Data(), [:])
    private static var captured: [(URLRequest, Data)] = []

    static var requests: [URLRequest] { lock.lock(); defer { lock.unlock() }; return captured.map { $0.0 } }
    static var bodies: [Data] { lock.lock(); defer { lock.unlock() }; return captured.map { $0.1 } }

    static func reset() {
        lock.lock(); defer { lock.unlock() }
        captured = []
        response = (200, Data(), [:])
    }

    static func respond(_ status: Int, json: String) {
        respond(status, body: Data(json.utf8), headers: ["Content-Type": "application/json"])
    }

    static func respond(_ status: Int, body: Data, headers: [String: String] = [:]) {
        lock.lock(); defer { lock.unlock() }
        response = (status, body, headers)
    }

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        Self.lock.lock()
        Self.captured.append((request, Self.readBody(request)))
        let stub = Self.response
        Self.lock.unlock()

        guard let url = request.url,
              let http = HTTPURLResponse(url: url, statusCode: stub.status, httpVersion: "HTTP/1.1", headerFields: stub.headers) else {
            client?.urlProtocol(self, didFailWithError: URLError(.badURL))
            return
        }
        client?.urlProtocol(self, didReceive: http, cacheStoragePolicy: .notAllowed)
        if !stub.body.isEmpty { client?.urlProtocol(self, didLoad: stub.body) }
        client?.urlProtocolDidFinishLoading(self)
    }

    override func stopLoading() {}

    /// URLSession moves `httpBody` into `httpBodyStream` before it reaches a URLProtocol.
    private static func readBody(_ request: URLRequest) -> Data {
        if let body = request.httpBody { return body }
        guard let stream = request.httpBodyStream else { return Data() }
        stream.open()
        defer { stream.close() }
        var data = Data()
        var buffer = [UInt8](repeating: 0, count: 4096)
        while stream.hasBytesAvailable {
            let read = stream.read(&buffer, maxLength: buffer.count)
            if read <= 0 { break }
            data.append(buffer, count: read)
        }
        return data
    }
}
