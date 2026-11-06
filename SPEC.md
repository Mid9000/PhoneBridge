# PhoneBridge — Cahier des charges v2 pour Claude Code

> Nom de travail : **PhoneBridge** (renommable).
> Objectif : faire de l'iPhone 13 d'Arthur et de son PC Windows 11 un duo qui se comporte **comme un iPhone et un Mac** : tout ce qu'il fait sur son iPhone (recevoir et **répondre** à un message, voir ses notifications, copier-coller, envoyer des fichiers, voir un appel) doit pouvoir se faire **depuis le PC**, avec une interface Windows qui ressemble à une **app macOS**.
> Auteur : Arthur (pseudo Mid9), lycéen en Terminale (spés Maths + NSI), Rennes. Projet perso, pas de budget, pas de Mac.

### Changements depuis la v1 (décisions d'Arthur)
- **Retirés (hors périmètre, peut-être plus tard)** : sauvegarde auto des photos, miroir d'écran iPhone, pilotage du PC depuis l'iPhone, iPhone en webcam.
- **Gardés et priorisés** : liens, **presse-papiers dans les deux sens**, codes de vérification (OTP), appels entrants, batterie, historique.
- **Poussé au maximum** : **répondre aux messages depuis le PC** devient une fonction centrale (plusieurs pistes en cascade, section 6).
- **Nouveau** : section Design (macOS-like côté Windows, intégrations système répartie côté iPhone), contacts, synchro lu/non-lu, réponses rapides, Windows Hello.

---

## 0. Instructions pour Claude Code

1. Lis tout ce fichier avant de coder. Copie-le à la racine du repo sous `SPEC.md` et crée un `CLAUDE.md` court qui y renvoie.
2. Travaille **phase par phase** (section 12). Ne passe à la suivante qu'après validation des critères d'acceptation **par Arthur sur son vrai matériel**.
3. Les parties marquées **SPIKE** sont des expérimentations risquées : fais un prototype minimal, dis honnêtement s'il marche, applique le plan B. Ne présente jamais un spike comme acquis.
4. Arthur est débutant-intermédiaire : explique tes choix en quelques lignes, donne les commandes exactes (PowerShell), réponds en français ; code et identifiants en anglais.
5. Ne suppose rien de ce qui est marqué **[À VÉRIFIER]** : teste ou demande.
6. Jamais d'argent, de compte payant ni de jailbreak. Si une étape l'exige, propose le plan B.
7. **Ne copie aucun asset Apple** (voir 10.6).

---

## 1. Contexte et contraintes

| Élément | Valeur |
|---|---|
| Téléphone | iPhone 13. Version d'iOS **[À VÉRIFIER]**. Cible de développement : **iOS 17 minimum** (widgets interactifs), avec des fonctions bonus iOS 18+ |
| PC | Windows 11 **[À VÉRIFIER : version, adaptateur Bluetooth]**. L'adaptateur doit gérer **Bluetooth Classic (BR/EDR) ET Low Energy** (double mode) |
| Mac | **Aucun** (pas de Xcode local) |
| Compte Apple | Apple ID gratuit |
| Localisation | France (UE) |
| Règle d'or | Tout **en local** : pas de cloud, pas de compte, pas de serveur tiers (sauf option explicitement activée, voir 9) |

### Ce qu'Apple interdit (ne pas essayer de contourner)
- Pas d'accès complet à iMessage (photos, groupes, réactions, effets) sans Mac relais.
- Pas de Handoff ni de Continuity officielle, pas d'AirDrop avec Windows.
- iOS limite la lecture du presse-papiers et les tâches de fond : une app iPhone ne tourne pas en permanence. **Toute la conception évite de dépendre d'une app toujours active** (voir 4 et 11).

---

## 2. Fonctionnalités

### 2.1 Cœur (obligatoire)

| # | Fonction | Détails |
|---|---|---|
| F1 | **Fichiers iPhone → PC** | Via la feuille de partage iOS (« Partager → PC d'Arthur »), un dossier ou plusieurs fichiers, progression, reprise |
| F2 | **Fichiers PC → iPhone** | Clic droit « Envoyer à → iPhone », glisser-déposer sur la fenêtre ou l'icône de la barre des tâches |
| F3 | **Notifications iPhone sur le PC** | Toutes les apps (WhatsApp, Discord, Instagram…), avec icône/nom de l'app, titre, texte, heure. Fermer une notif sur le PC la ferme sur l'iPhone (si possible) |
| F4 | **Messages (SMS + iMessage) sur le PC** | Liste de conversations, fil de discussion, noms des contacts, non-lus |
| F5 | **Répondre aux messages depuis le PC** | Depuis la notification Windows (champ de réponse intégré) et depuis l'app. **Fonction centrale à pousser au maximum** (section 6) |
| F6 | **Presse-papiers dans les deux sens** | Texte, liens, images ; copier sur l'iPhone → coller sur le PC, et inversement (section 7) |
| F7 | **Liens** | « Ouvrir sur le PC » depuis l'iPhone ; « Ouvrir sur l'iPhone » depuis le PC |
| F8 | **Codes de vérification (OTP)** | Détecte un code dans un SMS/notification, le copie automatiquement dans le presse-papiers du PC et prévient |

### 2.2 Confort (ensuite)

| # | Fonction | Notes |
|---|---|---|
| C1 | **Appels entrants** | Notification « X vous appelle » sur le PC avec **Refuser / Répondre** (réponse = décrocher sur l'iPhone). Appels manqués dans l'historique. L'audio sur le PC (HFP) est un stretch. L'interface sera affinée par Arthur plus tard |
| C2 | **Batterie de l'iPhone** | Niveau + état de charge dans l'app et la barre des tâches ; alertes < 20 % et « 100 % chargé » |
| C3 | **Contacts** | Lecture seule (noms, photos) pour afficher « Maman » au lieu d'un numéro |
| C4 | **Historique et recherche** | Tout est stocké en local (SQLite), recherche rapide (Ctrl+K) sur messages, notifs, fichiers, presse-papiers |
| C5 | **Réponses rapides** | Modèles (« J'arrive dans 5 min »), suggestions, raccourcis clavier |
| C6 | **Filtres de notifications** | Par app : afficher / masquer / silencieux ; heures calmes ; « ne pas afficher le contenu » |
| C7 | **Synchro lu / non-lu** | Lire un message sur le PC le marque comme lu sur l'iPhone (MAP le permet) |
| C8 | **Verrouillage Windows Hello** | Option : le contenu des messages demande un visage/PIN pour s'afficher |
| C9 | **Rechercher / sonner mon iPhone** | Stretch (iOS ne permet pas de faire sonner sans Localiser) |
| C10 | **Synchro Ne pas déranger / Focus** | Stretch, dépend de ce qu'iOS expose |

### 2.3 Hors périmètre (explicitement refusé par Arthur pour l'instant)
Sauvegarde auto des photos • miroir de l'écran de l'iPhone sur le PC • pilotage du PC depuis l'iPhone (volume, média, extinction) • iPhone comme webcam.
Ne pas les implémenter, ne pas les proposer en cours de route, ne pas préparer d'API pour eux.

---

## 3. Architecture

```
┌────────────────────── iPhone 13 ──────────────────────┐        ┌──────────────── PC Windows 11 ────────────────────┐
│ App SwiftUI "PhoneBridge" (centre de contrôle léger)   │        │ PhoneBridge.exe (hôte .NET 8 + interface WebView2)│
│  • Statut, historique, réglages, guide d'installation  │  Wi-Fi │  • Serveur HTTPS/HTTP + WebSocket (Kestrel)       │
│ Intégrations système (réparties partout) :             │◄──────►│  • mDNS, appairage QR, historique SQLite          │
│  • Share Extension (fichiers, texte, liens)            │  LAN   │  • Toasts Windows avec champ de réponse           │
│  • App Intents → Raccourcis, Siri, Spotlight           │        │  • Presse-papiers, ouverture de liens, OTP        │
│  • Widgets + Centre de contrôle + Toucher le dos       │        │                                                   │
│ Raccourcis iOS (Automations en arrière-plan, sans app) │        │ Module Bluetooth :                                │
│  • Message reçu, batterie, charge… → POST au PC        │        │  • ANCS (LE) : notifications + actions            │
│                                                        │        │  • MAP/OBEX (RFCOMM) : messages lire/envoyer      │
│ iOS système, via Bluetooth appairé à Windows :         │        │  • PBAP/OBEX (RFCOMM) : contacts, historique appels│
│  • ANCS • MAP • PBAP ──────────────────────────────────┼────────┼──► (aucune app requise sur l'iPhone pour ceux-ci) │
└────────────────────────────────────────────────────────┘        └───────────────────────────────────────────────────┘
```

### Quatre canaux
- **A — Wi-Fi local** (HTTPS épinglé + WebSocket) : fichiers, liens, presse-papiers, statut, commandes. Fiable, rapide.
- **B — Bluetooth LE / ANCS** : notifications de toutes les apps, actions sur notifications, appels entrants. **SPIKE.**
- **C — Bluetooth Classic / MAP + PBAP (OBEX sur RFCOMM)** : lire et **envoyer** des messages, contacts, historique d'appels. **SPIKE** (c'est ce que fait Phone Link de Microsoft sur iPhone).
- **D — Raccourcis iOS (Automations)** : pousse des événements en arrière-plan sans app (message reçu, batterie, charge) et sert de **plan B** pour les canaux B et C.

### Choix technologiques
- **Hôte Windows : C# / .NET 8** (WPF ou WinUI 3 comme coque) :
  - Backend : ASP.NET Core minimal API (Kestrel) + WebSocket, SQLite (`Microsoft.Data.Sqlite`), mDNS (`Makaretu.Dns.Multicast` ou équivalent **[À VÉRIFIER]**).
  - Bluetooth : `Windows.Devices.Bluetooth` (LE/GATT) et `Windows.Devices.Bluetooth.Rfcomm` (RFCOMM pour OBEX/MAP/PBAP), utilisables par les apps de bureau.
  - Notifications : `AppNotificationManager` (Windows App SDK) ou `Microsoft.Toolkit.Uwp.Notifications`, avec champ de saisie et boutons.
  - **Interface : WebView2 (HTML/CSS/TypeScript)**, dans une fenêtre sans bordure avec fond **Mica** (effet matériau de Windows 11, via `DwmSetWindowAttribute` / `DWMWA_SYSTEMBACKDROP_TYPE`). Raison : un rendu fidèle à macOS est beaucoup plus simple en web qu'avec les contrôles Windows natifs. Front en **Svelte** (ou React), Vite, CSS variables, pas de framework UI lourd.
  - Communication UI ↔ hôte : pont `WebView2.PostWebMessage` ou WebSocket sur `localhost` avec jeton.
- **iPhone : Swift / SwiftUI**, cible iOS 17+, aucune dépendance externe si possible. Cible : app + Share Extension + Widget/Controls extension. Compilation **sans Mac** via GitHub Actions (section 11).
- **Contrat unique** : `docs/protocol.md` + `docs/openapi.yaml`, maintenu à partir du code serveur.

---

## 4. Canal A : Wi-Fi local

### Découverte
Service mDNS/Bonjour `_phonebridge._tcp.local`, port HTTPS **47821**. Repli : IP saisie à la main ou contenue dans le QR code.

### Appairage (une fois)
1. Le PC génère un certificat TLS auto-signé et un **secret d'appairage** à usage unique (32 octets, 5 min).
2. Il affiche un **QR code** : `{ host, port, certSha256, pairingSecret, deviceId, name }`.
3. L'iPhone scanne (caméra de l'app), appelle `POST /v1/pair`, reçoit un **jeton** (256 bits) propre à l'appareil.
4. L'app épingle l'empreinte du certificat. Jeton dans le **Keychain** (iOS) et le **Credential Manager** (Windows).
5. Le PC liste et **révoque** les appareils.

### ⚠ Les Raccourcis iOS refusent le HTTPS auto-signé
Le canal D ne peut donc pas parler au serveur HTTPS épinglé. Solution :
- Un **second écouteur HTTP** (port 47822), **désactivable**, limité aux adresses privées (192.168.x.x, 10.x, 172.16-31.x), qui exige `Authorization: Bearer <jeton>`.
- N'expose que les routes marquées `[shortcuts]`.
- Activé par défaut en phase 1, proposé en option ensuite. L'interface explique clairement le risque (trafic non chiffré sur le Wi-Fi de la maison).

### Endpoints (préfixe `/v1`)
| Méthode | Route | Rôle |
|---|---|---|
| POST | `/pair` | Échange du secret contre un jeton |
| GET | `/status` | Version, nom du PC, fonctions actives, état Bluetooth |
| POST | `/files` `[shortcuts]` | Envoi iPhone→PC en streaming. En-têtes : `X-Filename` (URL-encodé), `X-Size`, `X-Sha256` (opt.) |
| GET | `/outbox` | Éléments en attente PC→iPhone (fichiers, presse-papiers, liens, réponses SMS) |
| GET | `/outbox/{id}` | Téléchargement (support `Range`) |
| DELETE | `/outbox/{id}` | Accusé de réception |
| POST | `/links` `[shortcuts]` | `{ url, title? }` → ouvert sur le PC |
| POST/GET | `/clipboard` `[shortcuts]` | iPhone→PC (POST) et dernier contenu PC→iPhone (GET) |
| POST | `/messages` `[shortcuts]` | `{ sender, body, service: "sms"/"imessage", receivedAt }` |
| POST | `/battery` `[shortcuts]` | `{ level, charging, source }` |
| GET (WS) | `/events` | Flux temps réel vers l'iPhone (outbox mise à jour, etc.) |

Règles : noms de fichiers assainis (anti path-traversal, caractères interdits Windows, collisions → `(1)`), dossier par défaut `Téléchargements\PhoneBridge\`, taille max configurable, hash vérifié si fourni.

---

## 5. Canal B : Notifications par Bluetooth LE (ANCS) — SPIKE

ANCS (*Apple Notification Center Service*) est un service GATT **intégré à iOS** : un accessoire appairé reçoit **toutes les notifications** sans aucune app sur l'iPhone. Référence existante : `gabrielmeir53/iphonebridge` et `BlueFerry` (Linux).

### Détails à vérifier dans la spec Apple « Apple Notification Center Service »
- Service ANCS : `7905F431-B5CE-4E99-A40F-4B1E122D00D0`
- *Notification Source* (notify) : `9FBF120D-6301-42D9-8C58-25E699A21DBD`
- *Control Point* (write) : `69D1D8F3-45E1-49A8-9821-9BBDFDAAD9D9`
- *Data Source* (notify) : `22EAC6E9-24D6-4BB5-BE44-B36ACE7C7BFB`
- Notification Source : 8 octets (EventID ajout/modif/suppression, EventFlags, CategoryID, CategoryCount, NotificationUID sur 4 octets).
- `GetNotificationAttributes` (CommandID 0) pour récupérer AppIdentifier, Title, Subtitle, Message, Date, libellés d'action ; réponse fragmentée via Data Source. `GetAppAttributes` (CommandID 1) donne le nom d'affichage de l'app. `PerformNotificationAction` (CommandID 2) : action positive/négative (ex. **Répondre/Refuser un appel**, ignorer).
- Catégories utiles : `incomingCall`, `missedCall`, `email`, `social`, etc.
- **Bonding LE obligatoire.** Sur l'iPhone, l'utilisateur doit activer dans **Réglages → Bluetooth → (i) à côté du PC** les options de partage (libellés iOS à confirmer : « Afficher les notifications », « Partager les notifications du système », « Synchroniser les contacts ») **[À VÉRIFIER]**.

### Difficulté principale
Sous Linux, `iphonebridge` doit faire en sorte que l'iPhone se connecte au PC **comme à un accessoire** (publicité BLE avec sollicitation ANCS, classe « mains libres », bonding LE) et a même évoqué le besoin d'un **second adaptateur Bluetooth**. Sous Windows, ce n'est pas garanti, mais **Phone Link reçoit déjà les notifs iPhone** sur un PC Windows : c'est donc possible. Conflit probable : **Phone Link s'approprie l'appairage**.

### Plan du spike
1. Console C# `tools/spike-ancs` : lister les appareils appairés, se connecter à l'iPhone, énumérer ses services GATT, chercher ANCS.
2. Sinon, tester `GattServiceProvider` + `BluetoothLEAdvertisementPublisher` (PC en périphérique) avec sollicitation ANCS.
3. Tester la **cohabitation avec Phone Link** : délié, désactivé, désinstallé.
4. Si aucun adaptateur interne ne convient, tester avec une clé USB Bluetooth 5.x double mode (quelques euros).
5. Consigner les résultats dans `docs/ancs-notes.md`, même les échecs.

---

## 6. Messages et **réponses** (cœur du projet)

Objectif : une vraie conversation sur le PC (liste, fil, non-lus, noms, envoi). Voici les pistes, **à essayer dans cet ordre** avec une décision à chaque étape.

### Ce que Phone Link prouve
Phone Link pour iPhone utilise **le Bluetooth** (profil MAP pour les messages, plus ANCS et PBAP) : il lit et envoie des SMS/iMessage depuis Windows, mais sans groupes, images, vidéos, vocaux ni apps iMessage, et seulement **quand l'iPhone est à portée Bluetooth**. Donc la voie technique existe côté Windows. Le projet Linux `iphonebridge` confirme qu'**iMessage est lisible et envoyable via MAP sur iOS 26.5**, mais que **les messages écrits sur l'iPhone lui-même restent invisibles** via MAP.

### Piste 1 — MAP/OBEX sur RFCOMM (principale) — SPIKE
Implémenter un client MAP maison dans `PhoneBridge.Bluetooth` :
- Connexion RFCOMM au service **MAS** (Message Access Server, classe 0x1132) de l'iPhone via `RfcommDeviceService`.
- Couche **OBEX** minimale : `Connect` (avec l'UUID cible MAS `BB582B40-420C-11DB-B0DE-0800200C9A66` **[À VÉRIFIER]**), `SetPath`, `Get`, `Put`, `Disconnect`.
- Opérations MAP : `GetFolderListing`, `GetMessagesListing` (`x-bt/MAP-msg-listing`), `GetMessage` (`x-bt/message`, format **bMessage**), **`PushMessage`** (envoi), `SetMessageStatus` (lu/non-lu), `SetNotificationRegistration`.
- Réception en temps réel : publier un service **MNS** (classe 0x1133, `RfcommServiceProvider`) pour que l'iPhone pousse les événements « nouveau message ».
- Contacts : **PBAP** (UUID cible `796135F0-F0C5-11D8-0966-0800200C9A66` **[À VÉRIFIER]**), vCard → noms, photos, historique d'appels.
- **S'inspirer du code de `iphonebridge` et `BlueFerry`** (Linux) pour les particularités d'iOS (ordre des opérations, champs obligatoires, destinataire par numéro ou adresse iMessage).
- Prérequis iPhone : activer les options de partage dans Réglages → Bluetooth → PC, et accorder l'accès aux messages. Un seul client MAP à la fois : **Phone Link doit être délié**.
- **Critère de réussite du spike** : lister les 10 derniers messages et en envoyer un à un numéro de test (ex. le propre numéro d'Arthur ou un proche consentant).

### Piste 2 — Réception par Raccourcis + ANCS (plan B pour recevoir)
- **Automation Raccourcis « Message »** (reçu de n'importe qui, « Exécuter immédiatement ») → `POST /v1/messages` avec expéditeur, texte, date. Depuis iOS 17, les automatisations peuvent s'exécuter immédiatement ; des utilisateurs confirment que c'est le cas pour « Message » sous iOS 18 **[À VÉRIFIER sur l'iPhone d'Arthur]** (la doc Apple d'iOS 15 disait le contraire).
- **ANCS** : les messages arrivent comme notifications `com.apple.MobileSMS` (expéditeur + aperçu).

### Piste 3 — Envoyer sans MAP : le « relais » (plan B pour répondre)
Le PC ne peut pas déclencher un Raccourci sur l'iPhone à distance. Plusieurs stratagèmes, du plus automatique au plus manuel :
1. **Relais par Raccourci + déclencheur** : les réponses sont mises en file dans l'outbox. Un Raccourci « Envoyer les réponses du PC » (`GET /outbox`, boucle, action **Envoyer un message** sans afficher la fiche) se lance par un **déclencheur automatique** (connexion du chargeur, arrivée à la maison via Wi-Fi, heure…) **ou en un geste** : Toucher le dos, bouton du Centre de contrôle, widget. **[À VÉRIFIER]** : l'envoi de SMS en Raccourci peut demander une confirmation la première fois ou selon la version d'iOS.
2. **Déclencheur par e-mail** (hack optionnel) : le PC envoie un e-mail à une adresse que l'iPhone relève en *push* ; une automation « Quand je reçois un e-mail de… » lance le Raccourci d'envoi. Nécessite un compte e-mail dédié et un mot de passe d'application ; à n'activer qu'avec l'accord explicite d'Arthur (donnée sensible).
3. **Réponse à confirmer en un tap** : l'iPhone affiche une notification « 1 réponse prête pour Maman » ; un tap ouvre Messages avec le texte pré-rempli (schéma `sms:<numéro>&body=<texte>`), il ne reste qu'à appuyer sur Envoyer. Fonctionne toujours, mais pas 100 % automatique. Peut utiliser une notification locale (app ouverte) ou le push optionnel de la section 9.

### Règles d'interface de réponse
- Dans l'app : composeur en pastille (comme Messages), envoi par Entrée, états *envoi… / envoyé / échec*.
- Dans Windows : la **notification affiche un champ « Répondre »** et un bouton Envoyer (champ de saisie des notifications Windows). La réponse part par la piste disponible la plus automatique ; l'état réel (envoyé ou en attente de relais) est toujours affiché honnêtement.
- Les messages envoyés **depuis l'iPhone** peuvent ne pas apparaître : l'interface l'indique (« Les messages envoyés depuis l'iPhone n'apparaissent pas ici »), et les réponses envoyées depuis le PC sont stockées localement pour compléter le fil.
- Bleu = iMessage, vert = SMS. Un destinataire non iMessage reçoit un SMS (comportement de l'iPhone).

### Limites connues (à afficher dans l'app, pas à cacher)
Texte uniquement au début (pas d'images/vidéos/vocaux) • groupes à vérifier **[À VÉRIFIER]** • les messages arrivés hors de portée Bluetooth peuvent ne pas être synchronisés • pas de « en train d'écrire » ni d'accusés de réception.

### Confidentialité
Messages stockés en local uniquement, option « ne pas conserver l'historique », jamais de contenu dans les logs, option Windows Hello (C8).

---

## 7. Presse-papiers dans les deux sens

### Ce qu'iOS permet
Aucun déclencheur « le presse-papiers a changé » n'existe, et une app ne lit pas le presse-papiers en arrière-plan. La synchro est donc **déclenchée par un geste léger** côté iPhone, **automatique** côté PC.

### iPhone → PC (copier sur l'iPhone, coller sur le PC)
Gestes proposés (au choix d'Arthur, tous branchés sur la même action « Envoyer le presse-papiers au PC ») :
1. **Toucher le dos** (Réglages → Accessibilité → Toucher → Toucher le dos) : double-tap lance le Raccourci.
2. Bouton dans le **Centre de contrôle** (iOS 18) ou **widget interactif** (iOS 17+).
3. **Feuille de partage** sur du texte/lien/image sélectionné.
4. L'app, quand elle est ouverte, **envoie automatiquement** ce qu'elle trouve (iOS peut afficher « Autoriser à coller » : à gérer proprement).
Sur le PC : le contenu est **placé directement dans le presse-papiers Windows**, avec une notification discrète « Copié depuis l'iPhone » et un bouton Annuler.

### PC → iPhone (copier sur le PC, coller sur l'iPhone)
- Le PC publie le contenu dans l'outbox (option **automatique** à chaque copie, ou raccourci clavier global, ex. `Ctrl+Alt+P`, ou clic droit sur l'icône).
- L'iPhone l'applique automatiquement quand l'**app est au premier plan**, ou via **Toucher le dos (triple)** / widget / Centre de contrôle : « Récupérer le presse-papiers du PC » (Raccourci qui fait `GET /v1/clipboard` puis `Copier dans le presse-papiers`).
- Types : texte, URL, images, fichiers (les fichiers passent par F2).

### Garde-fous
Types sensibles (OTP, mots de passe détectés, contenus marqués confidentiels) exclus de l'historique ; option « synchro manuelle uniquement » ; taille max ; historique visible et effaçable.

---

## 8. Autres comportements

- **OTP (F8)** : regex pour 4 à 8 chiffres + mots-clés (« code », « vérification », « verification », « 2FA », « OTP »), détectés dans messages et notifications. Copie auto, notification « Code 482 193 copié », effacement après 60 s, exclusion de l'historique Win+V et du presse-papiers cloud.
- **Liens (F7)** : iPhone→PC via feuille de partage ; ouverture dans le navigateur par défaut (option : confirmation). PC→iPhone via clic droit ou extension navigateur (stretch) qui ajoute à l'outbox ; l'iPhone l'ouvre via notification ou widget.
- **Appels (C1)** : notification ANCS `incomingCall` → toast « Refuser / Répondre » (actions ANCS). L'audio reste sur l'iPhone.
- **Batterie (C2)** : sans app en arrière-plan, via **Automations Raccourcis** (déclencheurs *niveau de batterie*, *chargeur connecté/déconnecté*, *heure*) qui font `POST /v1/battery`, plus mise à jour quand l'app est ouverte. Le niveau affiché porte l'heure de dernière mise à jour.
- **Notifications (F3)** : regroupées par app, icônes génériques par catégorie + lettre colorée (pas de copie des logos d'apps tierces), action « ignorer » qui envoie l'action négative ANCS.

---

## 9. Sécurité et vie privée (non négociable)

- **Aucune donnée ne quitte le réseau local ni le lien Bluetooth.** Pas de télémétrie.
- TLS 1.2+ avec certificat épinglé ; jeton par appareil et révocable ; secret d'appairage à usage unique avec expiration ; limitation de débit sur `/pair` et sur les échecs d'authentification.
- Serveur lié aux interfaces privées uniquement ; règle **Pare-feu Windows** limitée au profil « Privé » ; refus des IP publiques.
- Fichiers entrants : aucun exécutable lancé automatiquement, noms assainis, quota disque.
- Données sensibles (messages, OTP) : option sans historique, exclusion de l'historique du presse-papiers, aucun contenu dans les logs, **Windows Hello** en option.
- Le canal HTTP clair des Raccourcis est **désactivable** et signalé dans l'interface.
- Pas de secret en dur dans le dépôt ; `.gitignore` pour certificats, jetons, bases SQLite, IPA signées.
- **Option cloud explicite et désactivée par défaut** : notification « ping » via un serveur `ntfy` pour réveiller l'iPhone (utile pour la piste 3 de la section 6). Elle fait passer un message minimal (« réponse prête ») hors du réseau local : Arthur doit l'activer en connaissance de cause.

---

## 10. Design

### 10.1 Principes (les deux côtés)
Minimaliste, calme, propre. On n'affiche que ce qui sert. Beaucoup d'espace, typographie claire, peu de couleurs, une seule couleur d'accent. Chaque écran a un état vide soigné, un état « hors de portée / déconnecté » et un état d'erreur lisible en français. Animations courtes (150 à 250 ms, courbe douce), désactivées si « réduire les animations » est actif.

### 10.2 Windows : une app qui ressemble à une app Mac
**Fenêtre**
- Sans bordure système, coins arrondis (≈ 12 px), **fond Mica** (matériau translucide de Windows 11) pour la barre latérale, contenu sur fond uni.
- Option « Style de fenêtre : macOS » (par défaut) avec **trois boutons ronds en haut à gauche** (fermer / réduire / agrandir) ; option « Windows standard » avec boutons à droite pour qui préfère. Penser à préserver le glisser de la fenêtre, le double-clic de la barre, et la disposition Snap Layouts autant que possible.
- Barre d'outils unifiée en haut (titre de la section à gauche, actions et recherche à droite), sans trait de séparation dur.

**Disposition (inspirée de Messages / Notes sur Mac)**
- **Barre latérale** gauche (≈ 220 px, translucide) : *Messages, Notifications, Fichiers, Presse-papiers, Appels, iPhone*, puis *Réglages* en bas. Élément actif surligné par une pastille arrondie, icônes fines.
- **Messages** : colonne de conversations (avatar rond, nom, aperçu, heure, point bleu non lu), puis fil de discussion avec **bulles** (bleu = iMessage, vert = SMS, gris = reçu), composeur en pastille avec bouton d'envoi rond, bouton « Nouveau message » en haut, recherche arrondie.
- **Notifications** : liste groupée par app, bandeaux discrets, actions au survol.
- **Fichiers** : zone de dépôt (« Glissez un fichier ici »), transferts récents avec barre de progression fine, bouton « Afficher dans le dossier ».
- **Presse-papiers** : liste des éléments récents, aperçu, copier en un clic, indicateur « de l'iPhone / du PC ».
- **iPhone** : carte d'état (connecté, Bluetooth, Wi-Fi), **jauge de batterie** arrondie, derniers appareils, bouton de ré-appairage.
- **Menu rapide (équivalent de la barre de menus Mac)** : un clic sur l'icône de la zone de notification ouvre un **petit panneau arrondi** (batterie, statut, dernier presse-papiers, zone de dépôt, « Ouvrir PhoneBridge »).
- **Recherche globale** `Ctrl+K` (palette façon Spotlight).

**Système visuel (jetons à mettre en variables CSS)**
- Thèmes **clair et sombre** qui suivent Windows.
- Accent bleu type iOS : clair `#007AFF`, sombre `#0A84FF` ; vert SMS `#34C759` ; rouge `#FF3B30` ; gris système neutres (échelle de 8 niveaux).
- Rayons : 6 / 10 / 14 px ; espacements sur une grille de 4/8 px ; ombres très douces ; séparateurs 1 px à faible contraste.
- Typographie : **Inter** (licence libre) avec repli sur `Segoe UI Variable`, tailles 12/13/15/17/22/28, graisses 400/500/600. Pas de **SF Pro** : sa licence en limite l'usage aux plateformes Apple.
- Icônes : **Lucide** ou **Phosphor** (licences libres), trait fin 1,5 px. Pas de **SF Symbols**.
- Contrôles : interrupteurs façon iOS, contrôles segmentés, listes « groupées » à coins arrondis pour les Réglages.
- Accessibilité : contraste conforme, navigation complète au clavier, focus visible, mise à l'échelle Windows 100 à 200 %, lecteur d'écran (étiquettes ARIA).

**Notifications Windows**
- Par défaut : **toasts natifs** (fiables, respectent l'assistant de concentration) avec champ de réponse et boutons.
- Option : **bannières maison** en haut à droite, arrondies et floutées façon macOS. À ne faire qu'après le reste ; le comportement natif reste le repli.

**Première utilisation (assistant en 4 écrans)** : 1) Appairer l'iPhone en Bluetooth dans Windows, 2) Activer les options de partage sur l'iPhone (captures décrites), 3) Scanner le QR code, 4) Test (envoyer un message d'essai / un fichier). Chaque écran vérifie l'étape et propose de l'aide si ça échoue.

### 10.3 iPhone : une app centrale légère + des « petits stratagèmes » partout
L'idée d'Arthur : **le plus possible se passe sans ouvrir l'app**, via les endroits où iOS laisse s'accrocher.

| Où | Ce que ça fait |
|---|---|
| **Feuille de partage** | « Partager → *PC d'Arthur* » pour fichiers, photos, texte, liens, comme pour un contact. Placée dans les favoris de la feuille |
| **Raccourcis / Siri / Spotlight** (App Intents) | Actions « Envoyer le presse-papiers au PC », « Récupérer le presse-papiers du PC », « Envoyer au PC », « Envoyer les réponses du PC » |
| **Toucher le dos** | Double-tap : presse-papiers → PC ; triple-tap : presse-papiers du PC → iPhone |
| **Centre de contrôle** (iOS 18) et **widgets interactifs** (iOS 17+) | Boutons « Envoyer au PC / Récupérer du PC », statut de connexion, batterie du PC (stretch) |
| **Automations Raccourcis** | Message reçu → PC ; batterie et charge → PC ; réponses en attente → envoi |
| **Notifications locales** | « Fichier reçu du PC », « Réponse prête à envoyer » |
| **Actions rapides** (appui long sur l'icône) | Envoyer le presse-papiers, ouvrir les réglages |
| **Live Activity** (stretch) | Progression d'un gros transfert |

**L'app elle-même** (SwiftUI natif, donc elle ressemble automatiquement à une app Apple) :
- Un seul écran principal minimal : grande carte d'état (connecté à *PC d'Arthur*, Bluetooth ok, derniers transferts), liste « Reçu du PC » avec bouton Télécharger/Enregistrer dans Fichiers ou Photos, historique récent.
- *Réglages* en liste groupée : fonctions activées, appareil appairé, canal HTTP des Raccourcis, vie privée.
- *Guide d'installation* des Raccourcis et des options Bluetooth, pas à pas, avec liens d'ouverture directe.
- Utilise les composants système (listes, interrupteurs, feuilles), **SF Symbols autorisés ici**, Dynamic Type, mode sombre, VoiceOver.

### 10.4 Cohérence
Mêmes noms, mêmes verbes et mêmes couleurs des deux côtés. Les textes sont dans des fichiers de ressources (français d'abord, prêt pour l'anglais).

### 10.5 Livrables design pour Claude Code
Avant de construire les écrans : un fichier `docs/design-tokens.md` (couleurs, rayons, typo, ombres) et une page HTML statique `design/preview.html` montrant la barre latérale, un fil de messages, la zone fichiers et le menu rapide, **à faire valider par Arthur** avant de brancher les données.

### 10.6 Droits et marques
Ne pas reprendre de logo, d'icône, de police ni de capture d'écran d'Apple (pas de logo pomme, pas de SF Pro/SF Symbols côté Windows, pas d'icônes d'apps Apple). On s'inspire du **style**, pas des assets. Le nom du projet ne doit pas contenir de marque d'Apple (« iMessage », « AirDrop », « Mac »…).

---

## 11. Build et installation iOS sans Mac

1. **GitHub Actions**, runner `macos-latest` : `brew install xcodegen && xcodegen`, puis `xcodebuild archive` avec `CODE_SIGNING_ALLOWED=NO`, puis empaquetage en `.ipa` non signée publiée en *artifact*. Quotas de minutes selon dépôt public ou privé **[À VÉRIFIER]**.
2. **Installation** depuis Windows avec **SideStore** (renouvellement ensuite sans PC) ou **AltStore + AltServer** (iTunes et iCloud téléchargés depuis le site d'Apple), signature avec l'Apple ID gratuit. Mode développeur activé sur l'iPhone.
3. **Limites du compte gratuit** : l'app expire au bout de **7 jours** (à rafraîchir), nombre d'apps sideloadées limité (environ 3), une extension compte comme un identifiant en plus **[À VÉRIFIER]**.
4. **AltStore PAL (marketplace UE) n'aide pas** pour une app personnelle non publiée.
5. Si le renouvellement devient pénible : programme développeur payant (TestFlight 90 jours) ou rester sur la **version sans app** (canal D + Bluetooth), qui n'expire pas.
6. Les extensions (partage, widget) peuvent être limitées par la signature gratuite (App Groups notamment). **Plan B** : chaque extension embarque son propre jeton ou passe par le canal HTTP.
7. `XcodeGen` (`project.yml`) évite d'éditer un `.xcodeproj` à la main depuis Windows.

---

## 12. Plan par phases

### Phase 0 — Mise en place
Dépôt, structure, CI Windows (`dotnet build` + `dotnet test`), `CLAUDE.md`. Demander à Arthur : version d'iOS, modèle de PC, version de Windows ; vérifier Bluetooth double mode (`Get-PnpDevice -Class Bluetooth`). **Critère** : build vert en CI.

### Phase 1 — Design + socle Wi-Fi, sans app iOS
- `design/preview.html` validé par Arthur ; hôte WPF + WebView2 + Mica ; coque de l'interface (barre latérale, sections vides soignées).
- Serveur (HTTP 47822 + HTTPS 47821), mDNS, tray, toasts, dossier de réception, SQLite.
- Endpoints `/files`, `/links`, `/clipboard`, `/messages`, `/battery`, `/status`.
- Raccourcis fournis (`shortcuts/` + `docs/shortcuts-guide.md`) : *Envoyer au PC*, *Lien sur le PC*, *Presse-papiers → PC*, *Presse-papiers du PC*, automation *Message reçu*, automations *Batterie*.
- **Critères** : photo de 10 Mo et vidéo de 500 Mo arrivent intactes (hash identique) ; un SMS reçu apparaît en toast < 5 s ; un code OTP est copié automatiquement ; un lien partagé depuis Safari s'ouvre sur le PC ; texte copié sur l'iPhone collé sur le PC via Toucher le dos ; requête sans jeton refusée.

### Phase 2 — SPIKE Bluetooth : ANCS (notifications)
`tools/spike-ancs` puis module `PhoneBridge.Bluetooth`. **Critère** : une notification WhatsApp ou Discord apparaît sur le PC avec app, titre et texte. Sinon : plan B documenté (Phone Link pour les notifs, ou Raccourcis).

### Phase 3 — SPIKE Bluetooth : MAP/PBAP (messages et réponses)
Client OBEX/MAP/PBAP (section 6, piste 1). **Critère** : liste des 10 derniers messages avec noms de contacts ; envoi d'un message de test depuis le PC ; réception en temps réel. Sinon : piste 3 (relais) en priorité.

### Phase 4 — Messagerie complète sur le PC
Vue Messages complète, toasts avec champ de réponse, états d'envoi, synchro lu/non-lu, réponses rapides, recherche. **Critère** : conversation de bout en bout avec un proche, réponses depuis la notification Windows.

### Phase 5 — App iOS (SwiftUI) + extensions
Appairage QR, HTTPS épinglé, feuille de partage, réception PC→iPhone, App Intents, widgets/Centre de contrôle, WebSocket. CI iOS → IPA → SideStore/AltStore. **Critères** : appairage par QR en < 30 s ; envoi de 50 photos depuis la feuille de partage sans ouvrir l'app ; fichier PC→iPhone récupéré à l'ouverture ; presse-papiers PC→iPhone appliqué à l'ouverture de l'app.

### Phase 6 — Confort
Appels (C1), batterie dans la barre des tâches et alertes (C2), Windows Hello (C8), filtres de notifications (C6), historique et recherche globale (C4), bannières maison (option). L'interface des appels sera précisée par Arthur.

### Phase 7 — Stretch (si Arthur le demande)
Audio d'appel sur le PC (HFP), synchro Focus, extension navigateur « Envoyer à l'iPhone », Live Activity de transfert.

---

## 13. Risques et plans B

| # | Risque | Plan B |
|---|---|---|
| R1 | Windows n'obtient pas ANCS de l'iPhone (appairage, sollicitation, conflit avec Phone Link, adaptateur) | Clé USB Bluetooth 5.x double mode ; garder **Phone Link** pour les notifs ; notifications partielles via Raccourcis |
| R2 | Impossible d'écrire un client MAP/OBEX fiable sous Windows | Réception via Raccourcis/ANCS, **réponse par relais** (section 6, piste 3) |
| R3 | L'automation « Message » demande une confirmation | S'appuyer sur ANCS/MAP pour la réception |
| R4 | L'envoi de SMS en Raccourci demande une confirmation | Réponse « prête, 1 tap pour envoyer » |
| R5 | L'app expire tous les 7 jours | SideStore ; ou version sans app |
| R6 | Extensions/App Groups bloqués par la signature gratuite | Jeton dupliqué par extension, ou canal HTTP |
| R7 | Pare-feu Windows / Wi-Fi qui isole les clients | Partage de connexion de l'iPhone ; messages d'erreur clairs ; Bluetooth pour messages/notifs |
| R8 | iOS suspend l'app pendant un gros transfert | `URLSession` d'arrière-plan, reprise via `Range`, hash final |
| R9 | Apple change iOS (mise à jour) | Socle Wi-Fi + Raccourcis simple ; retester après chaque version |
| R10 | Le Bluetooth se coupe hors de portée | Statut clair dans l'interface ; resynchro à la reconnexion ; les messages arrivés hors portée peuvent manquer |

---

## 14. Tests

- **Unitaires** (.NET, xUnit) : assainissement des noms de fichiers, parsing ANCS (octets d'exemple), parsing bMessage/vCard/OBEX (jeux d'essai), détection d'OTP, appairage (expiration, usage unique), limitation de débit, file d'attente de réponses.
- **Intégration** : client de test qui envoie un fichier en streaming, vérifie le hash, reprise `Range` ; faux iPhone Bluetooth (simulateur de paquets) pour tester l'OBEX/MAP sans matériel.
- **Interface** : captures de référence (Playwright) clair/sombre pour les écrans principaux ; contraste et clavier.
- **Manuels guidés** (`docs/manual-tests.md`) sur le vrai iPhone 13 : chaque critère d'acceptation de phase.
- **iOS** : XCTest du client réseau et du parsing, exécutés en CI.
- Jamais de test qui contacte Internet.

---

## 15. Structure du dépôt

```
PhoneBridge/
├─ SPEC.md  CLAUDE.md  README.md
├─ docs/  protocol.md  openapi.yaml  shortcuts-guide.md  ancs-notes.md  map-notes.md
│         design-tokens.md  manual-tests.md  progress.md
├─ design/  preview.html            # maquette statique validée avant de coder l'UI
├─ windows/
│  ├─ PhoneBridge.sln
│  ├─ src/PhoneBridge.Core/         # modèles, protocole, SQLite
│  ├─ src/PhoneBridge.Server/       # Kestrel, mDNS, appairage, fichiers
│  ├─ src/PhoneBridge.Bluetooth/    # ANCS, OBEX, MAP, PBAP
│  ├─ src/PhoneBridge.Host/         # fenêtre, Mica, tray, toasts, pont WebView2
│  ├─ ui/                           # front Svelte + Vite
│  └─ tests/
├─ ios/  project.yml  PhoneBridge/  ShareExtension/  WidgetsExtension/  Tests/
├─ shortcuts/                        # exports .shortcut + descriptions
├─ tools/  spike-ancs/  spike-map/
└─ .github/workflows/  windows.yml  ios.yml
```

---

## 16. Conventions de travail

- Commits petits et clairs (anglais, impératif). Une branche par phase, relue par Arthur.
- Chaque fonction : code + test + page dans `docs/` + case cochée dans `docs/progress.md`.
- Logs par niveau, **sans contenu des messages**.
- Toute découverte Bluetooth/iOS va dans `docs/ancs-notes.md` ou `docs/map-notes.md`, échecs compris.
- Quand une décision d'Arthur est nécessaire (priorités, compromis de sécurité, style), s'arrêter et poser **une** question claire.

---

## 17. Références (recherches vérifiées)

- Linux : `zackb/tether` (continuité complète), `gabrielmeir53/iphonebridge` (MAP, PBAP, ANCS, HFP sans app iPhone, testé sur iPhone 16 Pro Max / iOS 26.5), **BlueFerry** (iMessage/SMS par Bluetooth sous Linux : MAP + PBAP + ANCS)
- Prototype Mac/Windows/iPhone : `aydinmrnv/ctrl` ; multiplateforme : `hardope/continuity`
- Windows + iPhone par Raccourcis : `yeyt97/AirDropPlus`, `spectreDeveloper/iOS-ShareEasy`, `msitt/SyncOTP`
- Transfert de fichiers éprouvé : `localsend/localsend`
- Bibliothèques ANCS : `robotastic/ble-ancs`, `ThatAdamM/ancs-js`, `k97/WearBridge`
- Phone Link pour iPhone : fonctionne par Bluetooth (messages, notifications, appels, contacts), avec les options « Afficher les notifications », « Synchroniser les contacts » à activer dans Réglages → Bluetooth ; limites : pas de groupes ni d'images, uniquement quand l'iPhone est à portée
- API Windows : `Windows.Devices.Bluetooth`, `Windows.Devices.Bluetooth.Rfcomm` (apps de bureau comprises), `GenericAttributeProfile`
- Sideload : AltStore, SideStore
- À consulter : spécifications Bluetooth SIG **MAP** (Message Access Profile) et **PBAP**, spécification Apple **ANCS**
