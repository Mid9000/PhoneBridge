# Avancement PhoneBridge

## Phase 0 — Mise en place
- [x] Dépôt git, structure, `SPEC.md`, `CLAUDE.md`
- [x] Sous-agents routés par modèle (`.claude/agents/`)
- [x] Workflow CI Windows (`.github/workflows/windows.yml`, s'active quand `windows/PhoneBridge.sln` existe)
- [ ] **Installer .NET 8 SDK** (absent) — en attente de l'accord d'Arthur : `winget install Microsoft.DotNet.SDK.8`
- [ ] Infos matériel à récupérer (voir "Questions pour Arthur")
- [ ] Squelette solution .NET (Core, Server, Bluetooth, Host, tests) + build vert

## Questions pour Arthur (une seule fois, groupées)
1. OK pour installer le .NET 8 SDK via winget (≈ 300 Mo, source Microsoft) ?
2. Version d'iOS de l'iPhone 13 (Réglages → Général → Informations → Version iOS).
3. Je peux lancer moi-même `Get-PnpDevice -Class Bluetooth` et `winver` pour le PC (lecture seule) : OK ?
4. Un compte GitHub (gratuit) pour la CI iOS : as-tu déjà un compte ? Dépôt privé ou public ?

## Phases suivantes
Voir SPEC §12 : P1 Design + socle Wi-Fi • P2 SPIKE ANCS • P3 SPIKE MAP/PBAP • P4 Messagerie • P5 App iOS • P6 Confort • P7 Stretch.
