# ANCS notes (SPIKE Phase 2)

État : **code prêt, non testé sur l'iPhone** (2026-11-06). Rien n'est validé sur matériel réel tant qu'Arthur n'a pas lancé `spike-ancs`.

## Checklist pour Arthur (1 page)

**Avant (iPhone)**
1. iPhone déverrouillé, Bluetooth activé, près du PC.
2. Si Phone Link (Lien avec Windows) est lié à l'iPhone : dans Phone Link > Paramètres > Mes appareils, **supprimer l'iPhone**. Sur l'iPhone : Réglages > Bluetooth > (i) à côté du PC > **Oublier cet appareil**.
3. Sur le PC : Paramètres > Bluetooth et appareils > **Ajouter un appareil** > Bluetooth > choisir l'iPhone, confirmer que le **même code** s'affiche des deux côtés, accepter sur l'iPhone.
4. Sur l'iPhone : Réglages > Bluetooth > (i) à côté du PC : activer **« Afficher les notifications »** / « Partager les notifications système » (libellé exact à noter), et accepter « Autoriser les notifications système » si iOS le demande. Si les options n'apparaissent pas : sortir et rouvrir (i) 2-3 fois.

**Lancer (PowerShell sur le PC)**
```
cd C:\Users\arthu\OneDrive\Documents\IBridge\tools
dotnet run --project spike-ancs -- --duration 120
```
Pendant l'écoute, s'envoyer un WhatsApp/Discord/SMS depuis un autre téléphone, et si possible se faire appeler.
Si le résultat dit `ANCS service visible: FAIL` ou `access denied`, relancer avec : `dotnet run --project spike-ancs -- --pair --duration 120` (accepter le code sur l'iPhone).
Variante à tenter en dernier : `--peripheral` (PC annoncé comme périphérique BLE connectable).

**À me renvoyer** : le fichier log indiqué en haut et en bas de la sortie (`%LOCALAPPDATA%\PhoneBridge\spike-logs\spike-ancs-....log`). Il ne contient **pas** le texte des notifications (sauf si `--verbose`, à ne pas envoyer). Plus : le libellé exact des options vues dans Réglages > Bluetooth > (i).

## Ce qui est fait (vérifié hors matériel)
- `windows/src/PhoneBridge.Bluetooth/Ancs/AncsProtocol.cs` : parsing Notification Source (8 octets), commandes GetNotificationAttributes / GetAppAttributes / PerformNotificationAction, réassemblage Data Source fragmenté. Tests avec fragments de 1 et 20 octets (MTU 23).
- `Ancs/AncsSession.cs` (`IAncsClient`) : une commande Control Point à la fois (les réponses Data Source n'ont pas d'identifiant de requête), cache des noms d'app, erreur 0xA2 (notif disparue) n'empêche pas les suivantes, événements `NotificationReceived`, `NotificationRemoved`, `IncomingCall`, `ConnectionStateChanged`. Testé contre un faux iPhone (`tests/PhoneBridge.Bluetooth.Tests/Fakes/FakeAncsIphone.cs`).
- `Platform/GattAncsTransport.cs` : WinRT GATT (Uncached), `GattSession.MaintainConnection`, abonnement Data Source **avant** Notification Source, écriture avec réponse. ATT 0x05/0x0F ou AccessDenied -> `PermissionDenied`.
- `Platform/AncsAdvertiser.cs` : publicité BLE avec sollicitation ANCS (AD 0x15, UUID en petit-boutiste) + option publicité connectable via `GattServiceProvider`.
- `tools/spike-ancs` : lancé sur ce PC sans iPhone : adaptateur MediaTek vu (Classic+LE, central+peripheral, offload, 31 octets max), « pas d'iPhone appairé » géré proprement, publicité de sollicitation acceptée par Windows.

## Vérification des identifiants [À VÉRIFIER] du SPEC §5
Source : Apple, *ANCS Specification* (developer.apple.com, archive CoreBluetooth, pages Specification + Appendix), consultée 2026-11-06.

| Élément | SPEC | Spec Apple | Statut |
|---|---|---|---|
| Service ANCS | 7905F431-B5CE-4E99-A40F-4B1E122D00D0 | idem | confirmé |
| Notification Source (notify) | 9FBF120D-6301-42D9-8C58-25E699A21DBD | idem | confirmé |
| Control Point (write with response) | 69D1D8F3-45E1-49A8-9821-9BBDFDAAD9D9 | idem | confirmé |
| Data Source (notify) | 22EAC6E9-24D6-4BB5-BE44-B36ACE7C7BFB | idem | confirmé |
| Notification Source 8 octets | EventID, Flags, CategoryID, CategoryCount, UID(4) | idem, petit-boutiste | confirmé |
| CommandID 0/1/2 | GetNotifAttr / GetAppAttr / PerformAction | idem | confirmé |
| EventFlags | - | Silent 1, Important 2, PreExisting 4, PositiveAction 8, NegativeAction 16 | confirmé |
| Catégories | - | 0 Other, 1 IncomingCall, 2 MissedCall, 3 Voicemail, 4 Social, 5 Schedule, 6 Email, 7 News, 8 Health, 9 Business, 10 Location, 11 Entertainment | confirmé |
| Attributs | - | 0 AppId, 1 Title*, 2 Subtitle*, 3 Message*, 4 MessageSize, 5 Date, 6 PosLabel, 7 NegLabel (* = longueur max 2 octets) | confirmé |
| ActionID | - | 0 positive, 1 negative | confirmé |
| Erreurs | - | 0xA0 unknown cmd, 0xA1 invalid cmd, 0xA2 invalid param, 0xA3 action failed | confirmé |
| AppIdentifier terminé par NUL | - | oui dans GetAppAttributes (requête et réponse) ; les valeurs d'attributs ne le sont pas | confirmé |
| Date | - | `yyyyMMdd'T'HHmmSS` (Apple écrit SS, en pratique secondes) | confirmé, lu comme `HHmmss` |
| Libellés iOS des options | « Afficher les notifications », « Partager les notifications du système » | iphonebridge : *Show System Notifications* ; BlueFerry : *Allow System Notifications* (popup) | **toujours À VÉRIFIER** sur l'iPhone d'Arthur (libellés FR) |

## Ce que disent les projets Linux (iOS quirks)
- **iphonebridge** (gabrielmeir53, archivé, renvoie vers BlueFerry) : ANCS exige un **vrai lien LE lié** (bond), qui ne se forme qu'à un appairage neuf. Testé OK seulement sur adaptateur **Intel** ; Realtek et la plupart des clés USB échouent (clés « legacy »). Dans son spike : la sollicitation ANCS publiée mais l'iPhone ne s'est **jamais connecté en LE** ; appairé en BR/EDR seulement. Les options de partage n'apparaissent dans iOS que si la classe d'appareil est **Mains libres (CoD 0x240408)** et qu'une pub BLE avec sollicitation ANCS tourne.
- **BlueFerry** (erikwb) : testé de bout en bout sur **MediaTek MT7922** (bonne nouvelle : PC d'Arthur = MediaTek). Appairage « connect-first » -> double lien (Classic + LE). Limites de texte : 128 o titre/sous-titre, 1024 o message (repris ici). Ignore les notifs `PreExisting` (nous les signalons avec un drapeau, l'Host choisira). Un lien LE périmé -> `LE Start Encryption` échoue (raison 0x08) : réappairer des deux côtés. iOS réutilise les UID de notification : une action doit viser la notif exacte. `com.apple.MobileSMS` = Messages ; pour un groupe nommé, le sous-titre est le nom du groupe.
- Ni l'un ni l'autre n'est sous Windows : sur Windows on **ne peut pas changer la classe d'appareil (CoD)** depuis une app. Phone Link y arrive pourtant avec des PC Windows ordinaires, donc la porte existe.

## Comment Windows expose ça
- `BluetoothLEDevice` + `GetGattServicesForUuidAsync(ANCS, Uncached)` : client GATT, l'OS gère la connexion LE quand l'iPhone (adresse aléatoire résolue grâce à l'IRK du bond) est à portée.
- Appairage Classic via Paramètres Windows : avec Secure Connections, Windows/iOS peuvent dériver la clé LE (CTKD) et créer une 2e entrée « BluetoothLE#… ». Le spike affiche si cette entrée LE existe.
- `BluetoothLEAdvertisementPublisher` : accepte la section 0x15 (testé ici). **Observé sur ce PC** : statut qui alterne `Waiting (ResourceInUse)` / `Started`, et la pub connectable `GattServiceProvider` passe `Aborted` quand les deux tournent. `CrossDeviceService` (service Windows « appareils connectés »/Phone Link) tourne sur ce PC et occupe probablement des créneaux de publicité.

## Points de rupture probables (sur le vrai iPhone) et plan B
1. **Pas d'entrée LE après l'appairage Windows** (bond Classic seul) -> `--pair` depuis le spike ; sinon oublier/réappairer en lançant le spike *avant* (la pub de sollicitation tourne pendant l'appairage, comme BlueFerry).
2. **ANCS invisible** : iOS ne publie ANCS qu'aux accessoires autorisés. Plan B dans l'ordre : (a) désactiver/fermer Phone Link et `CrossDeviceService`, retester ; (b) clé USB Bluetooth 5.x à puce Intel/MediaTek (iphonebridge : Intel OK) ; (c) garder Phone Link pour les notifs ; (d) notifications Messages par Raccourcis (SPEC §6 piste 2).
3. **Accès refusé (ATT 0x05/0x0F)** : bond LE incomplet -> réappairer.
4. **Conflit Phone Link** : il prend le même ANCS ; un seul consommateur stable. Le délier pendant le spike.
5. **Notifications pré-existantes en rafale** à la connexion : drapeau `IsPreExisting`, à filtrer côté Host.

## Journal des essais
- 2026-11-06 nuit, PC d'Arthur, sans iPhone : `spike-ancs` démarre, adaptateur MediaTek OK (Classic, LE, rôles central+périphérique, offload), pub de sollicitation acceptée (`Started` après `Waiting/ResourceInUse`), pas d'iPhone appairé -> message clair, code de sortie 3. Seul appareil appairé : souris LE « G309 ».
