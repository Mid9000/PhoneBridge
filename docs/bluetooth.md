# Bluetooth dans l'hôte (canal B)

Réglages > Bluetooth (expérimental) > « Notifications, appels et messages par Bluetooth ». **Désactivé par défaut** tant que les spikes ANCS/MAP ne sont pas validés sur l'iPhone d'Arthur (voir `ancs-notes.md`, `map-notes.md`).

## Ce que ça fait (code : `windows/src/PhoneBridge.Bluetooth/BluetoothService.cs`)
- **ANCS** : chaque notification de l'iPhone est enregistrée (si « Conserver l'historique »), envoyée à l'UI et affichée en toast. Les notifications déjà présentes à la connexion sont listées sans toast. Les codes de vérification sont copiés (même règle que le Wi-Fi). À chaque nouvelle session ANCS la liste du PC est remplacée par celle de l'iPhone (miroir du centre de notifications).
- **Appels** : toast « Appel entrant » avec Répondre / Refuser (action ANCS positive / négative), carte « en cours » dans Appels.
- **MAP** : messages reçus (événements MNS) enregistrés, toast avec réponse intégrée. Les réponses partent par MAP (`PushMessage`) quand MAP est connecté et que le destinataire est un numéro ou un e-mail ; sinon file du relais Raccourcis (`queued`). « Envoyé » = remis à l'iPhone, pas « délivré ».
- **Lu / non lu** : ouvrir une conversation sur le PC marque ses messages lus sur l'iPhone ; un événement `ReadStatusChanged` de l'iPhone efface le compteur du PC **[À VÉRIFIER : iOS l'envoie-t-il ?]**.
- **PBAP** : contacts tirés une fois par session, gardés **en mémoire seulement**, pour afficher les noms au lieu des numéros (comparaison sur les 9 derniers chiffres).
- Messages Apple (`com.apple.MobileSMS`) reçus par ANCS : sans toast quand MAP livre déjà les messages (pas de doublon).

## Pannes : jamais de plantage
Aucun iPhone, Bluetooth coupé, accès refusé : l'app continue, `connection.bluetooth` passe à `unavailable` et `connection.bluetoothIssue` dit pourquoi (`noAdapter`, `notPaired`, `notificationsDenied`, `messagesDenied`, `unreachable`), affiché dans iPhone et Réglages. Nouvel essai toutes les 60 s. `GET /v1/status` renvoie le même état dans `features.bluetooth`.

## Vérifier sans iPhone
`PhoneBridge.exe --bluetooth-smoke` : un passage de connexion sur une base jetable, écrit `BLUETOOTH SMOKE OK: state=... issue=...` dans `logs\host.log`, code de sortie 0 sauf plantage. Tests : `windows/tests/PhoneBridge.Bluetooth.Tests/BluetoothServiceTests.cs` (faux iPhone ANCS, MAS, PSE).

## Limites connues
- Premier appareil appairé dont le nom contient « iPhone ».
- Les correspondances notification ANCS / message MAP non lus sont en mémoire : après un redémarrage, l'état lu des anciens messages n'est plus synchronisé.
- Rien n'a encore tourné avec un vrai iPhone.
