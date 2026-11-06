# PhoneBridge (dossier IBridge)

iPhone 13 <-> PC Windows 11, comportement "iPhone + Mac". **Cahier des charges complet : [SPEC.md](SPEC.md)** (source de vérité, à lire avant toute tâche non triviale). Avancement : [docs/progress.md](docs/progress.md).

## Règles clés (résumé, détails dans SPEC.md)
- Réponses à Arthur en **français**, courtes ; code, identifiants, commits en **anglais**.
- Tout en local : aucun cloud, aucune télémétrie, aucun secret dans le dépôt. Jamais d'argent, de compte payant, de jailbreak, d'asset Apple.
- Hors périmètre (ne pas implémenter ni proposer) : sauvegarde photos, miroir d'écran, pilotage du PC, webcam.
- **SPIKE** (ANCS, MAP/PBAP) : prototype minimal, dire honnêtement si ça marche, appliquer le plan B, tout consigner dans `docs/ancs-notes.md` / `docs/map-notes.md` (échecs inclus). Ne rien supposer de ce qui est **[À VÉRIFIER]**.
- Travail **phase par phase** (SPEC §12). Passage à la phase suivante seulement après validation d'Arthur sur son matériel.
- Chaque fonction = code + test + page docs + case cochée dans `docs/progress.md`. Logs sans contenu de messages.

## Autonomie (priorité d'Arthur : qu'il fasse le minimum)
- Décider seul sur tout ce qui est technique et réversible ; documenter dans `docs/decisions.md` (1 ligne : décision + raison).
- Ne déranger Arthur que pour : action physique (iPhone, appairage, installation), choix de goût/design, compromis de sécurité, dépense, téléchargement/installation de logiciel. Poser **une** question claire, avec une recommandation.
- Grouper les demandes à Arthur en une liste unique, ne pas les égrener.

## Routage des modèles (sous-agents dans `.claude/agents/`)
| Agent | Modèle | Usage |
|---|---|---|
| `coder` | Opus 5.5, effort élevé | Tout le code (C#, Swift, TS), spikes Bluetooth, debug, architecture |
| `scribe` | Haiku | Textes : docs, README, guides Raccourcis, libellés FR/EN, messages de commit, progress.md |
| `ops` | Sonnet | Config, CI, scripts, recherche web, revue, tests manuels guidés, tâches diverses |
Le fil principal orchestre et délègue ; il n'écrit pas de gros blocs de code lui-même.

## Structure
`windows/` (.NET 8 : Core, Server, Bluetooth, Host + `ui/` Svelte), `ios/` (SwiftUI + XcodeGen, build via GitHub Actions), `shortcuts/`, `tools/` (spikes), `design/`, `docs/`.

## Environnement (vérifié 2026-11-06)
Windows 11, PowerShell. Présents : git, Node 24, Python 3.14, **.NET 8 SDK** (`C:\Program Files\dotnet`, ajouter au PATH du shell si besoin). **Smart App Control est actif** : les builds **Release** non signés sont bloqués (exe/DLL) -> lancer en **Debug** (`windows\scripts\run-dev.ps1`). Ne jamais désactiver SAC sans décision d'Arthur (irréversible). Le Swift n'est jamais compilé localement : seule la CI macOS le valide.
