# Tests manuels guidés (phases 1 à 5)

Ce document te guide, phase par phase, pour vérifier les critères d'acceptation de SPEC §12 sur **ton** PC et **ton** iPhone. Les tests sur l'iPhone sont à faire par toi. Les commandes PowerShell vérifient le PC.

Légende :
- **PC** : à faire dans PowerShell sur le PC.
- **iPhone** : à faire à la main sur l'iPhone.
- **[À VÉRIFIER]** : je ne peux pas confirmer le comportement d'iOS sans ton iPhone.

---

## 0. Règles avant de commencer

1. **Ne colle jamais le jeton ni le contenu des messages dans le chat.** Si une commande affiche une erreur qui contient le jeton, efface-la avant de me l'envoyer.
2. Les tests utilisent de **faux messages**. N'envoie des messages réels qu'à **ton propre numéro**, ou à quelqu'un qui est d'accord.
3. Un test marqué **KO** n'est pas grave. C'est une information utile. Note ce qui s'est passé.

### Préparation (PC, PowerShell)

Ouvre **PowerShell** (touche Windows, tape `powershell`, Entrée). Lance ces lignes **une fois** par fenêtre :

```powershell
# IP du PC : repère la ligne 192.168.x.x du Wi-Fi
Get-NetIPAddress -AddressFamily IPv4 | Select-Object InterfaceAlias, IPAddress

# Remplace l'IP par la tienne
$ip = '192.168.1.20'

# Jeton de l'iPhone : point bloquant, voir docs/shortcuts-guide.md section 1.
# Colle-le ici seulement, ne l'enregistre dans aucun fichier.
$env:PB_TOKEN = '<JETON>'
$h = @{ Authorization = "Bearer $env:PB_TOKEN" }

# Dossier de réception (à vérifier dans les Réglages du PC)
$dir = Join-Path $env:USERPROFILE 'Downloads\PhoneBridge'
```

Pour effacer le jeton de la session à la fin :

```powershell
Remove-Item Env:PB_TOKEN
```

### Contrôles de base (PC)

Vérifie que le serveur écoute et que le pare-feu est sur le profil Privé. Ces commandes ne changent rien.

```powershell
Get-NetTCPConnection -State Listen -LocalPort 47821,47822 | Select-Object LocalAddress, LocalPort
Get-NetFirewallRule -DisplayName 'PhoneBridge*' | Format-Table DisplayName, Enabled, Profile
```

Attendu : le port **47821** est en écoute. Le port **47822** l'est aussi si le Canal HTTP est activé. La règle du pare-feu indique `Private`.

---

## Phase 1 : socle Wi-Fi, sans app iOS

Source : SPEC §12 Phase 1. Matériel : PC + iPhone 13 sur le même Wi-Fi + raccourcis créés (`docs/shortcuts-guide.md`).

### 1.1 Requête sans jeton refusée (PC)

```powershell
try {
  Invoke-WebRequest -UseBasicParsing -Method Post -Uri "http://${ip}:47822/v1/messages" -ContentType 'application/json' -Body '{"sender":"Test","body":"Test"}'
} catch {
  "Code HTTP : " + $_.Exception.Response.StatusCode.value__
}
```

Attendu : `Code HTTP : 401`. Si tu relances plus de 10 fois en une minute, le PC répond 429 : attends 1 minute.

### 1.2 Statut sans jeton (PC)

```powershell
Invoke-RestMethod -Uri "http://${ip}:47822/v1/status"
```

Attendu : le nom du PC et la version. Rien d'autre (pas de détails sensibles).

### 1.3 Fichier de 10 Mo : contenu identique (PC)

Le serveur reçoit un fichier de test, calcule son empreinte et la compare à celle du PC.

```powershell
$test10 = Join-Path $env:TEMP 'pb-test-10mo.bin'
fsutil file createnew $test10 10485760
$sha = (Get-FileHash -Algorithm SHA256 $test10).Hash
curl.exe -sS -i -X POST "http://${ip}:47822/v1/files" -H "Authorization: Bearer $env:PB_TOKEN" -H "X-Filename: pb-test-10mo.bin" -H "X-Sha256: $sha" --data-binary "@$test10"
Get-ChildItem $dir -Filter 'pb-test-10mo*' | Get-FileHash -Algorithm SHA256
```

Attendu :
- La réponse contient `201 Created` et un `"sha256"` égal à `$sha`.
- Le hash du fichier reçu dans `$dir` est **le même** que `$sha`.

Nettoyage : supprime à la main les fichiers `pb-test-*` (Explorateur, Corbeille) et le fichier dans `%TEMP%`.

### 1.4 Vidéo de 500 Mo : contenu identique (PC)

Même test avec une taille plus grande. Prend quelques minutes.

```powershell
$test500 = Join-Path $env:TEMP 'pb-test-500mo.bin'
fsutil file createnew $test500 524288000
$sha500 = (Get-FileHash -Algorithm SHA256 $test500).Hash
curl.exe -sS -i -X POST "http://${ip}:47822/v1/files" -H "Authorization: Bearer $env:PB_TOKEN" -H "X-Filename: pb-test-500mo.bin" -H "X-Sha256: $sha500" --data-binary "@$test500"
Get-ChildItem $dir -Filter 'pb-test-500mo*' | Get-FileHash -Algorithm SHA256
```

Attendu : même logique que 1.3.

### 1.5 Vraie vidéo de 500 Mo depuis l'iPhone (iPhone + PC)

1. **iPhone** : Photos → une vidéo d'environ 500 Mo (Fichiers → Info pour voir la taille). Partager → **Envoyer au PC**.
2. **PC** : attends la notification « Fichier reçu ». Puis :

```powershell
Get-ChildItem $dir | Sort-Object LastWriteTime -Descending | Select-Object -First 3 Name, Length
```

Attendu : la vidéo est là, avec **la même taille en octets** que sur l'iPhone, et elle se lit. Le hash complet n'est comparé par le PC que si le raccourci envoie `X-Sha256` **[À VÉRIFIER : pas encore fait par le guide]**. Dis-le-moi si la taille ou la lecture ne correspond pas.

### 1.6 Photo de 10 Mo depuis l'iPhone (iPhone + PC)

Même test que 1.5 avec une photo de 10 Mo environ.

### 1.7 SMS reçu : toast en moins de 5 s

**Test simulé (PC)** :

```powershell
$sms = @{ sender = 'Test PhoneBridge'; body = 'Message de test, ignorez-le'; service = 'sms'; receivedAt = (Get-Date).ToUniversalTime().ToString('o') } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "http://${ip}:47822/v1/messages" -Headers $h -ContentType 'application/json' -Body $sms
```

Attendu : rien ne s'affiche dans PowerShell (204), et un toast apparaît sur le PC.

**Test réel (iPhone + chronomètre)** : demande à quelqu'un de t'envoyer un SMS, ou envoie-le-toi depuis un autre téléphone. Mesure le temps entre l'arrivée du SMS sur l'iPhone et le toast sur le PC. Attendu : **moins de 5 s**.

### 1.8 Code de vérification copié automatiquement (PC)

```powershell
$otp = @{ sender = 'Test'; body = 'Votre code de verification est 482193'; service = 'sms' } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "http://${ip}:47822/v1/messages" -Headers $h -ContentType 'application/json' -Body $otp
```

Attends 3 s, puis :

```powershell
Get-Clipboard
```

Attendu : `482193` (le code) est dans le presse-papiers, et le PC affiche « Code 482193 copié ». Le presse-papiers est effacé au bout de 60 s : si `Get-Clipboard` est vide, relance le test.

Puis fais le même test avec un vrai SMS contenant un code, si tu veux.

### 1.9 Lien partagé depuis Safari (iPhone + PC)

1. **iPhone** : Safari → ouvre une page → Partager → **Lien sur le PC**.

Attendu : le navigateur du PC s'ouvre sur la page.

Test simulé (PC) :

```powershell
$lien = @{ url = 'https://example.com' } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "http://${ip}:47822/v1/links" -Headers $h -ContentType 'application/json' -Body $lien
```

Attendu : la page `example.com` s'ouvre dans le navigateur du PC.

### 1.10 Texte copié sur l'iPhone, collé sur le PC (iPhone + PC)

1. **iPhone** : copie un mot (par exemple « bonjour test »).
2. **iPhone** : **Toucher le dos, deux fois**. Le raccourci « Presse-papiers → PC » doit tourner.
3. **PC** :

```powershell
Get-Clipboard
```

Attendu : `bonjour test` (ou le mot copié). **[À VÉRIFIER]** : iOS peut demander une confirmation ou refuser l'écriture en arrière-plan.

### Résultat Phase 1

Pour chaque test (1.1 à 1.10) : **OK** ou **KO**, plus le temps mesuré (test 1.7) et le message d'erreur s'il y en a un (sans contenu de message ni jeton).

---

## Phase 2 : SPIKE ANCS (notifications)

Source : SPEC §12 Phase 2. Critère : une notification WhatsApp ou Discord apparaît sur le PC avec **application, titre et texte**. Sinon, le plan B est documenté.

**Test (iPhone + PC)** :
1. Assure-toi que l'iPhone est appairé en Bluetooth avec le PC et que les options de partage des notifications sont activées (guide d'installation de l'app, section Bluetooth) **[À VÉRIFIER : libellés iOS]**.
2. Fais envoyer un message WhatsApp ou Discord à l'iPhone par quelqu'un qui est d'accord.
3. Regarde si le PC affiche la notification : nom de l'application, titre, texte.

Résultat à me renvoyer : **OK** (les trois champs visibles), **partiel** (lesquels manquent) ou **KO**, plus le nom de l'application testée et la version de Windows. Aucun contenu de message.

---

## Phase 3 : SPIKE MAP/PBAP (messages et réponses)

Source : SPEC §12 Phase 3. Critère : liste des 10 derniers messages avec noms de contacts ; envoi d'un message de test depuis le PC ; réception en temps réel. Sinon, piste 3 (relais) en priorité.

**Test (PC + iPhone)** :
1. Dans l'app PhoneBridge, ouvre la vue **Conversations**. Note si la liste des 10 derniers messages s'affiche, et si les noms des contacts apparaissent.
2. Envoie un message de test à **ton propre numéro** depuis le PC. Note si l'iPhone le reçoit.
3. Demande à quelqu'un (d'accord) de t'écrire. Note si le message arrive en temps réel sur le PC.

Résultat à me renvoyer : pour chaque point, **OK**, **partiel** ou **KO**, avec le message d'erreur affiché (sans contenu de message).

**[À VÉRIFIER]** : tant que le spike MAP n'est pas fait, ces tests peuvent échouer. C'est attendu.

---

## Phase 4 : messagerie complète sur le PC

Source : SPEC §12 Phase 4. Critère : conversation de bout en bout avec un proche, réponses depuis la notification Windows.

**Test (PC + iPhone + un proche d'accord)** :
1. Le proche t'écrit. La notification s'affiche sur le PC.
2. Réponds depuis le champ **Répondre** de la notification Windows.
3. Vérifie sur l'iPhone que le message est bien arrivé (ou, si la réponse est en file, lance « Envoyer les réponses du PC », `docs/shortcuts-guide.md` section 9).
4. Note l'état affiché sur le PC : **Envoyé**, **En attente du relais Raccourcis** ou **Échec de l'envoi**.

Résultat à me renvoyer : OK ou KO pour chaque étape, et l'état affiché.

---

## Phase 5 : app iOS (SwiftUI) et extensions

Source : SPEC §12 Phase 5. Prérequis : l'IPA est installée sur l'iPhone (SideStore ou AltStore, voir `docs/ios-install.md`). Attention, la CI iOS n'est pas encore verte : cette phase peut être bloquée avant.

### 5.1 Appairage par QR code en moins de 30 s (iPhone + chronomètre)

1. **PC** : PhoneBridge → **iPhone** → **Appairer un iPhone** (le QR s'affiche).
2. **iPhone** : ouvre l'app PhoneBridge, scanne le QR. Démarre le chronomètre au premier geste et arrête-le quand l'iPhone affiche « Connecté ».

Attendu : **moins de 30 s**. Note le temps exact.

Contrôle (PC) : le nouvel iPhone apparaît dans la liste des appareils appairés. Le serveur HTTPS répond sans jeton :

```powershell
curl.exe -k -s "https://${ip}:47821/v1/status"
```

Attendu : un JSON avec `name` et `version`. Le `-k` désactive la vérification du certificat : c'est acceptable seulement pour ce test sur ton réseau local.

### 5.2 Envoyer 50 photos depuis la feuille de partage sans ouvrir l'app (iPhone + PC)

1. **iPhone** : Photos → sélectionne **50 photos**. Partager → **Envoyer au PC**. N'ouvre **pas** l'app PhoneBridge.
2. **PC** : compte les fichiers reçus dans la dernière heure, et vérifie qu'il n'y a pas de doublon :

```powershell
$recent = Get-ChildItem $dir -File | Where-Object { $_.LastWriteTime -gt (Get-Date).AddHours(-1) }
"Fichiers recus dans la derniere heure : $($recent.Count)"
$recent | Get-FileHash -Algorithm SHA256 | Group-Object Hash | Where-Object Count -gt 1
```

Attendu : **50** fichiers, et **aucun groupe** affiché par la dernière ligne (aucun doublon). Note le résultat.

### 5.3 Fichier PC → iPhone récupéré à l'ouverture

1. **PC** : clic droit sur un petit fichier de test → **Envoyer à** → **iPhone** (ou la fonction équivalente de l'app).
2. **PC** : vérifie qu'il est dans la file :

```powershell
Invoke-RestMethod -Uri "http://${ip}:47822/v1/outbox" -Headers $h
```

Attendu : un élément `kind` = `file` avec le nom du fichier.

3. **iPhone** : ouvre l'app PhoneBridge. Le fichier apparaît dans **Reçu du PC**. Touche-le pour l'enregistrer.

Attendu : le fichier est récupéré à l'ouverture de l'app, sans action de ta part autre que l'ouverture.

### 5.4 Presse-papiers PC → iPhone appliqué à l'ouverture

1. **PC** :

```powershell
Set-Clipboard -Value "PhoneBridge test $(Get-Date -Format HH:mm:ss)"
```

2. **iPhone** : ouvre l'app PhoneBridge. Puis, dans une autre app (Notes), colle.

Attendu : le texte du PC est collé sur l'iPhone. Le réglage « Appliquer le presse-papiers du PC » doit être activé dans l'app (Réglages, section « À l'ouverture de l'app »). Si iOS demande « Autoriser à coller », dis-le-moi : c'est prévu dans l'app, mais à noter.

### 5.5 Canal HTTP désactivable (PC)

1. **PC** : PhoneBridge → Réglages → **Canal HTTP pour les Raccourcis** → désactive.
2. **PC** :

```powershell
Get-NetTCPConnection -State Listen -LocalPort 47822
```

Attendu : aucune ligne (le port est fermé). Les raccourcis ne marchent plus, c'est normal.

3. Réactive le canal, puis relance la commande : le port 47822 doit réapparaître.

### Résultat Phase 5

Pour chaque test (5.1 à 5.5) : **OK** ou **KO**, avec le temps (5.1), le nombre de fichiers (5.2), et la version iOS et Windows.

---

## Ce que tu me renvoies

Copie ce modèle, remplis-le, et envoie-le-moi dans le chat :

```
Version iOS : ...
Version Windows : ...
Phase 1 : 1.1 OK / 1.2 OK / 1.3 OK / 1.4 KO (raison) / ...
Phase 5 : 5.1 OK (25 s) / 5.2 OK (50 fichiers) / ...
Autres remarques : ...
```

Règles :
- **Jamais** le jeton, le contenu d'un message, un code de vérification, ni le nom de tes contacts.
- Donne seulement OK, KO ou partiel, le temps mesuré et le message d'erreur (sans contenu).
- Si une commande PowerShell échoue, copie l'erreur **après** avoir retiré tout ce qui ressemble à un jeton.
