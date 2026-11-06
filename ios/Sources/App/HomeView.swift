import SwiftUI

struct HomeView: View {
    @EnvironmentObject private var model: AppModel

    var body: some View {
        List {
            Section {
                StatusCard()
            }

            Section {
                HStack {
                    Label("Presse-papiers → PC", systemImage: "arrow.up.doc")
                    Spacer()
                    // PasteButton reads the clipboard without iOS's "Allow Paste" prompt.
                    PasteButton(payloadType: String.self) { strings in
                        let text = strings.joined(separator: "\n")
                        Task { @MainActor in await model.sendClipboard(.text(text)) }
                    }
                    .labelStyle(.iconOnly)
                    .buttonBorderShape(.capsule)
                }
                Button {
                    Task { await model.fetchClipboard() }
                } label: {
                    Label("Récupérer le presse-papiers du PC", systemImage: "arrow.down.doc")
                }
            } header: {
                Text("Presse-papiers")
            }

            Section {
                if model.outbox.isEmpty && model.receivedFiles.isEmpty {
                    Text("Rien pour l'instant. Sur le PC : clic droit → Envoyer à → iPhone.")
                        .foregroundStyle(.secondary)
                }
                ForEach(model.outbox) { item in
                    OutboxRow(item: item)
                }
                ForEach(model.receivedFiles, id: \.self) { url in
                    ShareLink(item: url) {
                        Label(url.lastPathComponent, systemImage: "doc")
                    }
                    .swipeActions {
                        Button(role: .destructive) {
                            model.deleteReceivedFile(url)
                        } label: {
                            Label("Supprimer", systemImage: "trash")
                        }
                    }
                }
            } header: {
                Text("Reçu du PC")
            } footer: {
                Text("Les fichiers reçus sont aussi dans l'app Fichiers → Sur mon iPhone → PhoneBridge. Touchez un fichier pour l'enregistrer dans Photos ou ailleurs.")
            }
        }
        .navigationTitle("PhoneBridge")
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                NavigationLink {
                    SettingsView()
                } label: {
                    Image(systemName: "gearshape")
                        .accessibilityLabel(Text("Réglages"))
                }
            }
        }
        .refreshable { await model.refresh() }
    }
}

private struct StatusCard: View {
    @EnvironmentObject private var model: AppModel

    var body: some View {
        HStack(alignment: .top, spacing: 14) {
            Image(systemName: symbol)
                .font(.system(size: 34))
                .foregroundStyle(color)
                .frame(width: 44)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 4) {
                Text(title).font(.headline)
                switch model.connection {
                case .online:
                    if let bluetooth = model.status?.features?.bluetooth {
                        Text("Bluetooth : \(bluetooth)").font(.subheadline).foregroundStyle(.secondary)
                    }
                    if let date = model.lastUpdate {
                        Text("Mis à jour \(date.formatted(date: .omitted, time: .shortened))")
                            .font(.footnote).foregroundStyle(.secondary)
                    }
                case .offline(let reason):
                    Text(reason).font(.subheadline).foregroundStyle(.secondary)
                    Button {
                        model.discovery.start()
                    } label: {
                        Label("Rechercher le PC sur le réseau", systemImage: "antenna.radiowaves.left.and.right")
                    }
                    .font(.subheadline)
                    .buttonStyle(.borderless)
                case .unknown:
                    ProgressView()
                }
            }
        }
        .padding(.vertical, 6)
        .accessibilityElement(children: .combine)
    }

    private var pcName: String { model.pairing?.config.name ?? "PC" }

    private var title: String {
        switch model.connection {
        case .online: return String(localized: "Connecté à \(pcName)")
        case .offline: return String(localized: "\(pcName) hors de portée")
        case .unknown: return pcName
        }
    }

    private var symbol: String {
        switch model.connection {
        case .online: return "checkmark.circle.fill"
        case .offline: return "wifi.exclamationmark"
        case .unknown: return "desktopcomputer"
        }
    }

    private var color: Color {
        switch model.connection {
        case .online: return .green
        case .offline: return .orange
        case .unknown: return .secondary
        }
    }
}

private struct OutboxRow: View {
    @EnvironmentObject private var model: AppModel
    let item: OutboxItem

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: symbol)
                .foregroundStyle(.tint)
                .frame(width: 28)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(title).lineLimit(2)
                if let subtitle {
                    Text(subtitle).font(.footnote).foregroundStyle(.secondary).lineLimit(2)
                }
            }
            Spacer()
            if let fraction = model.progress[item.id] {
                ProgressView(value: fraction)
                    .frame(width: 60)
            } else {
                Button(actionTitle) {
                    Task { await model.open(item) }
                }
                .buttonStyle(.bordered)
            }
        }
        .swipeActions {
            Button(role: .destructive) {
                Task { await model.acknowledge(item) }
            } label: {
                Label(item.kind == .reply ? LocalizedStringKey("Envoyée") : LocalizedStringKey("Retirer"),
                      systemImage: "checkmark")
            }
        }
    }

    private var symbol: String {
        switch item.kind {
        case .file: return "doc"
        case .clipboard: return "doc.on.clipboard"
        case .link: return "link"
        case .reply: return "bubble.left"
        case .unknown: return "questionmark.circle"
        }
    }

    private var title: String {
        switch item.kind {
        case .file: return item.name ?? String(localized: "Fichier")
        case .clipboard: return item.text ?? String(localized: "Presse-papiers du PC")
        case .link: return item.url ?? String(localized: "Lien")
        case .reply: return String(localized: "Réponse pour \(item.to ?? "?")")
        case .unknown: return String(localized: "Élément inconnu")
        }
    }

    private var subtitle: String? {
        switch item.kind {
        case .file: return item.size.map { ByteCountFormatter.string(fromByteCount: $0, countStyle: .file) }
        case .reply: return item.text
        default: return nil
        }
    }

    private var actionTitle: LocalizedStringKey {
        switch item.kind {
        case .file: return "Télécharger"
        case .clipboard: return "Copier"
        case .link: return "Ouvrir"
        case .reply: return "Envoyer"
        case .unknown: return "Ouvrir"
        }
    }
}
