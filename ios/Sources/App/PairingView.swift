import AVFoundation
import SwiftUI
import UIKit

struct PairingView: View {
    @EnvironmentObject private var model: AppModel
    @Environment(\.dismiss) private var dismiss
    @State private var showManual = false
    @State private var isPairing = false
    @State private var error: String?
    /// True when presented from Settings (re-pairing) rather than as the first screen.
    let isSheet: Bool

    init(isSheet: Bool = false) {
        self.isSheet = isSheet
    }

    var body: some View {
        ScrollView {
            VStack(spacing: 20) {
                Text("Sur le PC, ouvrez PhoneBridge → iPhone → Appairer, puis scannez le QR code.")
                    .multilineTextAlignment(.center)
                    .foregroundStyle(.secondary)

                QRScannerView { code in pair(code) }
                    .frame(height: 320)
                    .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
                    .overlay {
                        if isPairing {
                            ProgressView().controlSize(.large)
                        }
                    }
                    .accessibilityLabel(Text("Scanner de QR code"))

                if let error {
                    Label(error, systemImage: "exclamationmark.triangle")
                        .foregroundStyle(.red)
                        .multilineTextAlignment(.leading)
                }

                Button("Saisir les informations à la main") { showManual = true }
                    .buttonStyle(.bordered)
            }
            .padding()
        }
        .navigationTitle("Appairer avec le PC")
        .sheet(isPresented: $showManual) {
            NavigationStack {
                ManualPairingView { payload in
                    showManual = false
                    pair(payload)
                }
            }
            .environmentObject(model)
        }
    }

    private func pair(_ code: String) {
        do {
            let payload = try PairingPayload.parse(code)
            pair(payload)
        } catch {
            self.error = error.localizedDescription
        }
    }

    private func pair(_ payload: PairingPayload) {
        guard !isPairing else { return }
        isPairing = true
        error = nil
        Task {
            do {
                try await model.pair(with: payload)
                if isSheet { dismiss() }
            } catch {
                self.error = error.localizedDescription
            }
            isPairing = false
        }
    }
}

struct ManualPairingView: View {
    @EnvironmentObject private var model: AppModel
    @Environment(\.dismiss) private var dismiss
    let onSubmit: (PairingPayload) -> Void

    init(onSubmit: @escaping (PairingPayload) -> Void) {
        self.onSubmit = onSubmit
    }

    @State private var host = ""
    @State private var port = "47821"
    @State private var fingerprint = ""
    @State private var secret = ""
    @State private var deviceId = ""
    @State private var name = ""
    @State private var error: String?

    var body: some View {
        Form {
            DiscoveredPCsSection(discovery: model.discovery) { pc in
                host = pc.host
                port = String(pc.port)
                deviceId = pc.id
                name = pc.name
            }

            Section {
                TextField("Adresse IP (ex. 192.168.1.20)", text: $host)
                    .keyboardType(.numbersAndPunctuation)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                TextField("Port", text: $port)
                    .keyboardType(.numberPad)
                TextField("Empreinte du certificat (64 caractères)", text: $fingerprint, axis: .vertical)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                    .font(.system(.body, design: .monospaced))
                TextField("Code d'appairage", text: $secret)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
            } header: {
                Text("Informations affichées sous le QR code")
            } footer: {
                if let error { Text(error).foregroundStyle(.red) }
            }
        }
        .navigationTitle("Saisie manuelle")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .cancellationAction) {
                Button("Annuler") { dismiss() }
            }
            ToolbarItem(placement: .confirmationAction) {
                Button("Appairer") { submit() }
            }
        }
        .onAppear { model.discovery.start() }
    }

    private func submit() {
        let payload = PairingPayload(v: 1, host: host, port: Int(port) ?? 0, certSha256: fingerprint,
                                     pairingSecret: secret, deviceId: deviceId,
                                     name: name.isEmpty ? host : name)
        do {
            let valid = try payload.validated()
            onSubmit(valid)
        } catch {
            self.error = error.localizedDescription
        }
    }
}

/// Bonjour results; observes `Discovery` directly so the list updates live.
struct DiscoveredPCsSection: View {
    @ObservedObject var discovery: Discovery
    let onPick: (Discovery.Found) -> Void

    var body: some View {
        Section {
            ForEach(discovery.found) { pc in
                Button {
                    onPick(pc)
                } label: {
                    LabeledContent(pc.name, value: pc.host)
                }
            }
            if discovery.isSearching {
                ProgressView()
            } else {
                Button("Rechercher à nouveau") { discovery.start() }
            }
        } header: {
            Text("PC trouvés sur le réseau")
        }
    }
}

/// AVFoundation QR scanner. Shows a message instead when the camera is unavailable or denied.
struct QRScannerView: UIViewControllerRepresentable {
    let onCode: (String) -> Void

    func makeUIViewController(context: Context) -> ScannerViewController {
        let controller = ScannerViewController()
        controller.onCode = onCode
        return controller
    }

    func updateUIViewController(_ controller: ScannerViewController, context: Context) {
        controller.onCode = onCode
    }
}

final class ScannerViewController: UIViewController, AVCaptureMetadataOutputObjectsDelegate {
    var onCode: ((String) -> Void)?
    private let session = AVCaptureSession()
    private var previewLayer: AVCaptureVideoPreviewLayer?
    private var lastCode: String?
    private var lastCodeDate = Date.distantPast
    private var configured = false

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .black
        switch AVCaptureDevice.authorizationStatus(for: .video) {
        case .authorized:
            configure()
        case .notDetermined:
            AVCaptureDevice.requestAccess(for: .video) { granted in
                DispatchQueue.main.async {
                    if granted { self.configure() } else { self.showMessage(String(localized: "Accès à la caméra refusé. Autorisez-le dans Réglages → PhoneBridge, ou saisissez les informations à la main.")) }
                }
            }
        default:
            showMessage(String(localized: "Accès à la caméra refusé. Autorisez-le dans Réglages → PhoneBridge, ou saisissez les informations à la main."))
        }
    }

    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        previewLayer?.frame = view.bounds
    }

    override func viewWillAppear(_ animated: Bool) {
        super.viewWillAppear(animated)
        startRunning()
    }

    override func viewWillDisappear(_ animated: Bool) {
        super.viewWillDisappear(animated)
        let captureSession = self.session
        if captureSession.isRunning {
            DispatchQueue.global(qos: .userInitiated).async { captureSession.stopRunning() }
        }
    }

    private func configure() {
        guard let device = AVCaptureDevice.default(for: .video),
              let input = try? AVCaptureDeviceInput(device: device),
              session.canAddInput(input) else {
            showMessage(String(localized: "Caméra indisponible. Saisissez les informations à la main."))
            return
        }
        session.addInput(input)
        let output = AVCaptureMetadataOutput()
        guard session.canAddOutput(output) else {
            showMessage(String(localized: "Caméra indisponible. Saisissez les informations à la main."))
            return
        }
        session.addOutput(output)
        output.setMetadataObjectsDelegate(self, queue: .main)
        output.metadataObjectTypes = [.qr]

        let layer = AVCaptureVideoPreviewLayer(session: session)
        layer.videoGravity = .resizeAspectFill
        layer.frame = view.bounds
        view.layer.addSublayer(layer)
        previewLayer = layer
        configured = true
        startRunning()
    }

    private func startRunning() {
        guard configured, !session.isRunning else { return }
        lastCode = nil
        let captureSession = self.session
        DispatchQueue.global(qos: .userInitiated).async { captureSession.startRunning() }
    }

    private func showMessage(_ text: String) {
        let label = UILabel()
        label.text = text
        label.textColor = .white
        label.numberOfLines = 0
        label.textAlignment = .center
        label.font = .preferredFont(forTextStyle: .body)
        label.adjustsFontForContentSizeCategory = true
        label.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(label)
        NSLayoutConstraint.activate([
            label.centerYAnchor.constraint(equalTo: view.centerYAnchor),
            label.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 20),
            label.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -20),
        ])
    }

    func metadataOutput(_ output: AVCaptureMetadataOutput, didOutput metadataObjects: [AVMetadataObject],
                        from connection: AVCaptureConnection) {
        guard let object = metadataObjects.first as? AVMetadataMachineReadableCodeObject,
              let code = object.stringValue else { return }
        // Same code again only after 3 s, so a failed attempt (e.g. Wi-Fi) can be retried.
        guard code != lastCode || Date().timeIntervalSince(lastCodeDate) > 3 else { return }
        lastCode = code
        lastCodeDate = Date()
        UINotificationFeedbackGenerator().notificationOccurred(.success)
        onCode?(code)
    }
}
