import Foundation
import Network

/// Bonjour browser for `_phonebridge._tcp` (TXT: v, id, name). Resolves each service to an
/// IPv4 address by opening (and immediately closing) a TCP connection to it.
@MainActor
final class Discovery: ObservableObject {
    struct Found: Identifiable, Hashable {
        let id: String
        let name: String
        let host: String
        let port: Int
    }

    @Published private(set) var found: [Found] = []
    @Published private(set) var isSearching = false
    var onFound: (@MainActor (Found) -> Void)?

    private var browser: NWBrowser?
    private var connections: [NWConnection] = []
    private var generation = 0

    func start() {
        guard browser == nil else { return }
        found = []
        let browser = NWBrowser(for: .bonjourWithTXTRecord(type: "_phonebridge._tcp", domain: nil), using: .tcp)
        browser.browseResultsChangedHandler = { [weak self] results, _ in
            Task { @MainActor in self?.handle(results) }
        }
        browser.stateUpdateHandler = { [weak self] state in
            switch state {
            case .failed, .cancelled:
                Task { @MainActor in self?.isSearching = false }
            default:
                break
            }
        }
        browser.start(queue: .main)
        self.browser = browser
        isSearching = true
        generation += 1
        let current = generation
        // Searching forever drains battery; 15 s is plenty on a home network.
        Task { [weak self] in
            try? await Task.sleep(nanoseconds: 15_000_000_000)
            if self?.generation == current { self?.stop() }
        }
    }

    func stop() {
        browser?.cancel()
        browser = nil
        connections.forEach { $0.cancel() }
        connections = []
        isSearching = false
    }

    private func handle(_ results: Set<NWBrowser.Result>) {
        for result in results {
            guard case let .service(serviceName, _, _, _) = result.endpoint else { continue }
            var id = serviceName
            var name = serviceName
            if case let .bonjour(txt) = result.metadata {
                id = txt["id"] ?? id
                name = txt["name"] ?? name
            }
            guard !found.contains(where: { $0.id == id }) else { continue }
            resolve(result.endpoint, id: id, name: name)
        }
    }

    private func resolve(_ endpoint: NWEndpoint, id: String, name: String) {
        let parameters = NWParameters.tcp
        if let ip = parameters.defaultProtocolStack.internetProtocol as? NWProtocolIP.Options {
            ip.version = .v4
        }
        let connection = NWConnection(to: endpoint, using: parameters)
        connection.stateUpdateHandler = { [weak self, weak connection] state in
            switch state {
            case .ready:
                if let remote = connection?.currentPath?.remoteEndpoint,
                   case let .hostPort(host, port) = remote,
                   case let .ipv4(address) = host {
                    let ip = address.rawValue.map { String($0) }.joined(separator: ".")
                    let item = Found(id: id, name: name, host: ip, port: Int(port.rawValue))
                    Task { @MainActor in self?.add(item) }
                }
                connection?.cancel()
            case .failed, .waiting:
                connection?.cancel()
            default:
                break
            }
        }
        connection.start(queue: .main)
        connections.append(connection)
    }

    private func add(_ item: Found) {
        guard !found.contains(where: { $0.id == item.id }) else { return }
        found.append(item)
        onFound?(item)
    }
}
