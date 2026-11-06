# Avancement PhoneBridge

## Phase 0 — Mise en place
- [x] Dépôt git, structure, `SPEC.md`, `CLAUDE.md`
- [x] Sous-agents routés par modèle (`.claude/agents/`)
- [x] Workflow CI Windows (`.github/workflows/windows.yml`, s'active quand `windows/PhoneBridge.sln` existe)
- [x] .NET 8 SDK installé (8.0.427)
- [ ] Infos matériel à récupérer (voir "Questions pour Arthur")
- [x] Squelette solution .NET (Core, Server, Bluetooth, Host, tests) + build vert

## Phase 1 — Design + socle Wi-Fi
- [x] `docs/design-tokens.md` + `design/preview.html` (maquette interactive clair/sombre, à valider par Arthur)
- [x] Interface `windows/ui` (Svelte) : toutes les sections, états vide / hors de portée / erreur, FR, clavier, ARIA, réduction des animations ; données fictives hors WebView2
- [x] Contrat UI ↔ hôte : `docs/ui-bridge.md` ; tests vitest du pont et du mock
- [ ] Validation de la maquette par Arthur
- [x] Hôte WPF + WebView2 + Mica branché sur le pont (`windows/src/PhoneBridge.Host`) : fenêtre sans bordure, Mica, coins arrondis, drag/réduire/agrandir/fermer, menu rapide `#quick` dans la zone de notification, QR d'appairage natif — lancé en `--smoke` (UI chargée, poignée de main `ready`→`state` OK) ; **rendu visuel non vérifié** (session verrouillée)
- [x] Core : modèles, DTO du protocole, SQLite (appareils, messages, notifications, transferts, presse-papiers, outbox), assainisseur de noms, détecteur OTP, appairage (secret 32 o, 5 min, usage unique), limitation de débit, outbox, file de réponses
- [x] Serveur HTTPS 47821 (certificat auto-signé persistant, empreinte SHA-256) + HTTP 47822 limité aux routes [shortcuts], désactivable ; IP privées seulement ; jeton Bearer ; 429 sur /pair et échecs d'auth ; aucun contenu dans les logs
- [x] Routes `/pair`, `/status`, `/files` (streaming, hash, nom assaini, Mark-of-the-Web), `/outbox` (+ Range, DELETE), `/links`, `/clipboard`, `/messages`, `/battery`, WebSocket `/events` — `docs/openapi.yaml`
- [x] mDNS `_phonebridge._tcp` via le répondeur mDNS de Windows (`DnsServiceRegister`) : enregistrement OK (statut 0) ; **découverte depuis l'iPhone non vérifiée**
- [x] Toasts natifs (réponse intégrée pour les messages, OTP, fichier reçu, lien), presse-papiers (exclusion historique Win+V / cloud pour les codes, effacement 60 s, surveillance des copies PC), ouverture de liens http/https, Credential Manager (mot de passe du certificat)
- [x] Scripts `windows/scripts/` : `run-dev.ps1`, `publish.ps1` (dossier autonome vérifié), `firewall.ps1` (profil Privé, sous-réseau local ; jamais exécuté par l'agent)
- [x] Tests xUnit (69) : assainisseur, OTP, appairage, limitation, file de réponses, BDD, intégration serveur réel (appairage, envoi + hash, Range, 401, port HTTP, IP publiques, WebSocket)
- [x] Test de bout en bout curl contre l'hôte lancé : fichier 10 Mo hash identique, collision `(1)`, 401/404/410/400 attendus, WebSocket 101, aucun contenu dans le log
- [ ] Raccourcis iOS (`shortcuts/` + `docs/shortcuts-guide.md`)
- [ ] Critères d'acceptation Phase 1 sur le vrai iPhone (vidéo 500 Mo, SMS < 5 s, OTP, lien Safari, Toucher le dos, requête sans jeton)

## Phase 5 — App iOS (code écrit, jamais compilé : premier build = CI GitHub)
- [x] `ios/` : XcodeGen, app SwiftUI (QR + saisie manuelle, HTTPS épinglé, Bonjour, carte d'état, « Reçu du PC » avec reprise Range, presse-papiers, réglages, guide), Share Extension (arrière-plan si App Group), App Intents, widget + commandes iOS 18, XCTest — voir `ios/README.md`
- [x] CI `.github/workflows/ios.yml` (tests simulateur + .ipa non signée) ; guide `docs/ios-install.md`
- [ ] Premier build vert en CI (nécessite le dépôt sur GitHub)
- [ ] Validation sur l'iPhone d'Arthur : appairage QR < 30 s, 50 photos via la feuille de partage, fichier PC→iPhone à l'ouverture, presse-papiers PC→iPhone

## Questions pour Arthur (une seule fois, groupées)
2. Version d'iOS de l'iPhone 13 (Réglages → Général → Informations → Version iOS).
3. Je peux lancer moi-même `Get-PnpDevice -Class Bluetooth` et `winver` pour le PC (lecture seule) : OK ?
4. Un compte GitHub (gratuit) pour la CI iOS : as-tu déjà un compte ? Dépôt privé ou public ?

## Phases suivantes
Voir SPEC §12 : P1 Design + socle Wi-Fi • P2 SPIKE ANCS • P3 SPIKE MAP/PBAP • P4 Messagerie • P5 App iOS • P6 Confort • P7 Stretch.
