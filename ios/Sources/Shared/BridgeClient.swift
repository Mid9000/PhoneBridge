import Foundation

enum BridgeError: LocalizedError, Equatable {
    case notPaired
    case invalidPairingCode
    case pinMismatch
    case unauthorized
    case unreachable
    case emptyClipboard
    case nothingToSend
    case incomplete
    case storage
    case badResponse
    case network(String)
    case server(status: Int, code: String?, message: String?)

    var errorDescription: String? {
        switch self {
        case .notPaired:
            return String(localized: "Aucun PC appairé. Ouvrez PhoneBridge et scannez le QR code affiché sur le PC.")
        case .invalidPairingCode:
            return String(localized: "Ce QR code n'est pas un code d'appairage PhoneBridge valide.")
        case .pinMismatch:
            return String(localized: "Le certificat du PC ne correspond pas à celui de l'appairage. Ré-appairez si vous avez réinstallé PhoneBridge sur le PC.")
        case .unauthorized:
            return String(localized: "Le PC a refusé cet iPhone (jeton révoqué ?). Ré-appairez.")
        case .unreachable:
            return String(localized: "PC injoignable. Vérifiez le Wi-Fi, que PhoneBridge tourne sur le PC et que l'accès au réseau local est autorisé.")
        case .emptyClipboard:
            return String(localized: "Le presse-papiers est vide.")
        case .nothingToSend:
            return String(localized: "Rien à envoyer.")
        case .incomplete:
            return String(localized: "Téléchargement incomplet, il reprendra à la prochaine tentative.")
        case .storage:
            return String(localized: "Impossible d'enregistrer l'appairage dans le trousseau.")
        case .badResponse:
            return String(localized: "Réponse inattendue du PC.")
        case .network(let detail):
            return detail
        case .server(let status, _, let message):
            switch status {
            case 410: return String(localized: "Code d'appairage expiré. Affichez un nouveau QR code sur le PC.")
            case 413: return String(localized: "Fichier trop volumineux pour le PC.")
            case 429: return String(localized: "Trop de tentatives. Réessayez dans une minute.")
            default: return message ?? String(localized: "Erreur du PC (\(status)).")
            }
        }
    }
}

/// HTTPS client for docs/protocol.md, pinned on the PC certificate.
final class BridgeClient {
    let config: ServerConfig
    let token: String?
    let session: URLSession
    private let pinning: PinningDelegate?

    /// `session` is for tests (stubbed); production sessions are always pinned.
    init(config: ServerConfig, token: String?, session: URLSession? = nil) {
        self.config = config
        self.token = token
        if let session {
            self.session = session
            pinning = nil
        } else {
            let delegate = PinningDelegate(pinnedSha256: config.certSha256)
            let cfg = URLSessionConfiguration.ephemeral
            cfg.timeoutIntervalForRequest = 20
            self.session = URLSession(configuration: cfg, delegate: delegate, delegateQueue: nil)
            pinning = delegate
        }
    }

    deinit {
        // The session retains its delegate; release both once this client is gone.
        if pinning != nil { session.finishTasksAndInvalidate() }
    }

    static func paired() throws -> BridgeClient {
        guard let pairing = PairingStore.load() else { throw BridgeError.notPaired }
        return BridgeClient(config: pairing.config, token: pairing.token)
    }

    /// Full pairing from a scanned QR code: pinned `POST /pair`, then the config to store.
    static func pair(payload: PairingPayload, deviceName: String, phoneId: String) async throws -> StoredPairing {
        var config = ServerConfig(host: payload.host, port: payload.port, certSha256: payload.certSha256,
                                  deviceId: payload.deviceId, name: payload.name, httpPort: nil)
        let client = BridgeClient(config: config, token: nil)
        let response = try await client.requestPairing(secret: payload.pairingSecret, deviceName: deviceName, phoneId: phoneId)
        config.httpPort = response.httpPort
        if let serverName = response.serverName, !serverName.isEmpty { config.name = serverName }
        return StoredPairing(config: config, token: response.token)
    }

    // MARK: Routes

    func requestPairing(secret: String, deviceName: String, phoneId: String) async throws -> PairResponse {
        let body = PairRequest(pairingSecret: secret, deviceName: deviceName, deviceId: phoneId)
        let (data, _) = try await send(makeRequest("/pair", method: "POST", json: body))
        return try decode(PairResponse.self, data)
    }

    func status() async throws -> StatusResponse {
        let (data, _) = try await send(makeRequest("/status"))
        return try decode(StatusResponse.self, data)
    }

    func outbox() async throws -> [OutboxItem] {
        let (data, _) = try await send(makeRequest("/outbox"))
        return try decode(OutboxList.self, data).items
    }

    /// JSON body of a non-file outbox item (used when the list entry has no inline text).
    func outboxClipboard(id: String) async throws -> ClipboardContent {
        let (data, _) = try await send(makeRequest("/outbox/\(Self.pathComponent(id))"))
        return try decode(ClipboardContent.self, data)
    }

    func deleteOutbox(id: String) async throws {
        try await send(makeRequest("/outbox/\(Self.pathComponent(id))", method: "DELETE"))
    }

    func sendLink(url: String, title: String? = nil) async throws {
        try await send(makeRequest("/links", method: "POST", json: LinkPost(url: url, title: title)))
    }

    func sendClipboard(_ content: ClipboardPost) async throws {
        try await send(makeRequest("/clipboard", method: "POST", json: content))
    }

    /// `nil` when the PC answers 204 (nothing copied yet).
    func fetchClipboard() async throws -> ClipboardContent? {
        let (data, http) = try await send(makeRequest("/clipboard"))
        if http.statusCode == 204 || data.isEmpty { return nil }
        return try decode(ClipboardContent.self, data)
    }

    func uploadFile(at fileURL: URL, filename: String, progress: @escaping (Double) -> Void = { _ in }) async throws -> FileUploadResponse {
        let request = try makeUploadRequest(fileURL: fileURL, filename: filename)
        let result: (Data, URLResponse)
        do {
            result = try await session.upload(for: request, fromFile: fileURL, delegate: UploadProgressDelegate(progress))
        } catch {
            throw mapTransport(error)
        }
        let (data, _) = try validate(result)
        return try decode(FileUploadResponse.self, data)
    }

    /// Resumable download: appends to `partial` with `Range: bytes=<size>-` when it already has data.
    func download(_ item: OutboxItem, to partial: URL, progress: @escaping (Double) -> Void = { _ in }) async throws {
        var request = try makeRequest("/outbox/\(Self.pathComponent(item.id))")
        let fm = FileManager.default
        try fm.createDirectory(at: partial.deletingLastPathComponent(), withIntermediateDirectories: true)
        if !fm.fileExists(atPath: partial.path) { fm.createFile(atPath: partial.path, contents: nil) }
        let handle = try FileHandle(forWritingTo: partial)
        let offset: UInt64
        do { offset = try handle.seekToEnd() } catch { try? handle.close(); throw error }
        if offset > 0 { request.setValue("bytes=\(offset)-", forHTTPHeaderField: "Range") }

        let result: (Int, Data)
        do {
            result = try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<(Int, Data), Error>) in
                let delegate = RangeDownloadDelegate(handle: handle, alreadyReceived: Int64(offset), onProgress: progress) {
                    continuation.resume(with: $0)
                }
                let task = session.dataTask(with: request)
                task.delegate = delegate
                task.resume()
            }
        } catch {
            throw mapTransport(error)
        }
        let (status, body) = result
        if status == 416 { try? fm.removeItem(at: partial) }
        guard status == 200 || status == 206 else { throw Self.serverError(status: status, data: body) }
        if let expected = item.size,
           let actual = (try? fm.attributesOfItem(atPath: partial.path)[.size] as? NSNumber)?.int64Value,
           actual != expected {
            throw BridgeError.incomplete
        }
    }

    func eventsTask() throws -> URLSessionWebSocketTask {
        var request = try makeRequest("/events")
        request.url = config.url("/events", scheme: "wss")
        return session.webSocketTask(with: request)
    }

    // MARK: Building blocks (internal for tests and the background uploader)

    func makeRequest(_ path: String, method: String = "GET", json: Encodable? = nil) throws -> URLRequest {
        guard let url = config.url(path) else { throw BridgeError.badResponse }
        var request = URLRequest(url: url)
        request.httpMethod = method
        if let token { request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization") }
        if let json {
            request.httpBody = try JSONEncoder().encode(json)
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        }
        return request
    }

    func makeUploadRequest(fileURL: URL, filename: String) throws -> URLRequest {
        let size = try fileURL.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0
        var request = try makeRequest("/files", method: "POST")
        request.setValue(Self.encodeFilename(filename), forHTTPHeaderField: "X-Filename")
        request.setValue(String(size), forHTTPHeaderField: "X-Size")
        request.setValue(try Pinning.sha256Hex(ofFile: fileURL), forHTTPHeaderField: "X-Sha256")
        request.setValue("application/octet-stream", forHTTPHeaderField: "Content-Type")
        return request
    }

    /// RFC 3986 unreserved characters only, so the header stays pure ASCII.
    static func encodeFilename(_ name: String) -> String {
        let unreserved = CharacterSet(charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~")
        return name.addingPercentEncoding(withAllowedCharacters: unreserved) ?? "fichier"
    }

    static func pathComponent(_ id: String) -> String {
        id.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed.subtracting(CharacterSet(charactersIn: "/"))) ?? id
    }

    static func serverError(status: Int, data: Data) -> BridgeError {
        if status == 401 { return .unauthorized }
        let body = try? JSONDecoder().decode(APIErrorBody.self, from: data)
        return .server(status: status, code: body?.error, message: body?.message)
    }

    // MARK: Private

    @discardableResult
    private func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        let result: (Data, URLResponse)
        do {
            result = try await session.data(for: request)
        } catch {
            throw mapTransport(error)
        }
        return try validate(result)
    }

    private func validate(_ result: (Data, URLResponse)) throws -> (Data, HTTPURLResponse) {
        guard let http = result.1 as? HTTPURLResponse else { throw BridgeError.badResponse }
        guard (200..<300).contains(http.statusCode) else { throw Self.serverError(status: http.statusCode, data: result.0) }
        return (result.0, http)
    }

    private func decode<T: Decodable>(_ type: T.Type, _ data: Data) throws -> T {
        do { return try JSONDecoder().decode(type, from: data) } catch { throw BridgeError.badResponse }
    }

    private func mapTransport(_ error: Error) -> Error {
        if pinning?.pinFailed == true { return BridgeError.pinMismatch }
        if error is BridgeError { return error }
        if let urlError = error as? URLError {
            switch urlError.code {
            case .cannotConnectToHost, .cannotFindHost, .timedOut, .networkConnectionLost,
                 .notConnectedToInternet, .dnsLookupFailed, .internationalRoamingOff, .dataNotAllowed:
                return BridgeError.unreachable
            default:
                return BridgeError.network(urlError.localizedDescription)
            }
        }
        return BridgeError.network(error.localizedDescription)
    }
}

/// Forwards upload progress (0...1) from the task-specific delegate.
final class UploadProgressDelegate: NSObject, URLSessionTaskDelegate {
    private let onProgress: (Double) -> Void

    init(_ onProgress: @escaping (Double) -> Void) {
        self.onProgress = onProgress
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didSendBodyData bytesSent: Int64,
                    totalBytesSent: Int64, totalBytesExpectedToSend: Int64) {
        guard totalBytesExpectedToSend > 0 else { return }
        onProgress(Double(totalBytesSent) / Double(totalBytesExpectedToSend))
    }
}

/// Streams a (possibly partial, 206) response body to a file handle.
final class RangeDownloadDelegate: NSObject, URLSessionDataDelegate {
    private let handle: FileHandle
    private var received: Int64
    private var expected: Int64 = -1
    private var lastReported = -1.0
    private var status = 0
    private var errorBody = Data()
    private var writeError: Error?
    private let onProgress: (Double) -> Void
    private let completion: (Result<(Int, Data), Error>) -> Void

    init(handle: FileHandle, alreadyReceived: Int64, onProgress: @escaping (Double) -> Void,
         completion: @escaping (Result<(Int, Data), Error>) -> Void) {
        self.handle = handle
        self.received = alreadyReceived
        self.onProgress = onProgress
        self.completion = completion
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive response: URLResponse,
                    completionHandler: @escaping (URLSession.ResponseDisposition) -> Void) {
        status = (response as? HTTPURLResponse)?.statusCode ?? 0
        if status == 200 {
            // Server ignored Range: start over.
            do {
                try handle.truncate(atOffset: 0)
                try handle.seek(toOffset: 0)
            } catch {
                writeError = error
                completionHandler(.cancel)
                return
            }
            received = 0
        }
        if status == 200 || status == 206 {
            let length = response.expectedContentLength
            expected = length >= 0 ? received + length : -1
        }
        completionHandler(.allow)
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) {
        guard status == 200 || status == 206 else {
            errorBody.append(data)
            return
        }
        guard writeError == nil else { return }
        do {
            try handle.write(contentsOf: data)
        } catch {
            writeError = error
            dataTask.cancel()
            return
        }
        received += Int64(data.count)
        if expected > 0 {
            let fraction = min(1, Double(received) / Double(expected))
            if fraction - lastReported >= 0.01 || fraction >= 1 {
                lastReported = fraction
                onProgress(fraction)
            }
        }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        try? handle.close()
        if let failure = writeError ?? error {
            completion(.failure(failure))
        } else {
            completion(.success((status, errorBody)))
        }
    }
}
