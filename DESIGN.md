# GoalFueled — Visual & UX Redesign Spec

> Direction agreed 2026-06-11. Concept: **"Athletic instrument"** — the energy and
> typography of a training app (Strava/Nike) applied to the fuel metaphor, gamified
> through progress mechanics (weekly score, levels) rather than stickers. No emoji,
> no cartoon. This document is the source of truth for the redesign; implement in
> the phases listed at the bottom.

## 1. Brand foundation

### 1.1 Palette — "Evolved emerald + ember" (dark-first)

Dark is the composed-first theme; light and high-contrast are derived from the same
hues so the existing 3-theme system survives.

| Token | Dark (default) | Light | Role |
|---|---|---|---|
| `--bg` | `#0E1512` (Ink) | `#F7FAF8` (Chalk) | App background |
| `--surface` | `#1A2420` (Carbon) | `#FFFFFF` | Cards, sheets, tab bar |
| `--surface-2` | `#22302A` | `#EDF3EF` | Inset panels, chart plot area |
| `--text` | `#F2F7F4` (Chalk) | `#101814` (Ink) | Primary text |
| `--muted` | `#8FA39A` | `#5C7268` | Secondary text |
| `--primary` | `#22C55E` (Emerald) | `#0FA958` | Fuel, positive, progress, links |
| `--accent` | `#FF6B1A` (Ember) | `#F5540A` | Burn, energy, primary CTAs |
| `--danger` | `#E5484D` | `#D93934` | Over-budget, destructive |
| `--border` | `#2C3A33` | `#D8E6DF` | Hairline rules |

Rules:
- Ember is the CTA color (Log buttons, primary actions). Emerald is the *state*
  color (progress, on-track, fuel). Never both saturated in the same component.
- Red appears **only** for genuinely negative states (over budget, errors) — never
  as decoration.
- High-contrast theme: keep current WCAG-AAA approach, re-derived from these hues.

### 1.2 Typography — condensed display + clean body

- **Display:** Barlow Condensed (Google Fonts, free) — weights 600/800. Used for:
  all numerals ≥ 24px, screen titles, section labels (uppercase, +4% tracking).
  Always `font-variant-numeric: tabular-nums` for numbers.
- **Body/UI:** Inter (or `Inter, system-ui` fallback) — 400/600. Forms, labels,
  paragraphs, list rows.
- Scale: hero numeral 56–72px/800 condensed · stat numeral 28–36px/800 · screen
  title 24px/800 condensed uppercase · section label 13px/600 condensed uppercase
  muted · body 15px/400 · caption 13px/400 muted.
- Numbers are the typography. When in doubt, make the number bigger and the label
  smaller, uppercase, and muted.

### 1.3 Iconography — custom technical-monoline set (~14 icons)

Single-weight 2px stroke SVGs, geometric/ruler-drawn ("equipment, not characters"),
24×24 grid, `stroke="currentColor"`, filled variants for active tab states.
**All emoji removed from the UI, including motivational copy.**

Set: `today` (gauge with needle), `goals` (concentric target), `library`
(stacked plates/crates), `you` (helmet/profile), `log-plus` (bold + in ring),
`fuel` (gauge), `burn` (faceted flame), `weight` (plate + bar), `recipe`
(pan outline), `plan` (grid calendar), `ingredient` (hex container),
`level` (chevron rank bars), `score` (ring meter), `review` (flag/checkpoint).

Brand mark: replace the 🚀 emoji with a monoline rocket/gauge mark in emerald +
ember; wordmark set in Barlow Condensed 800.

## 2. Information architecture & navigation

### 2.1 Mobile — bottom tab bar + quick log

```
[ Today ]  [ Goals ]  [ (+) LOG ]  [ Library ]  [ You ]
```

- **Today** = merged Dashboard + The Log: the entire daily loop on one screen.
- **Goals** = goals list (progress-first) + weight trend chart + weekly reviews.
- **LOG (center, ember, raised)** opens a bottom sheet: `Fuel · Burn · Weight`
  → each a one-screen quick form. Logging anything = 1 tap + form.
- **Library** = Ingredients, Recipes, Meals, Plans (the pantry/setup work).
- **You** = profile, level & Fuel Points, theme, units, admin toggle, sign out.
- Hamburger drawer retired. Admin pages (Users, review queue) live under You →
  Admin when admin view is on.

### 2.2 Desktop (≥1024px)

Persistent left sidebar (same five destinations + admin), content area uses a
2-column grid on Today (gauge + goals left, timeline right) instead of one
centered phone column.

## 3. Screens

### 3.1 Today (the hero screen)

Top to bottom:
1. **Header row:** date, level chip (`LV3 CRUISE`), week-score chip.
2. **Hero fuel gauge:** large semicircular gauge, needle on net calories vs
   today's budget. Center: remaining kcal in 64px condensed (`620` / `KCAL LEFT`),
   or over-budget state in red. Replaces the three flat stat cards as the focal
   point; Fuel/Burn/Net become three compact stat blocks beneath it.
3. **Fuel/Burn/Net stat strip** (28px numerals, uppercase labels).
4. **Up next:** planned meals (when following a plan) as completable rows.
5. **Timeline:** today's logged entries; delete demoted to a swipe action /
   overflow menu instead of a red X on every row.
6. **Hourly chart** (actual vs expected) — collapsed by default behind "Detail".

### 3.2 Goals (progress-first)

- Goal cards lead the screen; "New goal" is a button that opens the form in a
  sheet — the form no longer sits above the content.
- Goal card: name + condensed % numeral + linear meter (no rings repeating the
  same emoji); status line is **conditional** copy (see §5), never repeated
  verbatim across cards.
- Weight trend chart moves here (with quick "log weight" linking to the LOG sheet).
- **Weekly review entry point:** last week's score card + "Review your week".

### 3.3 Weekly review ("Your week in fuel")

Generated Sunday (or first open after week end). Full-screen, swipeable sections:
week score (0–100 ring), logging consistency (days fully logged), net-fuel trend
vs target, weight delta, best day, Fuel Points earned + level progress. Ends with
one suggested focus for next week. Completing the review awards FP (§4).
Score inputs: logging consistency (50%), plan/budget adherence (30%), weight
logged on schedule (20%). Score reflects *engagement*, not deficit size.

### 3.4 You (level & profile)

Level header: rank icon, `LV4 BOOST`, FP progress bar to next level, lifetime FP.
Below: profile fields, units, theme, weight schedule, admin, sign out.

### 3.5 Library

Four entity sections with the new icons; existing list/form patterns restyled
with the new tokens. No structural change in this phase.

## 4. Gamification spec — Fuel Points & ranks

### 4.1 Level ladder (fuel/rocket metaphor)

| Level | Rank | Cumulative FP |
|---|---|---|
| 1 | IGNITION | 0 |
| 2 | LIFTOFF | 500 |
| 3 | CRUISE | 1,500 |
| 4 | BOOST | 3,500 |
| 5 | AFTERBURNER | 7,000 |
| 6 | ORBIT | 12,000 |
| 7+ | ESCAPE VELOCITY… | +7,000/level |

### 4.2 FP economy (behavior-only — safety rule)

XP rewards **behaviors, never outcomes**: no FP is ever tied to deficit size,
weight lost, or eating less. This keeps the system from rewarding under-eating.

- +10 log a fuel or burn entry (cap 50 FP/day from entries)
- +25 complete a full day (≥2 fuel entries logged)
- +50 log weight on schedule
- +150 complete the weekly review
- Weekly bonus: week score × 1 FP (max 100)

Level-ups get the app's **one** celebration moment: full-screen rank card
(monoline rank icon, condensed type, subtle ember particle sweep — 1.5s,
honors `prefers-reduced-motion`). No confetti emoji anywhere else.

## 5. Voice & motivational copy

- Tone: trainer, not cheerleader. Short, specific, factual-positive.
  `"620 kcal of fuel left. Dinner fits."` / `"3 of 3 logged. Full day — +25 FP."`
- Copy is **conditional and varied**: a status line never repeats verbatim on
  the same screen; messages reference the actual number/state that earned them.
- Uppercase condensed microcopy for labels (`NET KCAL · TODAY`), sentence case
  for coaching lines. No exclamation stacking, no emoji.

## 6. Component & chart restyle

- **Cards:** flatter — `--surface` on `--bg`, 1px border, 10px radius (slightly
  squarer than today), shadow only on raised elements (sheets, tab bar, FAB).
- **Buttons:** primary = ember fill; secondary = 1px border; destructive actions
  demoted to menus/swipes. Min touch target 44px.
- **Charts:** bars anchored to the zero baseline (no floating pills), deficit
  emerald / surplus ember, axis labels 11px condensed uppercase, plot area uses
  `--surface-2`; desktop charts capped at sensible heights inside the grid.
- **Meters:** linear progress meters with squared ends and a tick at 100%;
  ring meter reserved for the week score.
- Loading: skeleton shimmer blocks replace "Loading…" text.

## 7. Implementation phases

1. **Foundation:** fonts, color tokens (3 themes), base components (buttons,
   cards, meters, stat block), icon set, kill emoji.
2. **Navigation:** tab bar + LOG sheet (mobile), sidebar (desktop), route moves
   (Library grouping, You screen), retire drawer.
3. **Today screen:** merge Dashboard + Log, hero gauge, stat strip, timeline.
4. **Goals + Weight:** progress-first layout, form-in-sheet, trend chart.
5. **Gamification backend + UI:** FP ledger, levels, week score, weekly review
   screen, level-up moment. (New domain entities — remember the Cosmos rule:
   new value-type fields on persisted entities must be nullable.)
6. **Library restyle + polish:** skeletons, empty states, chart restyle sweep.
