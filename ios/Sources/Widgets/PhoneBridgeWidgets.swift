import AppIntents
import SwiftUI
import WidgetKit

@main
struct PhoneBridgeWidgets: WidgetBundle {
    var body: some Widget {
        ClipboardWidget()
        if #available(iOS 18.0, *) {
            SendClipboardControl()
            FetchClipboardControl()
        }
    }
}

// MARK: Home screen widget (iOS 17 interactive buttons)

struct ClipboardEntry: TimelineEntry {
    let date: Date
}

struct ClipboardProvider: TimelineProvider {
    func placeholder(in context: Context) -> ClipboardEntry {
        ClipboardEntry(date: Date())
    }

    func getSnapshot(in context: Context, completion: @escaping (ClipboardEntry) -> Void) {
        completion(ClipboardEntry(date: Date()))
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<ClipboardEntry>) -> Void) {
        // Static buttons: nothing to refresh.
        completion(Timeline(entries: [ClipboardEntry(date: Date())], policy: .never))
    }
}

struct ClipboardWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "ClipboardWidget", provider: ClipboardProvider()) { _ in
            ClipboardWidgetView()
        }
        .configurationDisplayName("Presse-papiers PC")
        .description("Envoyer le presse-papiers au PC ou récupérer celui du PC.")
        .supportedFamilies([.systemSmall, .systemMedium])
    }
}

struct ClipboardWidgetView: View {
    @Environment(\.widgetFamily) private var family

    var body: some View {
        Group {
            if family == .systemSmall {
                VStack(spacing: 8) { buttons }
            } else {
                HStack(spacing: 8) { buttons }
            }
        }
        .containerBackground(.fill.tertiary, for: .widget)
    }

    @ViewBuilder
    private var buttons: some View {
        Button(intent: OpenBridgeActionIntent(action: .sendClipboard)) {
            Label("Vers le PC", systemImage: "arrow.up.doc")
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        Button(intent: OpenBridgeActionIntent(action: .fetchClipboard)) {
            Label("Du PC", systemImage: "arrow.down.doc")
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }
}

// MARK: Control Center (iOS 18)

@available(iOS 18.0, *)
struct SendClipboardControl: ControlWidget {
    var body: some ControlWidgetConfiguration {
        StaticControlConfiguration(kind: "com.arthur.phonebridge.control.send") {
            ControlWidgetButton(action: OpenBridgeActionIntent(action: .sendClipboard)) {
                Label("Presse-papiers → PC", systemImage: "arrow.up.doc")
            }
        }
        .displayName("Presse-papiers → PC")
        .description("Envoie le presse-papiers de l'iPhone au PC.")
    }
}

@available(iOS 18.0, *)
struct FetchClipboardControl: ControlWidget {
    var body: some ControlWidgetConfiguration {
        StaticControlConfiguration(kind: "com.arthur.phonebridge.control.fetch") {
            ControlWidgetButton(action: OpenBridgeActionIntent(action: .fetchClipboard)) {
                Label("Presse-papiers du PC", systemImage: "arrow.down.doc")
            }
        }
        .displayName("Presse-papiers du PC")
        .description("Copie sur l'iPhone le dernier presse-papiers du PC.")
    }
}
