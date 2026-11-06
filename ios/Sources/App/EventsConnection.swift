import Foundation

/// `GET /v1/events` WebSocket while the app is in the foreground; reconnects every 5 s on failure.
@MainActor
final class EventsConnection {
    private var task: URLSessionWebSocketTask?
    private var client: BridgeClient?
    private var running = false
    private let onEvent: (ServerEvent) -> Void

    init(onEvent: @escaping (ServerEvent) -> Void) {
        self.onEvent = onEvent
    }

    func start(client: BridgeClient) {
        stop()
        self.client = client
        running = true
        connect()
    }

    func stop() {
        running = false
        task?.cancel(with: .goingAway, reason: nil)
        task = nil
    }

    private func connect() {
        guard running, let client, let task = try? client.eventsTask() else { return }
        self.task = task
        task.resume()
        receive(on: task)
    }

    private func receive(on task: URLSessionWebSocketTask) {
        task.receive { [weak self] result in
            Task { @MainActor in
                guard let self, self.task === task else { return }
                switch result {
                case .success(let message):
                    if let event = Self.decode(message) { self.onEvent(event) }
                    self.receive(on: task)
                case .failure:
                    self.task = nil
                    try? await Task.sleep(nanoseconds: 5_000_000_000)
                    if self.task == nil { self.connect() } // start() may have reconnected meanwhile
                }
            }
        }
    }

    private static func decode(_ message: URLSessionWebSocketTask.Message) -> ServerEvent? {
        let data: Data?
        switch message {
        case .string(let text): data = text.data(using: .utf8)
        case .data(let raw): data = raw
        @unknown default: data = nil
        }
        guard let data else { return nil }
        return try? JSONDecoder().decode(ServerEvent.self, from: data)
    }
}
