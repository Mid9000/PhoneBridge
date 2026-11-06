# MAP / PBAP notes (SPIKE Phase 3)

État : **code prêt et testé contre un faux iPhone, non testé sur l'iPhone** (2026-11-06).

## Checklist pour Arthur (1 page)

**Avant**
1. Phone Link doit être **délié** (un seul client MAP à la fois, iOS refuse le second : « Connection refused ») : Phone Link > Paramètres > Mes appareils > supprimer l'iPhone, puis fermer Phone Link.
2. iPhone appairé au PC en Bluetooth (Paramètres Windows > Bluetooth et appareils > Ajouter un appareil), même code des deux côtés.
3. iPhone : Réglages > Bluetooth > (i) à côté du PC : activer **« Afficher les notifications de messages »** (*Show Message Notifications*, MAP) et **« Synchroniser les contacts »** (*Sync Contacts*, PBAP). Noter les libellés exacts. Absentes ? Sortir et rouvrir (i) plusieurs fois ; sinon lancer d'abord `spike-ancs` (sa pub BLE peut faire apparaître les options), puis revenir.
4. iPhone déverrouillé, près du PC.

**Lancer (PowerShell)**
```
cd C:\Users\arthu\OneDrive\Documents\IBridge\tools
dotnet run --project spike-map
```
Pendant les 60 s d'attente « événements temps réel », s'envoyer un SMS/iMessage depuis un autre téléphone.
Test d'envoi (critère du spike), à un numéro consentant (le tien) :
```
dotnet run --project spike-map -- --send +336XXXXXXXX --text "Test PhoneBridge"
```
(le programme demande confirmation `o/N` avant d'envoyer).

**À me renvoyer** : le fichier log (`%LOCALAPPDATA%\PhoneBridge\spike-logs\spike-map-....log`, chemin affiché en haut et en bas). Sans `--verbose`, il ne contient ni texte de message, ni nom, et les numéros sont masqués (`+33*******78`). Dire aussi si le message de test est arrivé, en bleu ou en vert.

## Ce qui est fait (vérifié hors matériel)
- **OBEX** (`Obex/`) : paquets, 4 encodages d'en-têtes, Connect/SetPath/Get/Put/Disconnect/Abort, GET multi-paquets (Continue), PUT découpé selon le max du pair, ConnectionId en tête de chaque opération, délai de réponse, paquets malformés -> `ObexProtocolException`. `ObexServer` (réassemblage PUT, GET découpé) sert au MNS et aux faux serveurs de test.
- **MAP** (`Map/`) : `MapClient` (`IMapClient`) Connect avec cible MAS + `MapSupportedFeatures`, navigation SetPath un niveau à la fois, listing de dossiers, `GetMessagesListing` (MaxListCount, SubjectLength), `GetMessage` (Attachment=0, Charset=UTF-8), **PushMessage** dans `telecom/msg` Name=`outbox`, `SetMessageStatus` (octet de remplissage 0x30), `SetNotificationRegistration`. Parseurs XML (DTD interdite, repli tolérant si XML cassé), bMessage parse/build (LENGTH exact, marqueurs `END:MSG` neutralisés, destinataire validé : numéro ou e-mail, rien qui casse la vCard), rapport d'événement MNS 1.0/1.1.
- **MNS** : `MnsServer` + `Platform/MnsServiceHost` (enregistrement SDP 0x1133 via `RfcommServiceProvider`, profil MAP 1.4). Un `NewMessage` est complété par le **listing** (ne change pas l'état lu, iOS met le texte dans `subject`) ; `GetMessage` seulement en repli (il marque lu).
- **PBAP** (`Pbap/`) : `PbapClient` (taille via MaxListCount=0, pull vCard 3.0 **sans** MaxListCount, offset 1 = saute la fiche propriétaire), parseur vCard 2.1/3.0 (pliage, quoted-printable, TYPE nus, `itemN.`, échappements, photo base64, historique d'appels `X-IRMC-CALL-DATETIME`).
- 65 tests unitaires/intégration (boucle locale TCP en guise de RFCOMM, faux MAS/PSE/iPhone MNS) : verts.
- `tools/spike-map` lancé sur ce PC : démarre, voit l'adaptateur, « pas d'iPhone appairé » géré proprement.

## Vérification des identifiants [À VÉRIFIER] du SPEC §6
| Élément | Valeur | Source | Statut |
|---|---|---|---|
| Classe MAS | 0x1132 (MNS 0x1133, profil MAP 0x1134) | Bluetooth SIG Assigned Numbers | confirmé |
| Cible OBEX MAS | BB582B40-420C-11DB-B0DE-0800200C9A66 | MAP 1.4 §6.3 ; identique dans BlueZ obexd et Android | confirmé |
| Cible OBEX MNS | BB582B41-420C-11DB-B0DE-0800200C9A66 | MAP 1.4 | confirmé |
| Classe PBAP | PSE 0x112F, PCE 0x112E, profil 0x1130 | Assigned Numbers | confirmé |
| Cible OBEX PBAP | 796135F0-F0C5-11D8-0966-0800200C9A66 | PBAP 1.2 ; BlueZ/Android | confirmé |
| Types MIME | `x-obex/folder-listing`, `x-bt/MAP-msg-listing`, `x-bt/message`, `x-bt/messageStatus`, `x-bt/MAP-NotificationRegistration`, `x-bt/MAP-event-report`, `x-bt/phonebook` | MAP 1.4 / PBAP 1.2 | confirmé |
| Tags app params MAP | MaxListCount 0x01 … SubjectLength 0x13, Charset 0x14, StatusIndicator 0x17, StatusValue 0x18, MapSupportedFeatures 0x29 | MAP 1.4 §6.3.1 (de mémoire, recoupé BlueZ) | probable, à confirmer sur l'iPhone |
| Groupes | non | BlueFerry : MAP ne donne ni ID de groupe ni membres | **pas de groupes via MAP** |

Les specs SIG (MAP 1.4, PBAP 1.2, IrOBEX 1.2) sont derrière une connexion sur bluetooth.com : valeurs recoupées avec BlueZ/Android/BlueFerry, pas lues dans le PDF.

## Particularités iOS connues (BlueFerry, iphonebridge, iOS 26.5-27)
- `Forbidden` (0x43) à la connexion = **option désactivée** sur l'iPhone, pas un problème d'appairage. Géré : état `PermissionDenied` + conseil.
- iOS n'expose que la **boîte de réception** ; dossier Envoyés vide ; messages envoyés depuis l'iPhone invisibles. Les événements manqués hors portée ne sont pas rejoués, mais le listing les contient.
- **iMessage et SMS ont tous deux `type=SMS_GSM`** : impossible de distinguer bleu/vert via MAP. L'envoi d'un bMessage `TYPE:SMS_GSM` vers un contact iMessage part en iMessage (bleu) chez iphonebridge.
- Le texte du message est dans l'attribut `subject` du listing (peut-être tronqué : SubjectLength max 255).
- Forme d'envoi acceptée : vCard expéditeur **vide** hors BENV, destinataire(s) dans BENV (`TEL:` ou `EMAIL:` pour un Apple ID), `CHARSET:UTF-8`. C'est ce que construit `BMessage.BuildForPush`.
- Une seule session MAP (Phone Link doit être délié). Après une fermeture de session, une reconnexion rapide peut renvoyer `Forbidden` : garder **une session longue** (le futur Host le fera).
- PBAP : `MaxListCount` dans un pull complet a donné des transferts vides chez BlueFerry -> on ne l'envoie pas. Historique d'appels : 100 entrées max par listing.
- Heures sans fuseau : interprétées en heure locale du PC.
- Linux a dû forcer la classe d'appareil « Mains libres » pour que les options apparaissent ; impossible depuis une app Windows. Phone Link prouve que l'iPhone accepte des PC Windows, mais on ignore ce qu'il fait exactement.

## Points de rupture probables et plan B
1. **Options absentes dans Réglages > Bluetooth > (i)** (classe d'appareil du PC non « mains libres ») -> essayer avec Phone Link lié une fois pour voir les options, puis le délier sans oublier l'appairage ; lancer `spike-ancs` pendant l'appairage. Sinon plan B réception (Raccourcis, SPEC §6 piste 2) + relais d'envoi (piste 3).
2. **MAS absent du SDP** -> idem, c'est le même verrou iOS.
3. **`Connection refused`** -> Phone Link tient la session : le délier/fermer.
4. **MAS seulement en OBEX sur L2CAP** (GoepL2capPsm 0x0200 dans le SDP sans RFCOMM utilisable) : Windows n'offre pas L2CAP aux apps. Le spike affiche les attributs SDP pour le voir. Plan B : piste 3 (relais).
5. **MNS : l'iPhone ne se connecte jamais** (Windows refuse un attribut SDP, ou Phone Link a déjà un MNS) -> le journal liste les attributs refusés ; repli : sondage du listing toutes les N secondes (simple à ajouter).
6. **XML iOS mal formé** -> repli tolérant déjà en place.
7. **Annulation de lecture WinRT** : si un `ReadAsync` sur socket RFCOMM ignore l'annulation, le délai de 30 s ne joue pas ; à observer.

## Journal des essais
- 2026-11-06 nuit, PC d'Arthur, sans iPhone : `spike-map` démarre, adaptateur MediaTek OK (radio On, Classic+LE), aucun iPhone appairé -> message clair et conseils, résumé écrit dans le log.
