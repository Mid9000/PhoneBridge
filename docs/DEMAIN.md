# Demain matin — par où commencer (≈ 15 min)

État : le code des deux côtés est écrit. Windows : build Debug OK, 149 tests verts, l'hôte démarre. iPhone : Swift **jamais compilé** (pas de Mac), la CI GitHub sera le premier vrai test. Bluetooth : testé uniquement contre un faux iPhone.

## 1. Voir l'app Windows (2 min)
```powershell
$env:Path += ";C:\Program Files\dotnet"
cd C:\Users\arthu\OneDrive\Documents\IBridge\windows
.\scripts\run-dev.ps1
```
(Mode Debug exigé : Smart App Control bloque les builds Release non signés.) Regarde aussi `design/preview.html` (ouvre-le dans Chrome) : **dis-moi si le design te plaît ou quoi changer**.

## 2. Envoyer le projet sur GitHub pour compiler l'app iPhone (5 min, c'est toi qui dois le faire)
Crée un dépôt GitHub (privé conseillé) et pousse ce dossier. L'action `ios.yml` compile et produit l'IPA (artifact). Dis-moi si ça échoue : je corrige à partir des logs. Ensuite installation : `docs/ios-install.md` (SideStore/AltStore).

## 3. Tester sans l'app iPhone (Raccourcis)
`docs/shortcuts-guide.md` + `docs/manual-tests.md`.

## 4. Spikes Bluetooth (le plus risqué)
`docs/ancs-notes.md` et `docs/map-notes.md` : checklist en français, outils `tools/spike-ancs` et `tools/spike-map`. Renvoie-moi les logs.

## Décisions qui t'attendent
- Pare-feu : lancer `windows\scripts\firewall.ps1` en administrateur (réglage de sécurité, je ne l'ai pas fait).
- Smart App Control : le laisser actif (recommandé) et utiliser Debug, ou le désactiver (irréversible) pour une version Release.
- Jetons stockés en hash côté PC (écart assumé avec le cahier des charges, voir decisions.md).
- Version d'iOS de ton iPhone.
