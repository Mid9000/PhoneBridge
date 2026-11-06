import Foundation

/// Background `URLSession` used by the Share Extension for files when an App Group exists
/// (iOS requires a shared container for extension background sessions). Uploads keep going
/// after the share sheet closes; the app finishes the session events if iOS wakes it.
final class BackgroundUploader: PinningDelegate, URLSessionDataDelegate {
    static let identifier = "com.arthur.phonebridge.background-upload"

    /// Sent bytes (all running tasks), finished count, failed count. Called on the delegate queue.
    var onUpdate: ((Int64, Int, Int) -> Void)?
    /// Completion handler handed over by `application(_:handleEventsForBackgroundURLSession:)`.
    var systemCompletion: (() -> Void)?

    private let groupID: String
    private var sent: [Int: Int64] = [:]
    private var finished = 0
    private var failed = 0

    private lazy var session: URLSession = {
        let cfg = URLSessionConfiguration.background(withIdentifier: Self.identifier)
        cfg.sharedContainerIdentifier = self.groupID
        cfg.sessionSendsLaunchEvents = true
        cfg.isDiscretionary = false
        return URLSession(configuration: cfg, delegate: self, delegateQueue: nil)
    }()

    private static let lock = NSLock()
    private static var instance: BackgroundUploader?

    private init(pinned: String, groupID: String) {
        self.groupID = groupID
        super.init(pinnedSha256: pinned)
    }

    /// One session per identifier per process, as URLSession requires.
    static func shared(pinned: String, groupID: String) -> BackgroundUploader {
        lock.lock(); defer { lock.unlock() }
        if let instance { return instance }
        let uploader = BackgroundUploader(pinned: pinned, groupID: groupID)
        _ = uploader.session
        instance = uploader
        return uploader
    }

    static func uploadsDirectory(groupID: String) -> URL? {
        FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: groupID)?
            .appendingPathComponent("Uploads", isDirectory: true)
    }

    /// Called by the app when iOS relaunches it for this session.
    static func handleEvents(identifier: String, completion: @escaping () -> Void) {
        guard identifier == Self.identifier, let group = PairingStore.appGroupID else {
            completion()
            return
        }
        let pin = PairingStore.load()?.config.certSha256 ?? ""
        shared(pinned: pin, groupID: group).systemCompletion = completion
    }

    /// `file` must live inside the App Group container; it is deleted once the task ends.
    func enqueue(_ request: URLRequest, file: URL) {
        let task = session.uploadTask(with: request, fromFile: file)
        task.taskDescription = file.path
        task.resume()
    }

    // MARK: URLSession delegate

    func urlSession(_ session: URLSession, task: URLSessionTask, didSendBodyData bytesSent: Int64,
                    totalBytesSent: Int64, totalBytesExpectedToSend: Int64) {
        sent[task.taskIdentifier] = totalBytesSent
        report()
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        let status = (task.response as? HTTPURLResponse)?.statusCode ?? 0
        if error == nil, (200..<300).contains(status) { finished += 1 } else { failed += 1 }
        if let path = task.taskDescription {
            // Each shared file sits in its own UUID folder: remove the folder.
            try? FileManager.default.removeItem(at: URL(fileURLWithPath: path).deletingLastPathComponent())
        }
        report()
    }

    func urlSessionDidFinishEvents(forBackgroundURLSession session: URLSession) {
        DispatchQueue.main.async {
            self.systemCompletion?()
            self.systemCompletion = nil
        }
    }

    private func report() {
        onUpdate?(sent.values.reduce(0, +), finished, failed)
    }
}
