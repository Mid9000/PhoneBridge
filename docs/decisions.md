# Décisions

- 2026-11-06 — Racine du dépôt = dossier `IBridge` (nom de projet : PhoneBridge). Raison : dossier choisi par Arthur.
- 2026-11-06 — Routage modèles : code → Opus 5.5 (effort élevé), textes → Haiku, tâches annexes → Sonnet, via sous-agents. Raison : demande d'Arthur.
- 2026-11-06 — Structure conforme à SPEC §15.
- 2026-11-06 — UI : Svelte 5 + Vite 8 + TypeScript, icônes `@lucide/svelte`, police Inter embarquée (`@fontsource-variable/inter`, sous-ensemble latin). Raison : SPEC §3/§10, licences libres, aucun accès réseau.
- 2026-11-06 — `design/preview.html` est généré depuis l'app (`npm run design`, données fictives) au lieu d'une maquette écrite à part. Raison : la maquette validée et l'app ne peuvent pas diverger.
- 2026-11-06 — Couleurs « fill » plus foncées (`#0071E3`, `#1F8236`) sous le texte blanc (bulles, boutons). Raison : `#007AFF`/`#34C759` avec texte blanc ne passent pas le contraste AA ; les couleurs du SPEC restent pour les accents.
- 2026-11-06 — Pont UI↔hôte : messages JSON typés via `chrome.webview.postMessage` / `PostWebMessageAsJson` (docs/ui-bridge.md), fichiers via `postMessageWithAdditionalObjects`. Raison : pas de port local à protéger, chemins réels des fichiers côté hôte.
- 2026-11-06 — iOS : épinglage sur le SHA-256 du certificat DER complet (pas SPKI), comme `GetCertHash` côté Windows. Raison : nom `certSha256` du protocole, une seule définition des deux côtés.
- 2026-11-06 — iOS : appairage stocké dans le trousseau de chaque cible + copie dans l'App Group si disponible (ids lus dans `ALTAppGroups` de SideStore/AltStore) ; sinon transfert vers la feuille de partage par « Copier la configuration » (presse-papiers local, 2 min). Raison : SPEC §11.6, la signature gratuite peut bloquer les App Groups.
- 2026-11-06 — iOS : widgets et commandes du Centre de contrôle ouvrent l'app (`openAppWhenRun`) au lieu d'agir en arrière-plan. Raison : iOS réserve le presse-papiers à l'app au premier plan.
- 2026-11-06 — iOS : Swift 5 (mode de langage), concurrence stricte « minimal ». Raison : code écrit sans compilateur, éviter les erreurs Swift 6 au premier build CI.
- 2026-11-06 — Hôte : WPF (net8.0-windows10.0.19041.0) + WebView2 + toasts `Microsoft.Toolkit.Uwp.Notifications` 7.1.3 + `QRCoder`. Raison : se compile sans MSIX ni Windows App SDK ; icône de zone de notification via WinForms `NotifyIcon` (aucun paquet).
- 2026-11-06 — mDNS via l'API Windows `DnsServiceRegister` (dnsapi.dll) au lieu d'un paquet NuGet. Raison : `Makaretu.Dns.Multicast` n'est plus maintenu et le service mDNS de Windows occupe déjà UDP 5353.
- 2026-11-06 — Jetons des appareils stockés en SHA-256 dans SQLite ; Credential Manager garde le mot de passe du certificat PFX. Raison : le PC n'a besoin que de vérifier les jetons, rien de réutilisable ne reste sur disque.
- 2026-11-06 — Port HTTP des Raccourcis désactivé = écouteur réellement fermé (pas seulement des 403). Raison : surface réseau minimale.
- 2026-11-06 — Fichiers reçus marqués « venant d'Internet » (Zone.Identifier). Raison : SmartScreen/mode protégé d'Office s'appliquent aux fichiers de l'iPhone.
- 2026-11-06 — Fermer la fenêtre la masque (l'app reste dans la zone de notification) ; « Quitter » dans le menu de l'icône. Raison : comportement d'app Mac, le serveur doit rester joignable.
- 2026-11-06 — Le QR d'appairage s'affiche dans une petite fenêtre native, pas dans l'UI web. Raison : le secret ne transite pas par le pont, et le contrat UI n'a pas de message pour ça.
