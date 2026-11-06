import UIKit

/// Reads/writes the iPhone clipboard in the protocol's `ClipboardPost` / `ClipboardContent` shapes.
@MainActor
enum ClipboardBridge {
    static func currentPost() -> ClipboardPost? {
        let pasteboard = UIPasteboard.general
        if pasteboard.hasImages, let image = pasteboard.image, let png = image.pngData() {
            return ClipboardPost(kind: "image", text: nil, imageBase64: png.base64EncodedString())
        }
        if pasteboard.hasURLs, let url = pasteboard.url, !url.isFileURL {
            return ClipboardPost(kind: "url", text: url.absoluteString, imageBase64: nil)
        }
        if pasteboard.hasStrings, let text = pasteboard.string, !text.isEmpty,
           !text.hasPrefix(PairingStore.exportPrefix), // never ship the plan-B config or the token to the PC clipboard
           text.trimmingCharacters(in: .whitespacesAndNewlines) != PairingStore.load()?.token {
            return .text(text)
        }
        return nil
    }

    @discardableResult
    static func apply(_ content: ClipboardContent) -> Bool {
        let pasteboard = UIPasteboard.general
        if content.kind == "image", let base64 = content.imageBase64,
           let data = Data(base64Encoded: base64, options: .ignoreUnknownCharacters), let image = UIImage(data: data) {
            pasteboard.image = image
            return true
        }
        if content.kind == "url", let text = content.text, let url = URL(string: text) {
            pasteboard.url = url
            return true
        }
        if let text = content.text {
            pasteboard.string = text
            return true
        }
        return false
    }
}
