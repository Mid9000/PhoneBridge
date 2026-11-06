# Design tokens (Windows UI)

Source of truth in code: `windows/ui/src/app.css` (`:root` CSS variables). This page explains them. SPEC §10.2.

Colors use CSS `light-dark(light, dark)`: they follow Windows (`prefers-color-scheme`) unless the user forces a theme (`<html data-theme="light|dark">`, set from Settings).

## Color

### Accent and system colors
| Token | Light | Dark | Use |
|---|---|---|---|
| `--accent` | `#007AFF` | `#0A84FF` | Focus rings, sidebar icons, selection, progress bars, unread dot |
| `--accent-fill` | `#0071E3` | `#0071E3` | Backgrounds under white text: iMessage bubbles, primary buttons, selected rows (4.7:1) |
| `--accent-text` | `#0068D6` | `#0A84FF` | Accent-colored text (links, URLs) (5.3:1 / 4.6:1) |
| `--accent-soft` | accent 12 % | accent 20 % | Tinted backgrounds (drop zone hover, tags) |
| `--green` | `#34C759` | `#30D158` | Toggles on, "connected" dots, charging |
| `--green-fill` | `#1F8236` | `#1F8236` | SMS bubbles, "Answer" button (white text 4.9:1) |
| `--red` / `--red-text` / `--red-fill` | `#FF3B30` / `#D70015` / `#D70015` | `#FF453A` / `#FF6961` / `#D70015` | Errors, missed calls, "Decline" |
| `--orange` / `--orange-text` | `#FF9500` / `#A15000` | `#FF9F0A` / `#FF9F0A` | "Away / partial" state, relay-queued messages |
| traffic lights | `#FF5F57` `#FEBC2E` `#28C840` | same | macOS-style window buttons (plain colored circles, no Apple asset) |

Why separate `*-fill` tokens: white 13 px text on `#007AFF` is 4.0:1 and on `#34C759` 2.2:1, below WCAG AA. The fills are a shade darker so bubbles and buttons stay readable; the spec colors stay for non-text accents.

### Neutral scale (8 steps, 1 = background side, 8 = text side)
| Token | Light | Dark |
|---|---|---|
| `--gray-1` | `#F5F5F7` | `#1C1C1E` |
| `--gray-2` | `#E8E8ED` | `#2C2C2E` |
| `--gray-3` | `#D2D2D7` | `#3A3A3C` |
| `--gray-4` | `#AEAEB2` | `#48484A` |
| `--gray-5` | `#8E8E93` | `#636366` |
| `--gray-6` | `#68686D` | `#98989D` |
| `--gray-7` | `#48484A` | `#C7C7CC` |
| `--gray-8` | `#1D1D1F` | `#F5F5F7` |

### Semantic surfaces
| Token | Use |
|---|---|
| `--bg` | Content area (white / `#1E1E1E`) |
| `--bg-sidebar` | Sidebar, translucent. With a Mica backdrop (`html.mica`) it becomes almost transparent |
| `--bg-elevated` | Cards (notifications, iPhone status) |
| `--bg-popover` | Quick menu, Ctrl+K palette, toasts (+ `backdrop-filter: blur`) |
| `--bg-group` | Grouped lists (Settings, transfers) |
| `--fill-1/2/3` | Hover / selected / pressed and control tracks (5 %, 7.5 %, 11 % black; 6 %, 10 %, 15 % white) |
| `--separator`, `--border` | 1 px low-contrast lines |
| `--text`, `--text-2`, `--text-3` | Primary, secondary (≥ 4.5:1), tertiary (placeholders, decorative icons only) |
| `--bubble-in` | Received bubble (`#E9E9EB` / `#3A3A3C`) |

## Typography
- Family: `--font` = **Inter** (variable, bundled from `@fontsource-variable/inter`, latin subset, OFL) → `Segoe UI Variable` → `Segoe UI` → `system-ui`. No SF Pro.
- Sizes: `--fs-12` `--fs-13` (body) `--fs-15` (titles) `--fs-17` (card titles, palette input) `--fs-22` `--fs-28`.
- Weights: `--fw-regular` 400, `--fw-medium` 500, `--fw-semibold` 600.

## Shape, spacing, elevation
- Radii: `--r-6` (buttons, rows), `--r-10` (groups, list rows), `--r-14` (cards, popovers), `--r-window` 12 px, `--r-pill`. Bubbles 18 px with 6 px "joined" corners.
- Spacing on a 4/8 grid: `--s-1` 4 … `--s-10` 40 px. Sidebar `--sidebar-w` 220 px, toolbar `--toolbar-h` 52 px.
- Shadows: `--shadow-1` (cards), `--shadow-2` (hover, call banner), `--shadow-pop` (popovers), `--shadow-window` (preview frame only).

## Motion
- `--dur-fast` 150 ms, `--dur` 200 ms, `--dur-slow` 250 ms, `--ease` = `cubic-bezier(.2, .8, .2, 1)`.
- `prefers-reduced-motion: reduce` sets all durations to 0 and disables animations.

## Icons
Lucide (`@lucide/svelte`, ISC license), stroke 1.5–1.75 px. No SF Symbols, no app logos: third-party apps get a colored letter tile (`Avatar square`) plus a generic category icon.

## Accessibility
Focus ring `--focus-ring` (3 px accent halo) on `:focus-visible`. All text pairs above checked ≥ 4.5:1 except decorative/placeholder text. Every icon-only button has an `aria-label`.
