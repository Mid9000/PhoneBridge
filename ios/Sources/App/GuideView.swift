import SwiftUI
import UIKit

/// Step-by-step setup of the "no app needed" integrations (SPEC §7, §10.3).
struct GuideView: View {
    @Environment(\.openURL) private var openURL

    var body: some View {
        List {
            Section {
                Step(number: 1, text: "Ouvrez Raccourcis → + → Ajouter une action → PhoneBridge → « Envoyer le presse-papiers au PC ».")
                Step(number: 2, text: "Avant cette action, ajoutez « Obtenir le presse-papiers » et branchez-le sur le champ Texte. Nommez le raccourci « Presse-papiers → PC ».")
                Step(number: 3, text: "Créez de même « Presse-papiers du PC » : « Récupérer le presse-papiers du PC » puis « Copier dans le presse-papiers ».")
                Button("Ouvrir Raccourcis") { open("shortcuts://") }
            } header: {
                Text("1. Raccourcis presse-papiers")
            }

            Section {
                Step(number: 1, text: "Réglages → Accessibilité → Toucher → Toucher le dos.")
                Step(number: 2, text: "Toucher deux fois : « Presse-papiers → PC ». Toucher trois fois : « Presse-papiers du PC ».")
                Button("Ouvrir Réglages") { open(UIApplication.openSettingsURLString) }
            } header: {
                Text("2. Toucher le dos")
            }

            Section {
                Step(number: 1, text: "Écran d'accueil : appui long → Modifier → Ajouter un widget → PhoneBridge.")
                Step(number: 2, text: "iOS 18 : Centre de contrôle → appui long → Ajouter une commande → PhoneBridge.")
                Text("Ces boutons ouvrent brièvement l'app : iOS réserve le presse-papiers à l'app au premier plan.")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            } header: {
                Text("3. Widget et Centre de contrôle")
            }

            Section {
                Step(number: 1, text: "Raccourcis → Automatisation → + → Message → « Exécuter immédiatement ».")
                Step(number: 2, text: "Action PhoneBridge « Envoyer les réponses du PC », puis « Répéter avec chaque élément » : « Envoyer un message » (Texte, Destinataire), puis « Marquer la réponse comme envoyée ».")
                Text("iOS peut demander une confirmation avant d'envoyer un message depuis un raccourci. Sinon, touchez « Envoyer » sur une réponse dans l'app : Messages s'ouvre avec le texte prêt.")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            } header: {
                Text("4. Réponses écrites sur le PC")
            }

            Section {
                Step(number: 1, text: "Appairez l'iPhone dans les paramètres Bluetooth de Windows.")
                Step(number: 2, text: "iPhone : Réglages → Bluetooth → (i) à côté du PC → activez « Afficher les notifications » et « Synchroniser les contacts » (libellés à vérifier selon la version d'iOS).")
                Button("Ouvrir Réglages") { open(UIApplication.openSettingsURLString) }
            } header: {
                Text("5. Bluetooth (notifications, messages)")
            }

            Section {
                Step(number: 1, text: "Dans une app (Photos, Safari, Fichiers…) touchez Partager → « Envoyer au PC ».")
                Step(number: 2, text: "Pour l'avoir en premier : Partager → Plus → Modifier → ajoutez-le aux favoris.")
            } header: {
                Text("6. Feuille de partage")
            }
        }
        .navigationTitle("Guide d'installation")
    }

    private func open(_ string: String) {
        if let url = URL(string: string) { openURL(url) }
    }
}

private struct Step: View {
    let number: Int
    let text: LocalizedStringKey

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 10) {
            Text("\(number)")
                .font(.footnote.weight(.semibold))
                .foregroundStyle(.white)
                .frame(width: 22, height: 22)
                .background(Circle().fill(Color.accentColor))
                .accessibilityHidden(true)
            Text(text)
        }
    }
}
