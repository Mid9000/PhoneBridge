import SwiftUI

struct SettingsView: View {
    @EnvironmentObject private var model: AppModel
    @AppStorage(SettingsKey.autoApplyClipboard) private var autoApplyClipboard = true
    @AppStorage(SettingsKey.autoDownloadFiles) private var autoDownloadFiles = true
    @AppStorage(SettingsKey.autoSendClipboard) private var autoSendClipboard = false
    @AppStorage(SettingsKey.notifications) private var notifications = true
    @State private var showRepair = false
    @State private var confirmForget = false
    @State private var confirmCopyToken = false

    var body: some View {
        Form {
            Section {
                if let config = model.pairing?.config {
                    LabeledContent("PC", value: config.name)
                    LabeledContent("Adresse", value: "\(config.host):\(config.port)")
                    if let version = model.status?.version {
                        LabeledContent("Version sur le PC", value: version)
                    }
                }
                Button("Rechercher le PC sur le réseau") { model.discovery.start() }
                Button("Ré-appairer…") { showRepair = true }
                Button("Oublier ce PC", role: .destructive) { confirmForget = true }
            } header: {
                Text("Appareil appairé")
            }

            if let config = model.pairing?.config {
                Section {
                    // Hidden only when the PC says the HTTP channel is off; unknown status (offline) still shows it.
                    if let address = config.shortcutsAddress, model.status?.features?.shortcutsHttp != false {
                        LabeledContent("Adresse pour les Raccourcis") {
                            HStack {
                                Text(verbatim: address).textSelection(.enabled)
                                Button { model.copyShortcutsAddress() } label: {
                                    Image(systemName: "doc.on.doc")
                                }
                                .buttonStyle(.borderless)
                                .accessibilityLabel("Copier l'adresse")
                            }
                        }
                    }
                    Button("Copier le jeton") { confirmCopyToken = true }
                } header: {
                    Text("Raccourcis")
                } footer: {
                    Text("Le jeton va dans l'en-tête Authorization : « Bearer » suivi du jeton. Ne partagez jamais un raccourci qui le contient.")
                }
            }

            Section {
                Toggle("Appliquer le presse-papiers du PC", isOn: $autoApplyClipboard)
                Toggle("Télécharger les fichiers reçus", isOn: $autoDownloadFiles)
                Toggle("Envoyer le presse-papiers de l'iPhone", isOn: $autoSendClipboard)
            } header: {
                Text("À l'ouverture de l'app")
            } footer: {
                Text("L'envoi automatique lit le presse-papiers : iOS peut demander « Autoriser à coller ». Réglages → PhoneBridge → Coller depuis d'autres apps → Autoriser l'évite.")
            }

            Section {
                Toggle("Notifications locales", isOn: $notifications)
            } footer: {
                Text("Exemple : « Fichier reçu du PC » quand un téléchargement se termine en arrière-plan.")
            }

            Section {
                if PairingStore.isShared {
                    Label("Configuration partagée automatiquement", systemImage: "checkmark.circle")
                } else {
                    Button("Copier la configuration pour la feuille de partage") { model.copyConfigForExtensions() }
                }
            } header: {
                Text("Feuille de partage")
            } footer: {
                if !PairingStore.isShared {
                    Text("La signature gratuite ne permet pas toujours de partager l'appairage avec l'extension. Copiez la configuration ici, puis dans la feuille de partage touchez « Coller ». Elle reste 2 minutes dans le presse-papiers, uniquement sur cet iPhone.")
                }
            }

            Section {
                NavigationLink {
                    GuideView()
                } label: {
                    Label("Guide d'installation", systemImage: "book")
                }
            }

            Section {
                LabeledContent("Version", value: Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "?")
            } header: {
                Text("À propos")
            } footer: {
                Text("Tout reste sur votre réseau local. Aucune donnée n'est envoyée sur Internet.")
            }
        }
        .navigationTitle("Réglages")
        .sheet(isPresented: $showRepair) {
            NavigationStack {
                PairingView(isSheet: true)
                    .toolbar {
                        ToolbarItem(placement: .cancellationAction) {
                            Button("Annuler") { showRepair = false }
                        }
                    }
            }
            .environmentObject(model)
        }
        .confirmationDialog("Oublier ce PC ?", isPresented: $confirmForget, titleVisibility: .visible) {
            Button("Oublier", role: .destructive) { model.unpair() }
        } message: {
            Text("Il faudra scanner à nouveau le QR code. Pensez aussi à révoquer cet iPhone sur le PC.")
        }
        .alert("Copier le jeton ?", isPresented: $confirmCopyToken) {
            Button("Copier") { model.copyToken() }
            Button("Annuler", role: .cancel) {}
        } message: {
            Text("Ce jeton donne accès à votre PC. Collez-le uniquement dans vos propres raccourcis. Il reste 2 minutes dans le presse-papiers, uniquement sur cet iPhone. En cas de fuite, révoquez cet iPhone sur le PC.")
        }
    }
}
