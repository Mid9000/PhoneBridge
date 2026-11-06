# Raccourcis iOS (PhoneBridge)

Ce dossier liste les raccourcis et automatisations de l'iPhone qui parlent au PC. Le mode d'emploi pas à pas est dans **`docs/shortcuts-guide.md`**.

Il n'y a **pas de fichier `.shortcut` exporté** ici. Chaque raccourci se crée à la main dans l'app Raccourcis, en suivant le guide. Raison : un fichier exporté contiendrait le jeton, et il ne faut jamais le partager.

Canal utilisé : **HTTP 47822** (non chiffré, réseau local seulement), en-tête `Authorization: Bearer <JETON>`. Routes détaillées dans `docs/protocol.md` et `docs/openapi.yaml`.

## Liste

| Nom dans Raccourcis | Type | Route PC | Section du guide |
|---|---|---|---|
| Envoyer au PC | Raccourci (feuille de partage) | `POST /v1/files` | 3 |
| Lien sur le PC | Raccourci (feuille de partage) | `POST /v1/links` | 4 |
| Presse-papiers → PC | Raccourci (Toucher le dos, widget) | `POST /v1/clipboard` | 5 |
| Presse-papiers du PC | Raccourci (Toucher le dos, widget) | `GET /v1/clipboard` | 6 |
| Message reçu | Automatisation « Message » | `POST /v1/messages` | 7 |
| Batterie : chargeur connecté / déconnecté / batterie basse | Automatisations | `POST /v1/battery` | 8 |
| Envoyer les réponses du PC | Raccourci (relais) | `GET /v1/outbox`, `DELETE /v1/outbox/{id}` | 9 |
| Toucher le dos | Réglage d'accessibilité | (lance les raccourcis ci-dessus) | 10 |

## Points à retenir

- Remplace `<IP>` (IP du PC) et `<JETON>` (jeton de l'iPhone) dans chaque raccourci.
- **Le jeton n'est pas encore affichable** dans l'app iOS. Point bloquant, voir le guide, section 1.
- Ne partage jamais un raccourci qui contient le jeton.
- Les mentions **[À VÉRIFIER]** du guide sont des comportements d'iOS non confirmés sur l'iPhone d'Arthur.
