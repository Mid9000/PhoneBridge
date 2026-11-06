# Raccourcis iOS : guide pas à pas

Ce guide te montre comment créer à la main, dans l'app **Raccourcis** de l'iPhone, les raccourcis et automatisations de PhoneBridge. Ils parlent au PC par le canal HTTP **47822** (SPEC §4, `docs/protocol.md`).

Légende :
- **[À VÉRIFIER]** : je ne suis pas sûr du nom exact du bouton ou du comportement d'iOS. Vérifie sur ton iPhone et dis-le-moi si ça diffère.
- `<IP>` : l'adresse IP du PC (section 1).
- `<JETON>` : le jeton de l'iPhone (section 1, **point bloquant**).

Les noms de boutons peuvent varier selon la version d'iOS. Si tu ne trouves pas une action, tape son nom dans la barre de recherche « Ajouter une action ».

---

## 0. Avant de commencer

- Le PC est allumé, PhoneBridge est ouvert, et le **Canal HTTP pour les Raccourcis** est activé (Réglages du PC, section Réseau).
- L'iPhone est sur **le même Wi-Fi** que le PC. Pas de VPN, pas de données mobiles.
- Le réseau Wi-Fi du PC est en profil **Privé** (Windows : Paramètres → Réseau → Propriétés du Wi-Fi).

### Attention : ce canal n'est pas chiffré

Le port 47822 utilise **HTTP** (pas HTTPS). Les Raccourcis iOS refusent le HTTPS auto-signé (SPEC §4). Donc :
- Le trafic n'est **pas chiffré** sur le Wi-Fi. Utilise-le **uniquement chez toi**, sur ton réseau.
- Ne le mets jamais en place sur un Wi-Fi public.
- Le PC refuse les adresses publiques. C'est une protection, pas un chiffrement.
- Si tu perds ton iPhone ou si le jeton fuit : sur le PC, **Révoquer** cet iPhone.

### Attention : ne partage jamais un raccourci qui contient le jeton

Le jeton est écrit dans le raccourci. Si tu envoies ce raccourci à quelqu'un (« Partager le raccourci »), il envoie aussi le jeton. Ne fais pas ça.

---

## 1. Trouver l'IP du PC et le jeton

### L'adresse IP du PC

Sur le PC, ouvre **PowerShell** et tape :

```powershell
Get-NetIPAddress -AddressFamily IPv4 | Select-Object InterfaceAlias, IPAddress
```

Repère la ligne du Wi-Fi (souvent `Wi-Fi`) et une adresse comme `192.168.x.x`. C'est `<IP>`.

Tu peux aussi la voir sur l'iPhone : app **PhoneBridge** → **Réglages** → section **Appareil appairé** → ligne **Adresse** (ex. `192.168.1.20:47821`). Le nombre avant les deux-points est l'IP.

**Conseil** : si l'IP change, les raccourcis ne marchent plus. Demande à ton routeur de toujours donner la même IP au PC (« réservation DHCP », nom selon la marque **[À VÉRIFIER]**).

### Le jeton : point bloquant

Le jeton est donné à l'iPhone une seule fois, à l'appairage. Il est gardé dans le trousseau de l'iPhone. Le PC ne garde qu'une empreinte, donc il ne peut pas le réafficher.

App **PhoneBridge** → **Réglages** → section **Raccourcis** → **Copier le jeton** → confirme. Le jeton reste 2 minutes dans le presse-papiers, uniquement sur cet iPhone : colle-le tout de suite dans le raccourci. La même section affiche **Adresse pour les Raccourcis** (`http://<IP>:47822`) avec un bouton pour la copier.

Règles pour le jeton :
- Format : `Bearer <JETON>`, avec **un seul espace** après `Bearer`.
- Ne le colle jamais dans le chat ni dans un fichier du dépôt.

---

## 2. Vocabulaire des actions utiles

| Action Raccourcis (nom FR) | À quoi elle sert |
|---|---|
| **Obtenir le contenu de l'URL** | Envoie une requête HTTP (méthode, en-têtes, corps) et reçoit la réponse. |
| **Dictionnaire** | Construit le corps JSON (clés et valeurs). |
| **Obtenir la valeur du dictionnaire** | Lit une clé d'une réponse JSON. |
| **Répéter avec chaque élément** | Boucle sur une liste. |
| **Si** | Condition (ex. `kind` est `reply`). |
| **Obtenir le presse-papiers** | Lit le presse-papiers de l'iPhone. |
| **Copier dans le presse-papiers** | Met un texte dans le presse-papiers de l'iPhone. |
| **Envoyer un message** | Envoie un SMS/iMessage. |
| **Obtenir le nom** | Donne le nom d'un fichier. |
| **Encoder l'URL** | Encode un texte pour un en-tête (ex. nom de fichier avec accents) **[À VÉRIFIER nom exact]**. |
| **Formater la date** | Met une date au format ISO 8601 **[À VÉRIFIER]**. |

Dans **chaque** action « Obtenir le contenu de l'URL » qui appelle le PC, il faut :
- **URL** : `http://<IP>:47822/v1/...` (attention : `http`, pas `https`, et le port `47822`).
- **En-têtes** : ajoute `Authorization` = `Bearer <JETON>`.

---

## 3. Raccourci « Envoyer au PC » (fichiers et photos)

**Ce qu'il fait** : depuis la feuille de partage, envoie chaque fichier au dossier de réception du PC (`POST /v1/files`).

**Étapes**

1. Raccourcis → onglet **Mes raccourcis** → **+** → **Nouveau raccourci**.
2. Touche **Réglages** (icône en haut) → **Nom** : `Envoyer au PC` → **OK**.
3. Touche **Réglages** → active **Afficher dans la feuille de partage**. Types acceptés : **Images**, **Fichiers**, **Vidéos** **[À VÉRIFIER : nom des types]**. Ferme les réglages.
4. Touche **Ajouter une action** → cherche **Répéter avec chaque élément** → ajoute-la. Dans son champ d'entrée, choisis **Entrée du raccourci** (la variable magique).
5. Dans la boucle, **Ajouter une action** → **Obtenir le nom** → l'entrée est **Élément répété**.
6. **Ajouter une action** → **Encoder l'URL** → entrée : le **Nom** de l'étape 5.
7. **Ajouter une action** → **Obtenir le contenu de l'URL** :
   - **URL** : `http://<IP>:47822/v1/files`
   - **Méthode** : `POST`
   - **En-têtes** :
     - `Authorization` = `Bearer <JETON>`
     - `X-Filename` = la variable **Encodé** de l'étape 6
     - (optionnel) `X-Sha256` = empreinte SHA-256 du fichier en hexadécimal (action **Générer le hachage**, type SHA256 **[À VÉRIFIER nom exact]**) : le PC refuse le fichier (400) si elle ne correspond pas.
   - **Corps de la requête** : choisis **Fichier**, puis la variable **Élément répété** **[À VÉRIFIER : option « Fichier » du corps]**.
8. Fin de la boucle (automatique).
9. Ferme avec **OK**.

**Test** : Photos → une photo → **Partager** → **Envoyer au PC**. Le PC doit afficher une notification « Fichier reçu ». La photo est dans `Téléchargements\PhoneBridge\` **[À VÉRIFIER : dossier réel dans les Réglages du PC]**.

**Limites connues**
- Les très gros fichiers (vidéo 500 Mo) passent par la mémoire de Raccourcis. Si ça plante, dis-le-moi : on verra une autre piste. **[À VÉRIFIER]**
- iOS peut convertir les photos HEIC en JPEG avant l'envoi **[À VÉRIFIER]**.
- Un nom de fichier vide est refusé par le PC (400).

---

## 4. Raccourci « Lien sur le PC »

**Ce qu'il fait** : envoie une URL au PC, qui l'ouvre dans le navigateur (`POST /v1/links`). Le PC accepte seulement `http` et `https`.

**Étapes**

1. **+** → **Nouveau raccourci** → **Réglages** : nom `Lien sur le PC`. Active **Afficher dans la feuille de partage**, type **URL** (et **Texte** si tu veux).
2. **Ajouter une action** → **Obtenir les URL** (entrée : **Entrée du raccourci**) **[À VÉRIFIER nom exact]**.
3. **Ajouter une action** → **Obtenir le contenu de l'URL** :
   - **URL** : `http://<IP>:47822/v1/links`
   - **Méthode** : `POST`
   - **En-têtes** : `Authorization` = `Bearer <JETON>`
   - **Corps** : **JSON** → **Dictionnaire** :
     - clé `url` (type **Texte**) = la variable **URL** de l'étape 2.
4. **OK**.

**Test** : Safari → une page → **Partager** → **Lien sur le PC**. Le navigateur du PC s'ouvre sur la page.

**Limites** : une adresse qui n'est pas `http://` ou `https://` donne une erreur 400.

---

## 5. Raccourci « Presse-papiers → PC »

**Ce qu'il fait** : envoie le texte du presse-papiers de l'iPhone vers le presse-papiers du PC (`POST /v1/clipboard`).

**Étapes**

1. **+** → **Nouveau raccourci** → nom : `Presse-papiers → PC`.
2. **Ajouter une action** → **Obtenir le presse-papiers**.
3. **Ajouter une action** → **Obtenir le contenu de l'URL** :
   - **URL** : `http://<IP>:47822/v1/clipboard`
   - **Méthode** : `POST`
   - **En-têtes** : `Authorization` = `Bearer <JETON>`
   - **Corps** : **JSON** → **Dictionnaire** :
     - `kind` (**Texte**) = `text`
     - `text` (**Texte**) = la variable **Presse-papiers** de l'étape 2.
4. **OK**.

**Test** : copie un mot dans une app, lance le raccourci. Sur le PC, la commande `Get-Clipboard` (détaillée dans `docs/manual-tests.md`) doit afficher ce mot.

**Limites**
- Un presse-papiers vide ou une image donne une erreur 400 (le guide ne gère que le texte).
- Un code de vérification (OTP) est exclu de l'historique du PC et effacé après 60 s (SPEC §8).

---

## 6. Raccourci « Presse-papiers du PC »

**Ce qu'il fait** : lit le dernier texte copié sur le PC (`GET /v1/clipboard`) et le met dans le presse-papiers de l'iPhone.

**Étapes**

1. **+** → **Nouveau raccourci** → nom : `Presse-papiers du PC`.
2. **Ajouter une action** → **Obtenir le contenu de l'URL** :
   - **URL** : `http://<IP>:47822/v1/clipboard`
   - **Méthode** : `GET`
   - **En-têtes** : `Authorization` = `Bearer <JETON>`
3. **Ajouter une action** → **Obtenir la valeur du dictionnaire** → clé : `text`.
4. **Ajouter une action** → **Copier dans le presse-papiers** (entrée : la valeur de l'étape 3).
5. **OK**.

**Limites**
- Si le PC n'a encore rien copié, le serveur répond **204** (sans contenu). Le raccourci peut alors s'arrêter avec une erreur. C'est normal **[À VÉRIFIER]**.
- iOS peut refuser l'écriture dans le presse-papiers en arrière-plan. Si ça arrive, le texte n'arrive pas. Dis-le-moi **[À VÉRIFIER]**.

---

## 7. Automation « Message reçu » (POST /v1/messages)

**Ce qu'elle fait** : à chaque SMS ou iMessage reçu, envoie l'expéditeur, le texte et la date au PC. Le PC affiche un toast et détecte les codes (OTP).

**Étapes**

1. Raccourcis → onglet **Automatisation** → **+** → **Créer une automatisation personnelle**.
2. Choisis **Message**. Expéditeur : **Toute personne**. Touche **Suivant**.
3. **Ajouter une action** → **Obtenir le contenu de l'URL** :
   - **URL** : `http://<IP>:47822/v1/messages`
   - **Méthode** : `POST`
   - **En-têtes** : `Authorization` = `Bearer <JETON>`
   - **Corps** : **JSON** → **Dictionnaire** avec **exactement** ces quatre clés (orthographe et minuscules obligatoires) :

| Clé | Type | Valeur |
|---|---|---|
| `sender` | Texte | variable magique **Expéditeur** **[À VÉRIFIER nom exact]** |
| `body` | Texte | variable magique **Contenu** (ou **Message**) **[À VÉRIFIER nom exact]** |
| `service` | Texte | `sms` (fixe, voir la note) |
| `receivedAt` | Texte | variable **Date** passée par **Formater la date**, format **ISO 8601** **[À VÉRIFIER]** |

Le corps envoyé doit ressembler à :

```json
{
  "sender": "Maman",
  "body": "Votre code est 482193",
  "service": "sms",
  "receivedAt": "2026-11-06T18:30:00+01:00"
}
```

4. Touche **Suivant**. Désactive **Demander avant d'exécuter** et valide : l'automatisation devient **Exécuter immédiatement** **[À VÉRIFIER : sur ton iOS, ce réglage peut être différent]**.

**Note sur `service`** : Raccourcis ne dit pas toujours si le message est un iMessage ou un SMS. On met `sms` pour l'instant **[À VÉRIFIER]**.

**Test** : demande à quelqu'un de t'écrire (ou envoie-toi un SMS depuis un autre téléphone). Le PC doit afficher un toast en moins de 5 s (critère de la phase 1, SPEC §12).

**Limites**
- Si iOS demande une confirmation avant l'exécution, l'automatisation ne part pas toute seule **[À VÉRIFIER]**.
- Un message de plus de 20 000 caractères est refusé (400).

---

## 8. Automations « Batterie » (POST /v1/battery)

**Ce qu'elles font** : envoient le niveau de batterie au PC. Une automation ne tourne que quand son déclencheur arrive. Le PC affiche donc l'heure de la dernière mise à jour (SPEC §8).

Crée **trois** automatisations (onglet Automatisation → **+** → **Créer une automatisation personnelle**). Pour chacune, **Ajouter une action** → **Obtenir le contenu de l'URL** :
- **URL** : `http://<IP>:47822/v1/battery`
- **Méthode** : `POST`
- **En-têtes** : `Authorization` = `Bearer <JETON>`
- **Corps** : **JSON** → **Dictionnaire** :
  - `level` (**Nombre**) = la variable **Niveau de la batterie** **[À VÉRIFIER : action « Obtenir le niveau de la batterie », et le type Nombre]**.
  - `charging` (**Booléen**) = `Vrai` ou `Faux` selon l'automatisation **[À VÉRIFIER : type Booléen]**.
  - `source` (**Texte**) = `shortcut`.

| Automatisation | Déclencheur | `charging` |
|---|---|---|
| A : Chargeur connecté | **Chargeur** → **Est connecté** | `true` |
| B : Chargeur déconnecté | **Chargeur** → **Est déconnecté** | `false` |
| C : Batterie basse | **Niveau de la batterie** → **Est inférieur à** 20 % **[À VÉRIFIER]** | `false` |

Pour chaque automatisation : **Suivant** → désactive **Demander avant d'exécuter** → **Exécuter immédiatement**.

**Test** : branche le chargeur. Sur le PC, le niveau de batterie doit se mettre à jour (section « Batterie »).

---

## 9. Relais « Envoyer les réponses du PC »

**Ce qu'il fait** : récupère les réponses écrites sur le PC (`GET /v1/outbox`), les envoie une par une par SMS/iMessage, puis les retire de la file (`DELETE /v1/outbox/{id}`).

**Règle de sécurité** : la file peut contenir aussi des fichiers, du presse-papiers et des liens. Le raccourci **ne doit envoyer que** les éléments dont `kind` vaut `reply`. Sans cette condition, il effacerait des éléments sans les envoyer.

**Étapes**

1. **+** → **Nouveau raccourci** → nom : `Envoyer les réponses du PC`.
2. **Ajouter une action** → **Obtenir le contenu de l'URL** :
   - **URL** : `http://<IP>:47822/v1/outbox`
   - **Méthode** : `GET`
   - **En-têtes** : `Authorization` = `Bearer <JETON>`
3. **Ajouter une action** → **Obtenir la valeur du dictionnaire** → clé : `items` (le résultat est une liste).
4. **Ajouter une action** → **Répéter avec chaque élément** → entrée : `items`.
5. Dans la boucle, **Ajouter une action** → **Obtenir la valeur du dictionnaire** → clé `kind` (entrée : **Élément répété**).
6. Dans la boucle, **Ajouter une action** → **Si** → entrée : `kind` → **est** → `reply` (texte exact, en minuscules).
7. **Dans le Si** :
   a. **Obtenir la valeur du dictionnaire** → clé `to` (entrée : Élément répété).
   b. **Obtenir la valeur du dictionnaire** → clé `text` (entrée : Élément répété).
   c. **Envoyer un message** → **Destinataire** : `to`, **Texte** : `text`. Désactive l'option **Afficher lors de l'exécution** pour éviter l'écran de confirmation **[À VÉRIFIER nom exact]**.
   d. **Obtenir le contenu de l'URL** :
      - **URL** : `http://<IP>:47822/v1/outbox/` suivi de la variable **id** (clé `id` de l'élément répété). Ajoute une action **Obtenir la valeur du dictionnaire** clé `id` avant, et insère sa variable dans l'URL.
      - **Méthode** : `DELETE`
      - **En-têtes** : `Authorization` = `Bearer <JETON>`
8. **Fin du Si**, puis **Fin de la boucle** (automatique).

**Ordre important** : la suppression (étape 7d) vient **après** l'envoi. Si l'envoi échoue, le raccourci s'arrête et la réponse reste dans la file (rien n'est perdu).

**Risque connu** : si le message part mais que le PC ne répond pas au `DELETE`, la réponse sera renvoyée à la prochaine exécution (doublon possible). **[À VÉRIFIER]** : à surveiller dans les premiers tests.

**Déclencheurs proposés**
- Automatisation **Chargeur connecté** → **Exécuter immédiatement**.
- Siri : « Envoyer les réponses du PC avec PhoneBridge » (action de l'app, expliquée dans le guide de l'app).
- Ajouter ensuite une automatisation Wi-Fi « à la maison » **[À VÉRIFIER : type de déclencheur Wi-Fi]**.

**Test** : sur le PC, écris une réponse de test à **ton propre numéro** (jamais à un tiers sans accord). Lance le raccourci. Le message arrive ; sur le PC, la réponse disparaît de la file.

---

## 10. Toucher le dos (Back Tap)

**Ce qu'il fait** : lance un raccourci avec deux ou trois tapotements au dos de l'iPhone.

**Étapes**

1. Réglages → **Accessibilité** → **Toucher** → **Toucher le dos** **[À VÉRIFIER : libellés en français]**.
2. **Toucher deux fois** → choisis **Presse-papiers → PC**.
3. **Toucher trois fois** → choisis **Presse-papiers du PC**.

**Remarques**
- Un raccourci lancé par Toucher le dos peut demander une confirmation **[À VÉRIFIER]**.
- Si le triple tapotement déclenche aussi une fonction d'accessibilité, désactive cette fonction **[À VÉRIFIER]**.

---

## 11. Problèmes courants

| Ce que tu vois | Cause probable | Quoi faire |
|---|---|---|
| Erreur de connexion, rien ne répond | IP fausse, PC en veille, canal HTTP désactivé, mauvais Wi-Fi | Vérifie `<IP>`, le Wi-Fi et l'option du PC |
| 401 (non autorisé) | Jeton faux ou `Bearer` mal écrit | Recopie `Bearer <JETON>` avec un seul espace |
| 403 (refusé) | Adresse non privée (VPN, données mobiles) | Coupe le VPN, reste en Wi-Fi |
| 429 (trop de requêtes) | Trop d'erreurs d'authentification | Attends 1 minute |
| 400 (requête invalide) | Champ manquant ou mal écrit | Vérifie les noms des clés (`sender`, `body`…) |
| 404 | URL fausse | Vérifie `/v1/` et le chemin |
| 507 / 413 | Disque plein ou fichier trop gros | Libère de la place ou réduis le fichier |

Si une étape ne correspond pas à ton iPhone, note le nom exact du bouton et envoie-le-moi. Je mettrai à jour le guide.
