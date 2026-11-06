# Installer l'app PhoneBridge sur l'iPhone depuis Windows

Pas de Mac, pas de compte payant : on récupère l'app compilée par GitHub, puis on l'installe avec **SideStore** (recommandé) ou **AltStore**, signée avec ton Apple ID gratuit.

> Les menus et noms exacts de SideStore/AltStore changent souvent. Si une étape ne correspond pas, suis la doc officielle : https://docs.sidestore.io et https://faq.altstore.io. Les points marqués **[À VÉRIFIER]** n'ont pas encore été testés sur ton iPhone.

## 0. Ce qu'il faut savoir avant
- **7 jours** : une app signée avec un Apple ID gratuit **expire au bout de 7 jours**. Il faut la « rafraîchir » avant (SideStore le fait depuis l'iPhone ; AltStore a besoin du PC allumé sur le même Wi-Fi). Si elle expire, elle ne s'ouvre plus, mais tes données restent : il suffit de la rafraîchir ou de la réinstaller.
- **3 apps maximum** actives en même temps (SideStore/AltStore compte pour 1), et **10 identifiants d'app par semaine**. PhoneBridge contient 2 extensions (feuille de partage + widgets) : elles peuvent compter comme des apps en plus **[À VÉRIFIER]**. Si SideStore/AltStore propose de retirer des extensions, garde **la feuille de partage** et retire les widgets.
- Le **canal Raccourcis + Bluetooth** marche sans l'app et n'expire jamais : si le renouvellement devient pénible, c'est le plan B.

## 1. Récupérer le fichier .ipa
1. Va sur la page GitHub du dépôt → onglet **Actions** → workflow **ios** → la dernière exécution verte.
2. En bas, section **Artifacts** : télécharge **PhoneBridge-unsigned-ipa** (un .zip).
3. Dézippe-le : tu obtiens `PhoneBridge-unsigned.ipa`. (L'artifact reste disponible 14 jours ; relance le workflow sinon : bouton **Run workflow**.)

## 2. Préparer l'iPhone
1. Branche l'iPhone au PC en USB, déverrouille-le et touche **Faire confiance** à ce PC.
2. Active le **mode développeur** : Réglages → Confidentialité et sécurité → **Mode développeur** → activer → l'iPhone redémarre → confirme **Activer**. (L'option n'apparaît parfois qu'après la première installation par SideStore/AltStore.)

## 3a. Méthode recommandée : SideStore
SideStore se rafraîchit tout seul depuis l'iPhone (pas besoin du PC chaque semaine).
1. Suis le guide d'installation officiel pour Windows : https://docs.sidestore.io (rubrique *Installation*). Il demande en général :
   - d'installer **iTunes** et **iCloud** depuis le site d'Apple (pas la version Microsoft Store) **[À VÉRIFIER selon le guide en vigueur]** ;
   - de générer un **fichier d'appairage** de l'iPhone avec l'outil indiqué par le guide ;
   - d'installer l'app VPN locale recommandée par SideStore sur l'iPhone (elle reste sur l'iPhone, rien ne sort sur Internet).
2. Connecte-toi dans SideStore avec ton Apple ID (idéalement un Apple ID secondaire dédié).
3. Sur l'iPhone : Réglages → Général → **VPN et gestion de l'appareil** → ton Apple ID → **Faire confiance**.
4. Copie `PhoneBridge-unsigned.ipa` sur l'iPhone (iCloud Drive, câble avec iTunes « Partage de fichiers », ou envoie-le-toi) et enregistre-le dans **Fichiers**.
5. Ouvre SideStore → **Mes apps** → **+** → choisis `PhoneBridge-unsigned.ipa` → attends la fin de l'installation.
6. Chaque semaine : SideStore → Mes apps → **Tout rafraîchir** (le VPN SideStore doit être activé).

## 3b. Alternative : AltStore + AltServer (PC Windows)
1. Installe **iTunes** et **iCloud** depuis le site d'Apple (versions « hors Microsoft Store »), puis **AltServer pour Windows** depuis https://altstore.io.
2. Lance AltServer (icône losange dans la zone de notification). iPhone branché : clic sur l'icône → **Install AltStore** → ton iPhone → Apple ID + mot de passe (saisis-les toi-même dans AltServer).
3. Sur l'iPhone : Réglages → Général → **VPN et gestion de l'appareil** → ton Apple ID → **Faire confiance**.
4. Dans iTunes, coche **Synchroniser avec cet iPhone en Wi-Fi** (pour rafraîchir sans câble).
5. Mets `PhoneBridge-unsigned.ipa` dans **Fichiers** sur l'iPhone, puis AltStore → **My Apps** → **+** → choisis le fichier.
6. Chaque semaine : PC allumé avec AltServer, iPhone sur le même Wi-Fi → AltStore → **Refresh All**.

## 4. Premier lancement
1. Ouvre PhoneBridge. iOS demande l'accès au **réseau local** : touche **Autoriser** (sinon l'app ne voit pas le PC ; réglable plus tard dans Réglages → PhoneBridge).
2. Sur le PC : PhoneBridge → iPhone → **Appairer** → un QR code s'affiche.
3. Dans l'app : autorise la **caméra**, scanne le QR code. C'est fait.
4. Feuille de partage : si l'app affiche **« Copier la configuration pour la feuille de partage »** dans Réglages, c'est que la signature gratuite n'a pas permis le partage automatique. Touche ce bouton, puis partage une photo vers **Envoyer au PC** et touche **Coller** (une seule fois).
5. Ensuite : Réglages de l'app → **Guide d'installation** pour Toucher le dos, les widgets et les automatisations Raccourcis.

## Dépannage
| Problème | Solution |
|---|---|
| « PC injoignable » | Même Wi-Fi ? PhoneBridge lancé sur le PC ? Pare-feu Windows en réseau **Privé** ? Accès au réseau local autorisé pour l'app ? |
| « Le certificat du PC ne correspond pas » | PhoneBridge a été réinstallé sur le PC : Réglages de l'app → **Ré-appairer**. |
| L'app ne s'ouvre plus | Les 7 jours sont passés : rafraîchis dans SideStore/AltStore. |
| « Impossible de vérifier l'app » | Réglages → Général → VPN et gestion de l'appareil → **Faire confiance**. |
| Limite d'apps atteinte | Désactive une autre app sideloadée, ou retire l'extension widgets à l'installation. |
