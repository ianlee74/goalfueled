# DONE TO-DO ITEMS

## Instructions

[AI Agent] add completed tasks to the top of the `Completed Items` section after they are completed. Add the date & time that the item was completed. Also, add any notes about configuration, assumptions, or decisions made in a `NOTES` section below the item.

## Completed Items

### Simplify the nutrition breakdown on meal plans — 2026-06-13 CDT

- [x] **Simplified the meal-plan nutrition breakdown**: replaced the stack of per-day full nutrition labels with a single compact macro matrix table.

**NOTES:**

- Before, on `PlanFormPage`, a weekly plan rendered a separate full `NutritionLabel` (every micronutrient row) for each active day, plus another for the weekly total — 8+ stacked tables of vertical scroll and repetition.
- Kept the **Daily nutrition** card (full micronutrient detail + goal comparison) unchanged so no information is lost.
- Replaced the per-day breakdown with a new `MacroMatrix` component: rows = each active day plus emphasized **Average / day** and **Weekly total** summary rows below a heavier rule; columns = kcal, Protein, Carbs, Fat. Calories bold (focal number), labels uppercase condensed muted, numbers `tabular-nums` right-aligned. Reuses existing `.nutri-table` conventions and tokens.
- No calculation changes — reads from the existing `summary.days` / `dailyAverage` / `weeklyTotal` from `previewSummary`. New `.macro-matrix` CSS added after the `.nutri-table` block.
- Verified: `npm run build` succeeds; `npm run lint` 0 errors (3 pre-existing warnings). No backend changes.

### Improve the Activity dropdown on the Add-Burn form — 2026-06-13 CDT

- [x] **Improved the `Activity` dropdown**: replaced the cramped native `<datalist>` autocomplete on the Add-Burn form with a themed type-ahead combobox consistent with the ingredient finder.

**NOTES:**

- New `src/web/src/components/ActivityFinder.tsx`: a free-text combobox over the user's past activities (`burnSuggestions`), reusing the existing `.finder`/`.finder-pop` popover styling. Case/accent-insensitive substring match, full keyboard nav (arrows/Enter/Escape), combobox ARIA, options capped at 30 with a "keep typing" footer. The field stays open-ended (no fixed activity catalog), so any typed name is valid; picking a past activity fills the name AND defaults the calories from its last entry.
- Extracted the shared `fold()` helper (lower-case + strip diacritics, mirrors server `SearchText.Fold`) into new `src/web/src/lib/fold.ts`, used by both the ingredient and activity finders; `IngredientFinder.tsx` now imports it instead of defining its own copy.
- `TodayPage.tsx`: swapped the `<input list=...>` + `<datalist>` for `<ActivityFinder>`, widened the field min-width 8rem→10rem.
- `index.css`: generalized the finder section comment and added `.finder-burn` (ember-accent "−N kcal last time" hint, burn's color per DESIGN.md §1.1).
- Verified: `npm run build` succeeds (tsc + vite); `npm run lint` 0 errors (3 pre-existing warnings). No backend changes.

### Remove option to add a `fuel` recipe to the Log — 2026-06-12 CDT

- [x] **Removed the option to add a `fuel` recipe to the `Log`**: recipes define a meal and are no longer directly loggable; meals and free-form items remain.

**NOTES:**

- **Reject, don't remove**: `ConsumptionSource.Recipe` stays in the enum because persisted log documents still carry it. New recipe-sourced input is rejected in `ConsumptionService.Validate` with: "Recipes cannot be logged directly. Log a meal that uses the recipe, or a free-form item." The read/compute path keeps its Recipe branch so historical recipe entries still load, display, and roll into day totals and goal progress.
- **Frontend**: removed the "Recipe" option from the Today page fuel form (and its now-unused recipe catalog load); `ConsumptionEntryInput.source` is typed `Exclude<ConsumptionSource, 'Recipe'>` so the compiler forbids sending it. Rendering of existing recipe-kind entries is untouched.
- **Tests** restructured: recipe-log test became a rejection test; new historical-entry tests insert Recipe-source entries directly via the repository (the shape old documents have) and assert they still list with live-computed nutrition, including the missing-source case.
- Verified: `dotnet build` 0 errors; `dotnet test` 280/280 passed; `npm run build` succeeds; `npm run lint` 0 errors (3 pre-existing warnings).

### Add favorites — 2026-06-12 CDT

- [x] **Add favorites**: added an option to mark ingredients, recipes, and meals as a `favorite`; favorites always appear at the top of the Library lists.

**NOTES:**

- **Per-user favorites stored on the user document, not on the entities.** Ingredients, recipes, and meals can be app-level (shared), so a flag on the entity would leak one user's favorite to everyone. `AppUser` gained `FavoriteIngredientIds`/`FavoriteRecipeIds`/`FavoriteMealIds` as `List<string>?` (nullable per the Cosmos old-document rule); empty lists collapse back to `null`.
- **API**: `PUT /api/{ingredients|recipes|meals}/{id}/favorite` with body `{ isFavorite }` in a new `FavoriteFunctions`/`FavoriteService`. Favoriting validates visibility (throws NotFound for invisible items); un-favoriting always succeeds so stale ids of deleted items can be cleaned up. `FavoritesMax = 1000` cap.
- **DTOs** gained a trailing `bool IsFavorite = false`, computed per caller (same pattern as `CanEdit`). Sorting happens in the services with a stable `OrderByDescending(isFavorite)` so the existing name order is preserved within each group.
- **Frontend**: `FavoriteStar` toggle component (new `IconStar`, `aria-pressed`, click-swallowing on card links) on the Ingredients/Recipes/Meals Library cards, with optimistic toggle + local re-sort and revert on API failure. `.fav-btn` styles in `index.css`: muted monoline at rest, ember-accent fill when marked.
- **Tests**: 9 new `FavoriteServiceTests` (per-user isolation on shared items, ordering, stale-id cleanup, idempotency, persistence) + DI registration test.
- Verified: `dotnet build` 0 errors; `dotnet test` 279/279 passed; `npm run build` succeeds; `npm run lint` 0 errors (3 pre-existing warnings).

### Design an Ingredient finder — 2026-06-12 CDT

- [x] **Designed an Ingredient finder**: replaced the plain ingredient drop-downs with a searchable, filterable combobox and enriched ingredient data with tags, aliases, and non-English names that are matched in search.

**NOTES:**

**Design decisions**
- **Searchable finder component** (`src/web/src/components/IngredientFinder.tsx`, new): a type-ahead combobox replacing the old `<select>`s. Filters the already-loaded ingredient catalog client-side; shows a "why it matched" hint (`aka garbanzo beans`, `es: garbanzos`, `#legume`), per-option serving size + kcal + a "Mine" badge, full keyboard navigation (arrows/Enter/Escape), proper combobox ARIA (`aria-controls`/listbox `id`), options capped at 50 rendered, and up to 10 most-common tags as toggleable filter chips.
- **Accent/case-insensitive matching, mirrored on both sides:** `src/GoalFueled.Domain/SearchText.cs` (new) folds text via lower-casing + Unicode NFD decomposition stripping combining diacritics, so "jalapeno" ↔ "jalapeño" match bidirectionally; `IngredientFinder.tsx` implements the identical folding in TS. Server-side, `IngredientRepository.Refine` applies `SearchText.MatchesIngredient` (name, aliases, tags, localized names) to the `?search=` param on the list endpoints — the Ingredients page's existing debounced search box now matches the metadata too.
- **Enriched data model:** `Ingredient` gained `Tags`, `Aliases`, and `LocalizedNames` (with a `LocalizedName` owned type, mapped via `OwnsMany`). All new persisted fields are **nullable** per the project's Cosmos rule (new non-nullable value-type fields throw on read for old documents). DTO exposes never-null lists; input accepts optional lists. `IngredientService` normalizes on save (trim, de-dup, lower-cased language codes, null when empty) and validates limits (max 50 entries/list via `InputLimits.IngredientSearchTermsMax`, length caps).
- **Seed enrichment + backfill:** all ~40 seed ingredients in `DatabaseInitializer` got meaningful tags/aliases/localized names (es/fr/zh/de/it…), plus a one-time backfill that adds metadata to already-seeded rows only if they have none (never clobbers admin edits).
- **Metadata is editable:** `IngredientFormPage` gained a "Search & discovery" card — chip editors for tags and aliases (Enter/Add, case-insensitive de-dup) and language-code + name rows for non-English names. This also fixed a real bug: the form previously never sent these fields, so saving an edit would have wiped the metadata server-side.
- **Where the finder is used:** `RecipeFormPage` ingredient rows (still auto-sets the unit on pick) and `PlanFormPage` untimed-item refs when the kind is ingredient (meals keep the plain select). `IngredientsPage` shows tag badges on list cards and a search placeholder advertising alias/tag/non-English search.
- **Styling** in `src/web/src/index.css` matches the athletic design language: condensed-caps tag chips, emerald match hints, surface popover with raised shadow, high-contrast border and coarse-pointer tap-target refinements.

**Verification**
- `dotnet build` — succeeded (0 errors).
- `dotnet test` — **270/270 passed** (incl. 3 new `IngredientServiceTests`: alias/tag/localized matching, bidirectional accent-insensitivity, save-time normalization).
- `npm run build` (`src/web`) — succeeds (tsc + vite, 91 modules).
- `npm run lint` — 0 errors (only 3 pre-existing warnings in untouched files).

### Clicking the app logo goes to the Dashboard — 2026-06-11 CDT

- [x] **Made the app logo (brand area) clickable so it navigates to the Dashboard (index route `/`).**

**NOTES:**
- `src/web/src/components/Layout.tsx`: changed the `.brand` element (rocket 🚀 mark + "GoalFueled" wordmark) from a `<div>` to a react-router `<NavLink to="/" end>`, matching the existing nav routing approach. Added `aria-label="Go to Dashboard"` for accessibility; kept `aria-hidden` on the rocket mark.
- `src/web/src/index.css`: added `color: inherit; text-decoration: none; cursor: pointer;` to the `.brand` rule so the now-anchor element keeps its original appearance (no link underline/color) while showing a pointer cursor. Existing flex layout and gradient wordmark styling untouched.
- Assumption: the "logo" is the brand block (rocket emoji + name); there is no separate image asset.
- Verified: `npm run build` (tsc + vite, succeeded) and `npm run lint` (0 errors, only the known pre-existing `AuthContext.tsx` warning).

### Rename the "Home" menu item to "Dashboard" — 2026-06-10 CDT

- [x] **Renamed the "Home" navigation menu item to "Dashboard".**

**NOTES:**
- Changed the nav `label` from `'Home'` to `'Dashboard'` in `src/web/src/components/Layout.tsx` (the `{ to: '/' }` entry; route and 🏠 icon unchanged) and updated the adjacent code comment ("The Dashboard link points at the index route"). A scan found no other user-facing "Home" nav label.
- Verified: `npm run build` + `npm run lint` (0 errors, only the pre-existing `AuthContext.tsx` warning).

### Show goal summaries on the Dashboard — 2026-06-10 CDT

- [x] **Added a motivational "Your goals 🎯" section to the Dashboard** listing the user's active goals and their progress, reusing the existing computed progress snapshots.

**NOTES:**
- **Frontend-only** (`src/web/src/pages/DashboardPage.tsx`). Goals fetched via the existing `goalApi.list()` (each `Goal` already carries a computed `progress` snapshot — not recomputed). Placed as a `card stack` between "Up next" and "Past 7 days".
- **Data load:** a dedicated `useEffect` with its own `goals`/`goalsError` state and the same cancelled-flag pattern, separate from `dashboardApi.get()` so neither blocks the other; shows a loading state.
- **Compact rendering:** a new inline `GoalRow` (reuses `card ingredient-item`) rather than the large 140px `GoalProgressVisual` ring — shows tone emoji, name, percent, a small accessible horizontal progress bar (`role="progressbar"` + aria values), the friendly `progress.message`, and `current / target {goalUnitLabel} · periodLabel`. Tone/emoji helpers mirror `GoalProgressVisual`'s `toneFor`/`emojiFor` for a consistent look. Units respect the preference via `goalUnitLabel(goal, preference)`.
- **Active filter (documented):** recurring goals are always active; one-time goals stay active until their deadline passes unmet — so only one-time goals with `status === 'Missed'` are excluded. InProgress/Achieved/NoData/recurring are shown.
- **States:** no goals → nudge linking to `/goals`; goals exist but none active → a separate "set a new one" nudge (list never silently empties); error → muted, isolated. Overflow capped at 4 with a "View all N goals →" link (no silent hiding).
- Reused existing CSS classes; no new CSS. No dead code/unused imports.
- Verified: `npm run build` + `npm run lint` (0 errors, only the pre-existing `AuthContext.tsx` warning).

### Add a units preference (Metric vs US) to the profile — 2026-06-10 CDT

- [x] **Added a per-user units preference (Metric vs US/Imperial)**, scoped to **weight and height only**, applied to entry defaults and displays throughout the app. Recipe/ingredient units were deliberately NOT touched (per the user's clarification).

**NOTES:**
- **Enum/default:** new `UnitsPreference` enum (`Metric = 0`, `UsImperial = 1`) in `Enums.cs`; `AppUser.UnitsPreference` defaults to **`UsImperial`** (matches today's lb + ft/in defaults, so existing users see no change). Persisted via `.HasConversion<string>()`. Metric → kg + cm; US → lb + ft/in.
- **Backend:** surfaced on `CurrentUserDto`; added as a trailing defaulted param on `OnboardingInput` and `ProfileInput` (positional callers/tests unaffected); `UserProfileService` validates + persists it (onboarding + update); `UserFunctions.ToDto` passes it through. +2 tests in `UserProfileServiceTests` (default + round-trip + reject unknown).
- **Frontend:** `UnitsPreference` type + `UNITS_PREFERENCE_LABELS` on the user/profile/onboarding types. New `lib/weight.ts` (`weightUnitFor`, `convertWeight` kg↔lb mirroring the server's `KilogramsPerPound`, `roundWeight`, `formatWeight`) and `lib/goalUnit.ts` (`goalUnitLabel`). `lib/height.ts` gained `heightUnitFor`; `formatHeight(cm, preference)` now shows cm (Metric) or ft/in (US). `ProfileFields` gained a Units selector that re-defaults the height/weight unit selectors; `WelcomePage`/`ProfilePage` seed & send it.
- **Where it now applies:** entry defaults — ProfileFields height+weight selectors, WeightPage record unit, Dashboard quick-log unit, onboarding current-weight unit. Displays (converted + labeled): WeightPage history, Dashboard recorded value + 7-day estimated-loss, Log `WeighInRow`, Profile saved-height, Goals list + `GoalProgressVisual` (weight goals use the preferred weight label; nutrient goals keep their own units).
- **Scope confirmed:** `UnitOfMeasure`/`UnitOfMeasureInfo` and recipe/ingredient forms untouched.
- Verified: backend `dotnet build` (0 warnings/errors) + `dotnet test` (253 passed); frontend `npm run build` + `npm run lint` (0 errors, only the pre-existing `AuthContext.tsx` warning).

### Improve the Fuel Gauge chart (y-axis ticks + smoothing) — 2026-06-10 CDT

- [x] **Made the Fuel Gauge chart easier to read**: added y-axis (calorie) gridlines + labels and smoothed the trajectory lines.

**NOTES:**
- Frontend-only, in `src/web/src/components/DailyProgressChart.tsx` (still dependency-free, hand-rolled SVG).
- **Y-axis ticks:** widened `PAD_LEFT` 8→34 for kcal labels (the plot shifts right automatically since `x()` derives from `PAD_LEFT`). Added a `NICE_STEPS` table + `niceStep(maxAbs)` helper that picks the largest "nice" increment (25/50/100/250/500/1000/2000/5000) fitting the symmetric `±maxAbs` range; draws 3 or 5 subtle dashed horizontal gridlines (`var(--border)`, low opacity) with end-anchored `+N`/`N` labels. The zero baseline stays solid/stronger.
- **Smoothing:** new `toSmoothPath(pts)` converts the hourly `{hour,net}` points to a Catmull-Rom→cubic-Bézier `<path>` that passes exactly through every data point (endpoints clamped, no invented peaks). Both `<polyline>`s replaced by `<path>`; EXPECTED stays muted/dashed, ACTUAL solid/primary; end-point circles + `<title>` tooltips unchanged. Removed the now-unused `toPolyline` helper.
- `W/H`/viewBox unchanged (still within the 200px `.log-chart svg` cap); `buildSeries` and all data math untouched; `role="img"` + "Fuel Gauge" aria-label preserved.
- Verified: `npm run build` + `npm run lint` (0 errors, only the pre-existing `AuthContext.tsx` warning).

### Rename the "Net calories by hour" chart to "Fuel Gauge" — 2026-06-10 CDT

- [x] **Renamed The Log's "Net calories by hour" chart to "Fuel Gauge".**

**NOTES:**
- The task spelled it "Fuel Guage" — corrected to the proper **"Fuel Gauge"** spelling for the user-facing label.
- Changed the `<h2>` heading in `LogPage.tsx` (kept the "actual vs expected" subtitle span) and the SVG `aria-label` in `DailyProgressChart.tsx` ("Fuel Gauge: cumulative net calories by hour of day, actual versus expected"). The component/file/props and `log-chart` CSS class were left unchanged (only the human-readable labels). One internal code comment describing the data was left as-is.
- Verified: `npm run build` + `npm run lint` (0 errors, only the pre-existing `AuthContext.tsx` warning).

### Allow modifying a planned meal's servings when logging it — 2026-06-10 CDT

- [x] **Users can now adjust the serving size when logging a planned meal** (to record eating more or less than planned), both at log time and afterward; the day's nutrition/fuel totals scale accordingly.

**NOTES:**
- **Backend (single create-or-update `complete` endpoint — no new endpoint):** added trailing optional `double? Servings = null` to `CompletePlannedMealInput`. `CompletePlannedMealAsync` now uses the supplied servings (default 1). Behavior: no existing completion → create with `Servings ?? 1`; existing completion + a (different) servings supplied → update that entry's `Servings`/`UpdatedAt` in place (no duplicate); existing completion + no servings → returns unchanged (a plain re-complete never clobbers a chosen amount back to 1). Skip entries are excluded from the completion match. Added `UpdateAsync` to `IConsumptionEntryRepository`/`ConsumptionEntryRepository` (mirrors `WeightEntryRepository`). Extracted a shared `ValidateServings` helper (same `>0` / `ConsumptionServingsMax` rule + messages as ad-hoc logging). Nutrition already scales by servings, so totals adjust naturally.
- **Frontend:** added optional `servings?: number` to `CompletePlannedMealInput` (TS). `LogPage` gained per-ref `plannedServings` state; `toggleTask` accepts servings; an `editServings` handler. Pending `PlannedMealRow` shows a compact "Servings" number input (defaults 1) next to the complete checkbox (displayed kcal = per-serving × chosen). Completed `DoneMealRow` shows logged servings + actual kcal with an inline number input that commits on blur/Enter (only when valid & changed) and re-submits to update in place; edits don't bubble to the checkbox (so editing never un-completes), and the input is keyed on servings to re-sync after reload.
- **Tests:** +new cases in `PlannedLogTests.cs` (servings=2 scales totals; re-complete with different servings updates in place with no duplicate; re-complete without servings preserves the chosen amount; default-to-1; `[Theory]` rejecting 0/-1/over-max).
- Verified: backend `dotnet build` (0 warnings/errors) + `dotnet test` (251 passed); frontend `npm run build` + `npm run lint` (0 errors, only the pre-existing `AuthContext.tsx` warning).

### Add a daily progress chart to The Log — 2026-06-10 CDT

- [x] **Added a "Net calories by hour" line chart** to The Log's summary section, plotting **actual vs expected** cumulative net calories across the day, with the Kindle (passive) burn averaged evenly across the 24h day.

**NOTES:**
- **Frontend-only** (no backend change) — all data already loads on `LogPage`. New dependency-free inline-SVG component `src/web/src/components/DailyProgressChart.tsx`, modeled on `NetBalanceChart.tsx` (same viewBox/padding/color-var house style, `<title>` tooltips, `aria-label`).
- **Placement:** a `card stack log-chart` with heading "Net calories by hour · actual vs expected", inserted directly below the `log-totals` header card (top of the day-at-a-glance summary).
- **Expected line** (muted, dashed): cumulative planned-meal calories scheduled at/before hour h, minus `kindleCost·(h/24)`; spans the full day. *Anytime* (untimed) meals are placed at end-of-day (hour 24); *skipped* meals are excluded (won't be eaten).
- **Actual line** (brand primary, solid): cumulative logged fuel (all `day.entries`, both planned-completed and ad-hoc) minus burn (placed by burn `time` when present else `createdAt`) minus `kindleCost·(h/24)`. For **today** the actual line stops at the current hour; for a **past** day both lines span 0→24.
- Kindle averaged as `kindleCost·(h/24)` for both lines (treated as 0 when the profile is incomplete). The chart card hides entirely when there's nothing to plot (`hasChartData` guard). Added `.log-chart svg { max-height: 200px; }` to `index.css`.
- Verified: `npm run build` + `npm run lint` (0 errors, only the pre-existing `AuthContext.tsx` warning).

### Rename "Still to do" to "Planned" in The Log — 2026-06-10 CDT

- [x] **Renamed the "Still to do" section label to "Planned"** in The Log's timeline (the pinned block of pending planned meals + due weigh-in).

**NOTES:**
- Changed the visible label in `LogPage.tsx` (`log-section-label` span) and updated two comments that quoted the label by name (`LogPage.tsx` JSX comment, `index.css` section-labels comment). Internal identifiers/CSS classes (`log-todo`, `log-section-label`) were left unchanged; remaining lowercase prose references describing the concept were left as-is (minimal change).
- Verified: `npm run build` + `npm run lint` (0 errors, only the pre-existing `AuthContext.tsx` warning).

### Add an option to skip a planned meal in The Log — 2026-06-10 CDT

- [x] **Users can now skip a planned meal**, removing it from the "Still to do" list. A skipped meal contributes no nutrition, persists across reloads, and can be undone.

**NOTES:**
- **Approach (flag, reusing the existing correlation mechanism):** a skip is a `ConsumptionEntry` tagged with the task's `PlannedMealRef` and `Skipped = true`, carrying zero nutrition (`FreeForm`, `Servings = 0`, empty `Nutrition`). No new entity/repo/DI. Unskip reuses the existing uncomplete endpoint (deletes any entry for the ref).
- **Backend:** added `bool Skipped` to `ConsumptionEntry`; trailing optional `bool Skipped = false` on `PlannedMealTaskDto`; `SkipPlannedMealAsync` in `ConsumptionService`; new `consumption/planned/skip` POST endpoint. `GetPlannedDayAsync` marks a task `Skipped` (not `Done`) when its correlated entry is a skip (real completion still takes precedence). Skipped entries are excluded from `GetDayAsync` (entries list + `TotalNutrition`) and from `DailyNutritionInRangeAsync` (goal/dashboard roll-ups), and `DashboardService`'s upcoming-meal filter now excludes them. Plain `bool` maps natively — no DbContext change.
- **Frontend:** `skipped?: boolean` on `PlannedMealTask`; `consumptionApi.skipPlanned(...)` (undo reuses `uncompletePlanned`). The pending list is now `!t.done && !t.skipped`; `PlannedMealRow` gained a small "Skip" button (outside the complete checkbox `<label>`); skipped meals render in a muted "Skipped" section with an "Undo" button; a `skippingRef` busy state disables buttons mid-request.
- **Tests:** +6 in `PlannedLogTests.cs` (skip marks Skipped/not-Done & drops from pending with no nutrition; excluded from daily roll-up; idempotent; unskip restores; skipping a completed task replaces the completion & clears nutrition; skip rejects an unknown ref).
- Verified: backend `dotnet build` (0 warnings/errors) + `dotnet test` (244 passed); frontend `npm run build` + `npm run lint` (0 errors, only the pre-existing `AuthContext.tsx` warning).

### Add an optional time when adding a burn entry — 2026-06-10 CDT

- [x] **Burn (exercise) log entries now accept an optional time of day**, defaulting to the current time. The specified time orders and labels the burn in The Log's chronological timeline.

**NOTES:**
- **Backend:** added `TimeOnly? Time` to the `BurnActivity` entity and to both `BurnActivityDto` and `BurnActivityInput` (as a trailing optional param, so existing positional callers/tests didn't break). `BurnService.LogAsync` sets it; `ToDto` maps it. `TimeOnly?` maps natively under EF Core 8 (in-memory/relational) — no DbContext change needed.
- **Frontend:** added optional `time?: string` ("HH:mm:ss") to the `BurnActivity`/`BurnActivityInput` TS types. The "+ Add burn" form gained a `type="time"` input defaulting to the current local time (`nowTime()`); a `toApiTime()` helper normalizes "HH:mm" → "HH:mm:ss". Clearing the input sends no time.
- **Timeline:** a burn's sort key uses its specified time (`secondsFromScheduledTime`) when present, else falls back to `createdAt`; `BurnRow` likewise displays the specified time (`formatTime`) when present, else `formatClock(createdAt)`. Older time-less burns are unaffected.
- Verified: backend `dotnet build` (0 warnings/errors) + `dotnet test` (238 passed); frontend `npm run build` + `npm run lint` (0 errors, only the pre-existing `AuthContext.tsx` warning).

### Add a personal-data explanation note to the profile — 2026-06-10 CDT

- [x] **Added an explanatory note** near the sex / year-of-birth / height fields explaining that we use this data to calculate the user's **Kindle** (the calories burned just by being alive, at rest) and reassuring them we will never share it without permission.

**NOTES:**
- Added inside the shared `ProfileFields` component (directly after the Height field), so it appears on BOTH the Welcome onboarding page and the Profile page with no duplicated text and no new prop.
- Wording: *"We use your sex, year of birth, and height to work out your Kindle — the calories your body burns just by being alive, even at rest. We'll never share this with anyone without your permission."*
- Correctly frames Kindle as a passive daily calorie burn (the task text said "Kindle weight", but Kindle is a burn, not a weight). Rendered as a `muted small` paragraph matching the existing note convention. Purely additive — no field logic changed.
- Verified: `npm run build` + `npm run lint` (0 errors, only the pre-existing `AuthContext.tsx` warning).

### Collect only the user's year of birth — 2026-06-10 CDT

- [x] **The profile now collects only the user's year of birth** (not a full date of birth) to avoid storing private data. In age calculations, January 1 of the birth year is used as the date of birth.

**NOTES:**
- **Minimal-churn approach:** the backend storage and wire contract are unchanged — `AppUser.DateOfBirth` stays `DateOnly?` and the DTO field stays `dateOfBirth` ("yyyy-MM-dd"). The UI simply stores `"{year}-01-01"`. `KindleCostCalculator.AgeOn` is year-based math, so age calcs already "use Jan 1 of the birth year" with no backend change.
- `ProfileFields.tsx`: replaced the `type="date"` "Date of birth" input with a bounded `type="number"` "Year of birth" input (min 1900, max current year, no future years), with `birthYear`/`setBirthYear` conversion helpers (reads the year by slicing the stored value, writes back `${year}-01-01`). Removed the now-unused `todayIso` helper. `ProfileFieldsValue.dob` kept as the "yyyy-MM-dd" string (no new field).
- `WelcomePage.tsx` / `ProfilePage.tsx`: validation messages updated to "…year of birth."
- Legacy rows with a real month/day still display the correct year and normalize to Jan 1 on next save.
- Verified: frontend `npm run build` + `npm run lint` (0 errors, only the pre-existing `AuthContext.tsx` warning); backend `dotnet build` (0 warnings/errors) + `dotnet test` (238 passed).

### Rework The Log into a single chronological timeline — 2026-06-10 CDT

- [x] **The Log is now a single chronological day timeline** instead of separate `Fuel` and `Burn` sections. Top of the page shows a **totals header** (Fuel / Burn / Net Calories). The meal-plan picker is collapsed into a compact one-line disclosure. Adding ad-hoc fuel/burn is now behind **`+ Add fuel`** / **`+ Add burn`** buttons (forms toggle inline) rather than always-visible forms. A single timeline lists the day's events latest-first; **pending planned items float to the top** ("Still to do": planned fuel tasks + a due weigh-in), completed events sort by time-of-day, and **Kindle** sits at the bottom as the day's passive baseline (start of day).

**NOTES:**
- **Layout (top→bottom):** title + date picker → `.log-totals` header (3 tiles, Net emphasized with `fuel − burn[ − kindle]` subtext) → compact `<details>`/`<summary>` plan picker → `+ Add fuel` / `+ Add burn` toggle buttons (forms are the original markup verbatim, collapse on successful submit) → single "Today's timeline · latest first" card → day-total `NutritionLabel`.
- **Chronological ordering:** each completed event gets a `sortKey` = seconds since local midnight, sorted descending. Completed planned meals use scheduled `time` (fallback to completing entry's `createdAt`); ad-hoc fuel + burn use `createdAt` time-of-day; a recorded weigh-in (no timestamp) is placed at start of day (sortKey 0, above Kindle); "Anytime" meals fall back to `createdAt` else 0. Kindle is rendered outside the sorted list, always last. Ties broken by key for stable order.
- All existing functionality preserved: task complete/un-complete, add/remove fuel, add/remove burn, weigh-in link, plan switching, burn autocomplete, day-total label. Removed the now-unused `FuelBurnHeading` import.
- Minimal supporting CSS added to `src/web/src/index.css` (`.log-totals` tiles). Inline-style/aria IDE advisories were left as-is — not part of the project's configured eslint and consistent with existing pages (e.g. DashboardPage).
- Verified: `npm run build` (`tsc -b && vite build`) succeeded; `npm run lint` reported 0 errors (only the pre-existing unrelated `AuthContext.tsx` warning).

### Remove `Current Weight` from the profile page — 2026-06-10 CDT

- [x] **`Current Weight`** in the `/profile` page did not allow entry, so it was removed from the profile page. Weight continues to be recorded through The Log (existing functionality, left intact).

**NOTES:**
- The weight field lives in the shared `ProfileFields` component, which is *also* used by the Welcome/onboarding page where current weight is still required. To avoid breaking onboarding, the `weightRequired?: boolean` prop was replaced with `showWeight?: boolean` (default `true`); `ProfilePage` passes `showWeight={false}` while `WelcomePage` keeps the default.
- `ProfilePage.save()` no longer parses/validates weight nor includes `currentWeight` in the `updateProfile` payload, so saving the profile never upserts a weight entry. `weightUnit` is still sent because the backend `ProfileInput` contract requires it; the API ignores it when no weight is supplied.
- Removed the now-obsolete explanatory paragraph and the post-save weight-field reset.
- Verified: `npm run build` (`tsc -b && vite build`) passed; `npm run lint` reported 0 errors (1 pre-existing unrelated warning in `AuthContext.tsx`).

- [x] **Capture basic info during first login**: when a user first creates their account, let's prompt them for a few basic pieces of information (one time). We'll also need a user profile page where they can update these attributes later if necessary.
  - Sex at Birth: male/female
  - Current Weight
  - Height
  - Goal: (already asking)

### Create a landing page dashboard — 2026-06-10 CDT

- [x] The app now opens on a **Dashboard** home page that gives an at-a-glance view of the user's progress: the **next 3 upcoming meals** from the plan they're following (with consume-times and a one-tap **Done** to complete them), a **weigh-in prompt shown only when a measurement is due today** (with an inline quick-log), and a **7-day chart of net calories (fuel − burn − kindle)** with a derived **estimated weight change** (deficit → loss). The dashboard is the post-login landing route; The Log, Plans, etc. stay reachable from the nav (a new **Home** entry was added).

**NOTES:**

**Design decisions**
- **One aggregating endpoint — `GET /api/dashboard`** returning a `DashboardDto`, rather than composing several existing calls client-side. Cleaner, single round-trip, and unit-testable server-side. It reuses (injects) the existing services and does no roll-up math of its own.
- **`DashboardService`** (in `src/GoalFueled.Api/Services`) injects `MealPlanService`, `ConsumptionService`, `BurnService`, and `IWeightEntryRepository`, and uses the static `KindleCostCalculator` + the new `WeightProgressEstimator`. It exposes `GetAsync(user, date?, now?, ct)` — `now` is injectable so the "from now" upcoming-meal ordering is deterministically testable.
- **Next-3 upcoming meals:** reuses `ConsumptionService.GetPlannedDayAsync` (which already resolves the active plan's tasks + completion state via `MealPlanService.GetPlannedMealsForDateAsync`). Selection = today's **not-yet-completed** tasks scheduled **at/after now**, ordered **timed-before-untimed then by time**; **untimed ("anytime") meals sort last**. **Documented roll-forward:** if fewer than 3 remain today, we roll into subsequent plan days (up to 7 days ahead) taking each day's meals in schedule order, flagged `isLaterDay: true` (those show a "later" badge instead of a Done button, since you complete them on their day). Completion of upcoming meals uses the **existing** `POST /api/consumption/planned/complete` with the task `ref` + the occurrence `date` (the DTO carries both) — no new completion path.
- **Weight due:** surfaced only when due today. Reuses the planned-day `WeightTaskDto` (which `GetPlannedDayAsync` returns non-null only on a due day → presence == "due today"). The dashboard maps it to `WeightDueDto { alreadyRecorded, recordedValue, recordedUnit }`. Logging reuses the existing `POST /api/weights` (`WeightService.RecordAsync`, one-per-day upsert).
- **7-day series:** reuses `ConsumptionService.DailyNutritionInRangeAsync` (fuel; calories nutrient id `"calories"`) and `BurnService.DailyBurnInRangeAsync` (burn). **Kindle** is derived once via `KindleCostCalculator.TryCompute` from the profile + **latest** recorded weight and applied to every day in the window. **Documented simplification:** past days reuse the current weight rather than reconstructing each day's historical weight (kindle varies little day-to-day; avoids a per-day weight lookup). Net = fuel − burn − kindle. When the profile is incomplete the kindle term is 0, `kindleAvailable=false`, and the UI shows a "complete your profile" nudge with net = fuel − burn only.
- **Weight-lost estimate — pure helper:** `src/GoalFueled.Domain/WeightProgressEstimator.cs` (static, dependency-free). `CaloriesPerPound = 3500`. `PoundsLost(net) = -net / 3500` — **sign:** a NEGATIVE cumulative net (deficit) → POSITIVE pounds lost; a positive net (surplus) → negative (a gain). `CumulativeNet`, `PoundsLostOverSeries`, and `InUnit(pounds, unit)` (lb passthrough; kg via `KindleCostCalculator.KilogramsPerPound`) round it out. The DTO reports the estimate in the user's last-used weight unit.
- **Chart — dependency-free inline SVG** (`src/web/src/components/NetBalanceChart.tsx`, no new npm deps), in the hand-rolled-SVG style of `GoalProgressVisual.tsx`: per-day net bars around a zero baseline (emerald = deficit/toward-loss, flame-orange = surplus) with a dashed cumulative-net trend line; the estimated weight delta is shown prominently above it.
- **Empty/incomplete states:** no active plan → prompt linking to The Log to pick one; active plan but nothing upcoming → friendly "enjoy the breather"; profile incomplete (kindle unavailable) → fuel − burn only + a gentle profile nudge; no fuel/burn/kindle data in the window (`hasData=false`) → friendly zero-state.

**Files created**
- `src/GoalFueled.Domain/WeightProgressEstimator.cs` — the pure net→weight-change helper.
- `src/GoalFueled.Domain/Contracts/DashboardDtos.cs` — `DashboardDto`, `UpcomingMealDto`, `WeightDueDto`, `ProgressSeriesDto`, `ProgressDayDto`.
- `src/GoalFueled.Api/Services/DashboardService.cs` — the aggregation.
- `src/GoalFueled.Api/Functions/DashboardFunctions.cs` — `GET /api/dashboard`.
- `src/web/src/pages/DashboardPage.tsx` — the landing page.
- `src/web/src/components/NetBalanceChart.tsx` — the inline SVG chart.
- `tests/GoalFueled.Tests/WeightProgressEstimatorTests.cs` — 6 pure-helper tests (incl. sign).
- `tests/GoalFueled.Tests/DashboardServiceTests.cs` — 13 tests (upcoming selection/ordering/roll-forward/skip-completed, weight-due flag, 7-day series assembly with/without kindle).

**Files changed (backend)**
- `src/GoalFueled.Api/Program.cs` — registered `DashboardService`.
- `tests/GoalFueled.Tests/DependencyInjectionTests.cs` — registered + asserted `DashboardService`.

**Files changed (frontend)**
- `src/web/src/api/types.ts` — `Dashboard`/`UpcomingMeal`/`WeightDue`/`ProgressSeries`/`ProgressDay` TS mirrors.
- `src/web/src/api/services.ts` — `dashboardApi.get()`.
- `src/web/src/App.tsx` — `/` index route now renders `DashboardPage` (was a redirect to `/ingredients`); catch-all redirects to `/`.
- `src/web/src/components/Layout.tsx` — added the **Home** nav entry (`end` match so it highlights only on `/`).

**API endpoints**
- **New:** `GET /api/dashboard[?date=]` → `DashboardDto`. No other endpoints changed; meal completion and weight logging reuse the existing `POST /api/consumption/planned/complete` and `POST /api/weights`.

**Verification**
- `dotnet test -c Release` → **238/238 passed** (was 219; +19: 6 estimator + 13 dashboard).
- `npm run build` (`src/web`) → **succeeds** (tsc + vite, 79 modules).
- `npm run lint` (`src/web`) → **0 errors** (only the 1 pre-existing `AuthContext.tsx` warning; no new ones).

### Create a `Kindle` cost — 2026-06-10 CDT

- [x] The Log now shows a passive **Kindle cost** — the calories a user burns just staying alive — as its own clearly-labeled `burn` line, and subtracts it in the day's net-calorie summary (**net = fuel − burn − kindle**). The cost is the Mifflin–St Jeor BMR × 1.2 (sedentary factor), derived on the fly from the user's profile (sex, date of birth → age, height) and latest recorded weight. Because the formula needs an **age** the profile didn't capture, a **date of birth** field was added to onboarding and the Profile page (mirroring the recently-added sex/height fields). When any input is missing the cost is unavailable and The Log prompts the user to complete their profile instead of showing a wrong/zero number.

**NOTES:**

**Design decisions**
- **Pure, unit-testable math:** all BMR/Kindle arithmetic lives in `src/GoalFueled.Domain/KindleCostCalculator.cs` (a static, HTTP/DB-free class). It exposes `SexConstant` (+5 male / −161 female), `ToKilograms` (lb→kg at 1 lb = 0.45359237 kg), `AgeOn(dob, asOf)` (whole-year age derivation respecting whether the birthday has passed), `Bmr`, `Compute` (= `round(BMR × 1.2)`, `MidpointRounding.AwayFromZero`), and `TryCompute(...)` which folds in the graceful-degradation rule. `KindleLimits.MaxAgeYears = 130` bounds plausible ages.
- **Derived, never stored:** the Kindle cost is computed per request from the current profile + the **latest** `WeightEntry`, so it always reflects the user's current weight and never duplicates/grows a daily `BurnActivity` row. No new entity/table.
- **Surfacing — extended `BurnDayDto`:** added `int? KindleCost` to `BurnDayDto` (returned by `GET /api/burn`). The Log already loads the burn day for its Burn section and net summary, so this is the cleanest single integration point — no new endpoint. `BurnService` gained an `IWeightEntryRepository` dependency to read the latest weight and calls `KindleCostCalculator.TryCompute`.
- **Graceful-degradation rule (precise):** `KindleCost` is `null` (and the client shows "Complete your profile … to see your Kindle cost") when **any** of these is true: no `SexAtBirth`, no `HeightCm` (or ≤ 0), no `DateOfBirth`, no recorded weight (or value ≤ 0), or the derived age is < 0 (future DOB) or > 130. Otherwise it is a positive kcal magnitude the client subtracts (treated as negative in the net summary, consistent with logged burn's sign convention).
- **Added date of birth (the missing input):** chose `DateOnly? DateOfBirth` on `AppUser` (durable — age is derived, not snapshotted) over storing an age. Added to `OnboardingInput`/`ProfileInput`/`CurrentUserDto`, validated in `UserProfileService` (not in the future; implied age ≤ `KindleLimits.MaxAgeYears`) consistent with the existing height/weight validation style. `DateOnly` maps natively in EF for both Cosmos and the in-memory provider (like the weight-schedule start date) — no value converter needed.
- **The Log presentation:** the Kindle line uses the burn icon/treatment (🔥) and is labeled "Kindle cost · passive", distinct from logged exercise (which remains individually removable; Kindle is not removable). The Burn section heading total and the net-calorie card both include it; when unavailable, both surface a friendly prompt linking to `/profile`.

**Files created**
- `src/GoalFueled.Domain/KindleCostCalculator.cs` (`KindleCostCalculator` + `KindleLimits`).
- `tests/GoalFueled.Tests/KindleCostCalculatorTests.cs` (10 pure-math/degradation tests).

**Files changed (backend)**
- `src/GoalFueled.Domain/Entities/AppUser.cs` — added `DateOnly? DateOfBirth`.
- `src/GoalFueled.Domain/Contracts/UserDtos.cs` — `DateOfBirth` on `CurrentUserDto`, `OnboardingInput`, `ProfileInput`.
- `src/GoalFueled.Domain/Contracts/BurnDtos.cs` — `int? KindleCost` on `BurnDayDto`.
- `src/GoalFueled.Api/Services/UserProfileService.cs` — persist + validate `DateOfBirth` (`ValidateDateOfBirth`).
- `src/GoalFueled.Api/Functions/UserFunctions.cs` — map `DateOfBirth` into `CurrentUserDto`.
- `src/GoalFueled.Api/Services/BurnService.cs` — inject `IWeightEntryRepository`; compute `KindleCost` in `GetDayAsync` via `KindleCostCalculator.TryCompute`.
- `src/GoalFueled.Data/GoalFueledDbContext.cs` — clarifying comment (no converter needed for `DateOfBirth`).
- `tests/GoalFueled.Tests/UserProfileServiceTests.cs` — DOB in the test helpers/inputs; new DOB persistence + future/too-old validation tests.
- `tests/GoalFueled.Tests/BurnActivityServiceTests.cs` — updated `BurnService` ctor; new Kindle present/absent tests.
- (`DependencyInjectionTests` unchanged — `BurnService`'s new dependency `IWeightEntryRepository` was already registered.)

**Files changed (frontend)**
- `src/web/src/api/types.ts` — `dateOfBirth` on `CurrentUser`/`OnboardingInput`/`ProfileInput`; `kindleCost?` on `BurnDay`.
- `src/web/src/components/ProfileFields.tsx` — `dob` field + native date input (capped at today).
- `src/web/src/pages/WelcomePage.tsx` & `pages/ProfilePage.tsx` — seed/validate/submit DOB.
- `src/web/src/pages/LogPage.tsx` — Kindle passive burn line, net = fuel − burn − kindle, and the incomplete-profile prompts.

**API endpoints**
- No new endpoints. `GET /api/burn[?date=]` now also returns `kindleCost` (nullable). `POST /api/me/onboarding` and `PUT /api/me/profile` now accept/require `dateOfBirth`; `GET /api/me` returns it.

**Verification**
- `dotnet test -c Release` → **219/219 passed** (was 203; +16: 10 calculator + 3 DOB profile + 3 Kindle burn).
- `npm run build` (`src/web`) → **succeeds** (tsc + vite, 77 modules).
- `npm run lint` (`src/web`) → **0 errors** (only the 1 pre-existing `AuthContext.tsx` warning; no new ones).

### Log `burn` activities — 2026-06-10 CDT

- [x] Users can log a `burn` activity (a free-form name + calories burned) for a day in The Log's **Burn** section. Calories are stored as a positive magnitude and treated as **negative** in the day's net-calorie summary (net = fuel − burn). The activity-name field autocompletes from the names the user has logged before; selecting a past name pre-fills the calories from that name's most-recent entry (still editable).

**NOTES:**

**Design decisions**
- **New entity `BurnActivity`** (`src/GoalFueled.Domain/Entities/BurnActivity.cs`), OwnerUserId-scoped, mirroring `ConsumptionEntry`/`WeightEntry`: `Id`, `OwnerUserId`, `DateOnly Date`, `string Name`, `int CaloriesBurned` (positive magnitude), `CreatedAt`/`UpdatedAt`. Chose `int` for calories (whole kcal is the right granularity for exercise burn; the client rounds before posting). No per-day uniqueness — a day can have many activities; logging appends, removal is by id (mirrors consumption).
- **Sign convention (explicit & consistent):** calories are stored and returned as a **positive magnitude** everywhere in the slice. The negative sign is applied **only** where burn enters a net-calorie summary. `BurnDayDto.TotalCaloriesBurned` is a positive total the caller subtracts; `BurnService.DailyBurnInRangeAsync` returns positive per-day totals for future net roll-ups; The Log computes `net = fuel − burn` and renders burn with a leading `−`. The service never stores or returns a negative value.
- **Net-calorie summary in The Log:** reused the existing fuel calorie roll-up (`ConsumptionDay.totalNutrition` → the `calories` nutrient, the same nutrient-id the page already reads) for the fuel side, subtracted the burn day total, and surfaced a new "Net calories (fuel − burn)" card. The Burn section heading shows `−N kcal`. (Deeper analytics/dashboard are a later task; this only makes The Log's day summary reflect fuel − burn.)
- **Suggestions / autocomplete shape:** `GET /api/burn/suggestions` → `BurnSuggestionDto[] { name, lastCalories }`, derived from the user's whole history, **most-recent-first and distinct by name** (case-insensitive dedupe; the most-recent entry for each name wins, so its calories become the default pre-fill). UX uses a native `<input list>` + `<datalist>` (no new npm dependency); on a case-insensitive name match the calories field defaults to that name's `lastCalories` but stays editable. Suggestions are re-fetched after a successful add so a brand-new name is immediately available.
- **Provider-agnostic persistence:** `BurnActivityRepository` materializes the user's (few) rows then filters/orders `DateOnly` in memory — identical to `ConsumptionEntryRepository`/`WeightEntryRepository` — so it works against both Cosmos and the in-memory provider. Added the `BurnActivities` Cosmos container mapping (partition key + key on `Id`; no owned collections).
- **Validation (matches Weight/Consumption conventions):** name required + length-bounded (`InputLimits.NameMaxLength`); calories must be > 0 and ≤ new `InputLimits.BurnCaloriesMax` (100,000); date defaults to today and cannot be in the future. Removal is owner-scoped (cross-user delete → `NotFoundException`, no cross-user view even for admins).

**Files created (backend)**
- `src/GoalFueled.Domain/Entities/BurnActivity.cs`
- `src/GoalFueled.Domain/Contracts/BurnDtos.cs` (`BurnActivityDto`, `BurnDayDto`, `BurnSuggestionDto`, `BurnActivityInput`)
- `src/GoalFueled.Data/Repositories/IBurnActivityRepository.cs` + `BurnActivityRepository.cs`
- `src/GoalFueled.Api/Services/BurnService.cs`
- `src/GoalFueled.Api/Functions/BurnFunctions.cs`
- `tests/GoalFueled.Tests/BurnActivityServiceTests.cs` (13 tests)

**Files changed (backend)**
- `src/GoalFueled.Api/Common/InputLimits.cs` — added `BurnCaloriesMax = 100_000`.
- `src/GoalFueled.Data/GoalFueledDbContext.cs` — `DbSet<BurnActivity> BurnActivities` + `BurnActivities` container mapping.
- `src/GoalFueled.Data/DataServiceCollectionExtensions.cs` — registered `IBurnActivityRepository`.
- `src/GoalFueled.Api/Program.cs` — registered `BurnService`.
- `tests/GoalFueled.Tests/DependencyInjectionTests.cs` — assert `BurnService` + `IBurnActivityRepository` resolve.

**Files created/changed (frontend)**
- `src/web/src/api/types.ts` — added `BurnActivity`, `BurnDay`, `BurnSuggestion`, `BurnActivityInput`.
- `src/web/src/api/services.ts` — added `burnApi` (`day`, `log`, `remove`, `suggestions`).
- `src/web/src/pages/LogPage.tsx` — Burn section now logs activities (datalist autocomplete defaulting calories from the picked name's last entry), lists/removes them, shows `−N kcal` in the heading, and added a "Net calories (fuel − burn)" summary card.

**New API endpoints** (all per-authenticated-user)
- `GET /api/burn[?date=yyyy-MM-dd]` → `BurnDayDto` (activities + positive `TotalCaloriesBurned`; date defaults to today).
- `POST /api/burn` → `BurnActivityDto` (body `BurnActivityInput { name, caloriesBurned, date? }`).
- `GET /api/burn/suggestions` → `BurnSuggestionDto[] { name, lastCalories }` (distinct by name, most-recent-first).
- `DELETE /api/burn/{id}` → 204 (owner-scoped).

**Verification**
- `dotnet test -c Release` → **203/203 passed** (was 190; +13 new burn tests).
- `npm run build` (`src/web`) → **succeeds** (tsc + vite, 77 modules).
- `npm run lint` (`src/web`) → **0 errors** (only the 1 pre-existing `AuthContext.tsx` warning; no new ones).

### Re-design around `fuel` and `burn` — 2026-06-10 CDT

- [x] Introduced a consistent `fuel` (calories added — food) vs. `burn` (calories used — exercise) vocabulary across the app, applied to The Log. Meal-plan/consumption tasks and ad-hoc food logging are now framed as **Fuel**; a **Burn** section is present with an empty state, ready for the upcoming "log burn activities" task. Added a reusable icon/label treatment for both concepts.

**NOTES:**

**Design decisions**
- **Frontend-only conceptual/visual pass.** No backend changes were needed (the burn data model is the next task), so no `.NET` code or tests changed. The DoD `.NET` suite was still run to confirm green.
- **Icons — emoji wrapped in a reusable component.** Followed the app's established emoji-icon convention (nav uses 🍪 ⚖️ 🎯, etc.) rather than introducing SVGs or any npm dependency. Chose **🍽️ for fuel** (reuses the app's existing "meal" emoji — reinforces "consuming food") and **🔥 for burn** (universal calorie-burn/exercise symbol; also mirrors the brand's flame-orange rocket exhaust). Both are wrapped in `components/FuelBurnIcon.tsx` so the iconography + labels + accent colors are applied consistently and the next task can reuse them for real burn items.
- **Accent colors map to the existing theme tokens.** Fuel → `--primary` (emerald, the brand's "fuel"), Burn → `--accent` (flame-orange). This was a natural fit: the theme header literally describes "a green check-rocket … with a flame-orange exhaust." No theme files under `src/web/src/theme` were touched; all styling lives in `src/web/src/index.css` via existing CSS variables.
- **Where the vocabulary appears.** The Log now groups its day around the two concepts:
  - **Fuel** sections (accent-edged cards, green): the plan-driven tasks ("Fuel you've planned to eat … check each off as you consume it"), the ad-hoc "Log an item" form ("Log any fuel you ate outside the plan"), and the "also logged" list ("Fuel also logged today outside the plan").
  - **Burn** section (accent-edged card, orange): present with an empty state ("Exercise that burns calories." / "No burn logged for this day yet — activity logging is coming soon.") and a `−0 kcal` summary, so the fuel/burn split reads coherently now and exercise items can slot in cleanly next.
- **Reusable treatment.** `FuelBurnIcon` (themed emoji chip) + `FuelBurnHeading` (icon + "Fuel"/"Burn" title with colored accent and optional trailing slot for a count/summary). `FuelBurnKind` (`'fuel' | 'burn'`) is exported for the next task. The internal `FUEL_BURN_META` map is kept un-exported so the file only exports components/types (satisfies the `react-refresh/only-export-components` lint rule).
- **Scope restraint.** Did not re-label the source-specific thumbnails (🍽️/🍳/🍪) on individual ad-hoc entries — keeping the per-item source distinction is more informative, and the section-level fuel framing already carries the concept. Did not touch other pages (Meals/Recipes/etc.) since the fuel/burn split is specific to The Log; over-reaching there would add noise without clarity.

**Files created**
- `src/web/src/components/FuelBurnIcon.tsx` — reusable `FuelBurnIcon`, `FuelBurnHeading`, and `FuelBurnKind` type with per-kind icon/label/accent metadata.

**Files changed**
- `src/web/src/index.css` — added `.fb-icon`, `.fb-title`, `.fb-heading`, and `.card.fb-section` styles (fuel = emerald, burn = flame-orange accents) under a new "Fuel / Burn vocabulary" block.
- `src/web/src/pages/LogPage.tsx` — applied `FuelBurnHeading` + `fb-section fb-fuel` to the plan-tasks, "Log an item", and "also logged" cards with fuel-worded copy; added a new `fb-section fb-burn` Burn card with an empty state.
- `TODO.md` — removed the completed task line.

**Verification**
- `dotnet test -c Release` → **190/190 passed** (no backend changes; suite confirmed green).
- `npm run build` (`src/web`) → **succeeds** (tsc + vite, 77 modules).
- `npm run lint` (`src/web`) → **0 errors** (1 pre-existing `AuthContext.tsx` warning only; no new warnings/errors).

### Scheduled Weight Measurements — 2026-06-10 CDT

- [x] Users can configure a recurring weight-measurement schedule (daily / weekly / every N days / every N weeks, anchored on a chosen start day-of-week). A "Log your weight" task now appears in The Log only on days a measurement is due, showing complete when a weight has been recorded for that day.

**NOTES:**

**Design decisions**
- **Schedule model — scalars on `AppUser` + a pure value object.** Added `WeightScheduleCadence { Daily, Weekly, EveryNDays, EveryNWeeks }` (in `Enums.cs`) and three nullable scalars on `AppUser`: `WeightScheduleCadence?`, `WeightScheduleInterval` (int?), `WeightScheduleStartDate` (DateOnly?). No owned/complex type — chosen to keep Cosmos mapping trivial (cadence persists by string name via `HasConversion<string?>()`, consistent with `PrimaryGoal`/`SexAtBirth`; interval/start-date are scalars both providers map natively). The **start date encodes the chosen "day to start measurements"** via its `DayOfWeek`, and is also the recurrence anchor for the "every N" cadences. `AppUser.HasWeightSchedule` is true once cadence + start date are set.
- **"Due on date D?" — a pure, unit-testable rule.** New value object `WeightSchedule` (`src/GoalFueled.Domain/Entities/WeightSchedule.cs`) with `IsDueOn(DateOnly)`. Never due before the start date. Per cadence: **Daily** → every day; **Weekly** → matches the start's day-of-week each week; **EveryNDays** → `(D - start).Days % N == 0` (anchored on the start, so due on the start and every Nth day); **EveryNWeeks** → day-of-week must match the start AND the whole-week offset `((D - start).Days / 7) % N == 0`. A non-positive interval is treated as 1 (no divide-by-zero). `WeightSchedule.FromUser(AppUser)` bridges the persisted scalars back to the rule (null when unconfigured; missing interval defaults to 1).
- **Completion reuses the existing one-per-day weight model (no parallel store).** A due day's task is complete iff a `WeightEntry` already exists for that date — `ConsumptionService` reads `IWeightEntryRepository.GetForUserAndDateAsync`. No new persistence for completion.
- **Surfaced as a sibling task on the existing planned-day endpoint** rather than a new endpoint, to keep The Log coherent. `PlannedDayDto` gained an optional `WeightTask` (`WeightTaskDto(Done, RecordedValue?, RecordedUnit?)`), populated for **any** day a measurement is due — independent of whether the user has an active meal plan (the early-return no-plan branch still computes it). It is `null` on non-due days so the task only appears when due.
- **Completing the task from The Log** links to the existing Weight page (`/weight?returnTo=/log`) for inline-quick entry — simplest and consistent (the page already upserts one entry per day and honors `returnTo`). On return, The Log refetches and the task shows done.
- **Where it is edited:** a new "Weight measurement schedule" card on the Profile page (`WeightScheduleEditor` component), exposed via a dedicated endpoint and echoed on `/api/me`.

**New / changed API endpoints**
- `PUT /api/me/weight-schedule` — **new**; body `WeightScheduleInput(Cadence?, StartDate?, Interval=1)`; returns the updated `CurrentUserDto`. Null cadence clears the schedule; start date defaults to today; interval validated (≥1, ≤365) only for the "every N" cadences (forced to 1 otherwise).
- `GET /api/me` — `CurrentUserDto` now also returns `WeightSchedule` (`WeightScheduleDto(Cadence, StartDate, Interval, StartDayOfWeek)`), null when unconfigured.
- `GET /api/consumption/planned[?date=]` — `PlannedDayDto` now also returns `WeightTask` (`WeightTaskDto(Done, RecordedValue?, RecordedUnit?)`), present only on due days.

**Files changed (backend)**
- `src/GoalFueled.Domain/Enums.cs` — added `WeightScheduleCadence`.
- `src/GoalFueled.Domain/Entities/AppUser.cs` — added the three schedule scalars + `HasWeightSchedule`.
- `src/GoalFueled.Domain/Entities/WeightSchedule.cs` — **new** pure value object (`IsDueOn`, `FromUser`).
- `src/GoalFueled.Domain/Contracts/UserDtos.cs` — extended `CurrentUserDto`; added `WeightScheduleDto`, `WeightScheduleInput`.
- `src/GoalFueled.Domain/Contracts/ConsumptionDtos.cs` — extended `PlannedDayDto`; added `WeightTaskDto`.
- `src/GoalFueled.Data/GoalFueledDbContext.cs` — `WeightScheduleCadence` persisted by string name.
- `src/GoalFueled.Api/Common/InputLimits.cs` — added `WeightScheduleIntervalMax = 365`.
- `src/GoalFueled.Api/Services/UserProfileService.cs` — added `SetWeightScheduleAsync` (clear/validate/persist).
- `src/GoalFueled.Api/Services/ConsumptionService.cs` — now depends on `IWeightEntryRepository`; `GetPlannedDayAsync` computes the weight task (both plan/no-plan branches) via `ResolveWeightTaskAsync`.
- `src/GoalFueled.Api/Functions/UserFunctions.cs` — new `SetWeightSchedule` endpoint; `ToDto` maps the schedule.
- No `Program.cs`/DI changes needed (`IWeightEntryRepository` already registered; `ConsumptionService` resolves it from the container).

**Files changed (frontend)**
- `src/web/src/api/types.ts` — `WeightScheduleCadence`, `WeightSchedule`, `WeightScheduleInput`, `WeightTask`; `CurrentUser.weightSchedule`; `PlannedDay.weightTask`; `WEIGHT_SCHEDULE_CADENCE_LABELS`.
- `src/web/src/api/services.ts` — `userApi.setWeightSchedule(...)`.
- `src/web/src/components/WeightScheduleEditor.tsx` — **new** Profile-page editor (enable toggle, cadence, interval for the "every N" cadences, start date with day-of-week hint; save/clear via `setWeightSchedule` + auth `refresh`).
- `src/web/src/pages/ProfilePage.tsx` — renders `<WeightScheduleEditor />`.
- `src/web/src/pages/LogPage.tsx` — renders a "Weigh-in" card with the recurring "Log your weight" task (only when `planned.weightTask` is present), showing done/recorded state and a "Log weight"/"Update" link to `/weight?returnTo=/log`.

**Tests**
- `tests/GoalFueled.Tests/WeightScheduleTests.cs` — **new**, 14 tests covering the pure `IsDueOn` rule across all four cadences (start-day anchoring, before-start, weekly day-of-week match, every-N-days N=3 and N=1, every-N-weeks N=2 incl. day-of-week requirement and N=1, non-positive interval), plus the `FromUser` bridge.
- `tests/GoalFueled.Tests/UserProfileServiceTests.cs` — added 5 schedule-persistence tests (persist every-N-weeks; default start-date/interval; null clears; invalid interval rejected; unknown cadence rejected).
- `tests/GoalFueled.Tests/PlannedLogTests.cs` — added 4 weight-task tests (no schedule → no task; appears on a due day and is not done until recorded; done with recorded value/unit when a weight exists; absent on a non-due day, present again the next matching week). Updated its `Ctx`/setup and the three `new ConsumptionService(...)` call sites (in `PlannedLogTests`, `ConsumptionServiceTests`, `GoalServiceTests`) for the new `IWeightEntryRepository` dependency. `DependencyInjectionTests` needed no change (registrations unchanged).

- Verified: `dotnet test -c Release` → **190/190 passed**; web `npm run build` ✓; `npm run lint` → **0 errors** (1 pre-existing `AuthContext.tsx` warning; no new warnings).

### Redesign `The Log` — 2026-06-10 CDT

- [x] Renamed "Food Log" to "The Log" everywhere in the UI; it now follows along with the meal plan the user is currently following — that plan's meals for the day appear as checkable tasks that record real consumption when completed, while ad-hoc free-form/catalog logging remains available alongside.

**NOTES:**

**Design decisions**
- **Persisting "currently-following plan":** stored as a new nullable scalar `AppUser.ActiveMealPlanId` (chosen over a separate settings entity — simplest robust option, matching the existing profile/`AppUser` pattern). Exposed on `CurrentUserDto` (so the client knows the current selection) and set via a new `PUT /api/me/active-plan` endpoint (delegating to `UserProfileService.SetActiveMealPlanAsync`, which validates the plan is visible to the user via `MealPlanService.GetAsync`, or clears it when null/blank). It is intentionally NOT a hard foreign key — a stale/deleted plan id simply yields no tasks.
- **Deriving "today's planned meals":** added `MealPlanService.GetPlannedMealsForDateAsync(user, planId, date)` which reuses the existing resolve/nutrition pipeline (no duplicated nutrition math — same `ItemNutrition`/`MealNutritionCalculator.ComputeTotal` the plan summary uses). It mirrors the summary's day model exactly: a **Daily** plan contributes all scheduled meals; a **Weekly** plan contributes meals pinned to the date's day-of-week **plus** day-less scheduled meals (which apply every day). **Untimed *meal* items** appear as "anytime" tasks (no time); **untimed *ingredient* items** (e.g. water) are not meals and are excluded from tasks (they still count in the plan's own nutrition summary). Returns empty if the plan is missing/not visible.
- **Modeling task completion / correlation:** added an optional `ConsumptionEntry.PlannedMealRef` (string). Completing a task creates a normal `ConsumptionEntry` (`Source=Meal`, the plan's `MealId`, `Servings=1`) tagged with a stable correlation key `planId:mealId:day:time` (`MealPlanService.PlannedMealRef(...)`). The Log matches a task to its completion entry by this ref so it renders checked and can be toggled off (which deletes the entry). **No double-counting:** the ref only correlates — nutrition rolls up through the normal per-entry path with zero special-casing, and completion is **idempotent** (re-completing the same ref+date returns the existing entry instead of logging a duplicate). A separate ad-hoc log of the same meal is a genuinely separate intake and counts on its own (verified by test); only ref-tagged entries match a task. The Log's "Also logged" section hides ref-tagged entries (shown as tasks) but the day total still includes everything.
- **Provider-agnostic:** `ActiveMealPlanId` and `PlannedMealRef` are simple nullable strings (scalars) — no DbContext mapping changes needed; Cosmos and the in-memory provider handle them natively. Date filtering for completions reuses the existing in-memory `ListForUserAndDateAsync`.

**New / changed API endpoints**
- `PUT /api/me/active-plan` — **new**; body `ActiveMealPlanInput(MealPlanId?)`; returns the updated `CurrentUserDto`. Null/blank clears the selection.
- `GET /api/me` — `CurrentUserDto` now also returns `ActiveMealPlanId`.
- `GET /api/consumption/planned[?date=]` — **new**; returns `PlannedDayDto(Date, PlanId?, PlanName?, Tasks[])` where each `PlannedMealTaskDto` has `Ref, MealId, MealName?, Time?, Nutrition, Done, CompletedEntryId?`.
- `POST /api/consumption/planned/complete` — **new**; body `CompletePlannedMealInput(Ref, Date?)`; records (idempotently) the completion entry, returns the `ConsumptionEntryDto`.
- `POST /api/consumption/planned/uncomplete` — **new**; body `CompletePlannedMealInput(Ref, Date?)`; removes the completion entry (204).
- `ConsumptionEntryDto` now also carries `PlannedMealRef?`.

**Files changed (backend)**
- `src/GoalFueled.Domain/Entities/AppUser.cs` — added `ActiveMealPlanId?`.
- `src/GoalFueled.Domain/Entities/ConsumptionEntry.cs` — added `PlannedMealRef?`.
- `src/GoalFueled.Domain/Contracts/UserDtos.cs` — extended `CurrentUserDto`; added `ActiveMealPlanInput`.
- `src/GoalFueled.Domain/Contracts/ConsumptionDtos.cs` — extended `ConsumptionEntryDto`; added `PlannedMealTaskDto`, `PlannedDayDto`, `CompletePlannedMealInput`.
- `src/GoalFueled.Api/Services/MealPlanService.cs` — added `GetPlannedMealsForDateAsync`, the `PlannedMealRef` key builder, `CanView` helper, and a `BuildTask` mapper (reusing existing nutrition logic).
- `src/GoalFueled.Api/Services/ConsumptionService.cs` — now depends on `MealPlanService`; added `GetPlannedDayAsync`, `CompletePlannedMealAsync`, `UncompletePlannedMealAsync`; `ToDtoAsync` includes the ref; sets the ref on completion entries.
- `src/GoalFueled.Api/Services/UserProfileService.cs` — now depends on `MealPlanService`; added `SetActiveMealPlanAsync`.
- `src/GoalFueled.Api/Functions/UserFunctions.cs` — new `SetActiveMealPlan` endpoint; `ToDto` includes `ActiveMealPlanId`.
- `src/GoalFueled.Api/Functions/ConsumptionFunctions.cs` — new `GetPlannedDay`, `CompletePlannedMeal`, `UncompletePlannedMeal` endpoints.
- No `Program.cs`/DI changes needed (all services already registered; `ConsumptionService`/`UserProfileService` simply resolve the already-registered `MealPlanService`).

**Files changed (frontend)**
- `src/web/src/api/types.ts` — `CurrentUser.activeMealPlanId`; `ConsumptionEntry.plannedMealRef`; new `PlannedMealTask`, `PlannedDay`, `CompletePlannedMealInput`.
- `src/web/src/api/services.ts` — `userApi.setActivePlan(id|null)`; `consumptionApi.plannedDay/completePlanned/uncompletePlanned`.
- `src/web/src/components/Layout.tsx` — nav label "Food Log" → "The Log".
- `src/web/src/pages/LogPage.tsx` — rewritten: title "The Log", a "Plan you're following" picker (persists via `setActivePlan` + auth `refresh`), a "Today's plan" task list with checkboxes (showing meal name, scheduled time, kcal, and a done count) wired to complete/uncomplete, and the existing ad-hoc "Log an item" form + "Also logged" list (ref-tagged completions filtered out of that list) + day-total nutrition retained. Route stays `/log`.

**Tests**
- `tests/GoalFueled.Tests/PlannedLogTests.cs` — **new**, 12 tests: active-plan selection persists & clears; rejects a non-visible plan; no-active-plan yields empty planned day; daily plan lists all scheduled meals; weekly plan lists only today's pinned + day-less meals; untimed meal items appear (ingredient items do not); completing records a `Source=Meal` 1-serving entry tagged with the ref and marks the task done (day total counts it once); completing twice is idempotent (no double count); un-completing removes the entry and resets the day total; a completed planned meal + an ad-hoc log of the same meal count separately; completing rejects an unknown ref / a missing active plan.
- Updated setups in `ConsumptionServiceTests.cs`, `GoalServiceTests.cs`, `UserProfileServiceTests.cs` for the new `MealPlanService` dependency. `DependencyInjectionTests` needed no change (registrations unchanged).

- Verified: `dotnet test -c Release` → **168/168 passed**; web `npm run build` ✓; `npm run lint` → **0 errors** (1 pre-existing `AuthContext.tsx` warning; no new warnings).

### Capture basic info during first login + Profile page — 2026-06-10 CDT

- [x] First login now captures Sex at Birth, Height, Current Weight, and Goal one time; a new Profile page (`/profile`) lets users update them later.

**NOTES:**

**Design decisions**
- **New `SexAtBirth` enum (`Male`/`Female`)** in `Enums.cs`, stored as a nullable `SexAtBirth? SexAtBirth` on `AppUser` (null until captured). Persisted by its string name via `HasConversion<string?>()` in `GoalFueledDbContext` (consistent with how `PrimaryGoal` round-trips); `PrimaryGoal` was given the same explicit string conversion so both onboarding enums store readably in the Cosmos document.
- **Height stored canonically in centimeters** (`double? HeightCm` on `AppUser`). The UI defaults to a friendly **ft/in** entry (two number inputs) with a **cm** toggle; conversions live in `src/web/src/lib/height.ts` (`cmToFtIn`/`ftInToCm`/`formatHeight`). Storing one canonical unit avoids unit ambiguity server-side and keeps validation simple (positive, ≤ `InputLimits.HeightCmMax` = 300 cm).
- **Current weight is NOT stored on the profile.** Onboarding/profile-save record it as **today's `WeightEntry`** via the existing `WeightService.RecordAsync` (single source of truth). This immediately satisfies the meal-plan weight gate and weight goals, and reuses the existing per-day upsert + value/unit validation. Weight is therefore captured (value + unit) but lives only in the weight feature. On the Profile page the current-weight field is **optional** — blank leaves the last recorded weight untouched; a value upserts today's entry.
- **`HasCompletedOnboarding` is set true only after all required fields validate and persist.** The weight is recorded first inside `CompleteOnboardingAsync`; if any validation throws, onboarding is left incomplete (verified by a test).
- **Onboarding logic moved into a new `UserProfileService`** (was inline in `UserFunctions`). This keeps the Function endpoints thin and makes onboarding/profile-update unit-testable, matching the Weight/Goals/UserAdmin service-layer pattern. It depends on `IUserRepository` + `WeightService`.

**New / extended API endpoints**
- `POST /api/me/onboarding` — now takes `OnboardingInput(PrimaryGoal, SexAtBirth, HeightCm, CurrentWeight, WeightUnit)` (was goal-only).
- `PUT /api/me/profile` — **new**; takes `ProfileInput(PrimaryGoal, SexAtBirth, HeightCm, CurrentWeight?, WeightUnit)`.
- `GET /api/me` — `CurrentUserDto` now also returns `SexAtBirth?` and `HeightCm?`.

**Files changed (backend)**
- `src/GoalFueled.Domain/Enums.cs` — added `SexAtBirth` enum.
- `src/GoalFueled.Domain/Entities/AppUser.cs` — added `SexAtBirth?` and `HeightCm?`.
- `src/GoalFueled.Domain/Contracts/UserDtos.cs` — extended `CurrentUserDto` + `OnboardingInput`; added `ProfileInput`.
- `src/GoalFueled.Data/GoalFueledDbContext.cs` — string-name conversions for `PrimaryGoal`/`SexAtBirth`.
- `src/GoalFueled.Api/Common/InputLimits.cs` — added `HeightCmMax = 300`.
- `src/GoalFueled.Api/Services/UserProfileService.cs` — **new** service (onboarding + profile update + validation).
- `src/GoalFueled.Api/Functions/UserFunctions.cs` — onboarding delegates to the service; new `UpdateProfile` endpoint; `ToDto` extended.
- `src/GoalFueled.Api/Program.cs` — registered `UserProfileService`.

**Files changed (frontend)**
- `src/web/src/api/types.ts` — `SexAtBirth` union, `SEX_LABELS`, extended `CurrentUser`, new `OnboardingInput`/`ProfileInput`.
- `src/web/src/api/services.ts` — `completeOnboarding(input)` (full payload) + new `updateProfile(input)`.
- `src/web/src/lib/height.ts` — **new** cm⇄ft/in helpers.
- `src/web/src/components/ProfileFields.tsx` — **new** shared fields component (sex, height w/ unit toggle, weight w/ unit, goal) used by both pages.
- `src/web/src/pages/WelcomePage.tsx` — rewritten to capture all four fields and submit the full onboarding payload.
- `src/web/src/pages/ProfilePage.tsx` — **new** `/profile` page.
- `src/web/src/App.tsx` — added the `/profile` route.
- `src/web/src/components/Layout.tsx` — added the Profile nav link.

**Tests**
- `tests/GoalFueled.Tests/UserProfileServiceTests.cs` — **new**, 9 tests: onboarding persists all fields + marks complete, onboarding records today's weight, invalid weight/height/enum rejected (and leaves onboarding incomplete), profile update changes fields, optional weight (omitted → no entry; provided → upserts today), invalid height on update rejected.
- `tests/GoalFueled.Tests/DependencyInjectionTests.cs` — registered + asserted `UserProfileService`.

- Verified: `dotnet test -c Release` → **156/156 passed**; web `npm run build` ✓; `npm run lint` → 0 errors (1 pre-existing `AuthContext.tsx` warning).

### Time picker restricted to 15-minute intervals — 2026-06-09 CDT

- [x] Meal/plan time pickers now only offer minutes in 15-minute intervals (:00/:15/:30/:45) via a reusable TimePicker component

**NOTES:**
- **Approach:** a single reusable `<select>` component (`components/TimePicker.tsx`) with 96 options spanning 00:00…23:45 in 15-min steps. Chosen over hour+minute selects or native `step={900}` because a select is deterministic across browsers (native `<input type="time">` `step` only affects validation/spinner granularity, not the dropdown UX), and avoids hour/minute reconciliation. Options show friendly 12-hour labels ("8:30 AM") while values stay machine-format.
- **Replaced both (and only) `type="time"` inputs in `src/web/src`** (grep-confirmed, both in `pages/PlanFormPage.tsx`): the "Add a meal to multiple days" form time (`multiTime`) and the per-row time inside the shared `ScheduledMealFields` (`row.time`). No other page uses a time input.
- **Format/blank preserved:** value prop and onChange emit the same `"HH:mm"` strings wired to the same state; the page's existing `"HH:mm:ss"`↔`"HH:mm"` load/save conversion is untouched, so round-trip is unchanged. Untimed state preserved via a blank first option (`Anytime` → empty string).
- **Legacy off-grid times:** a non-empty value not on a 15-min boundary (e.g. older `08:20`) is rendered as an extra selectable option labeled "… (custom)" — nothing is snapped or dropped, so editing an old plan can't lose its saved time.
- No CSS changes (reuses existing `.select`); theme files untouched.
- Verified: web `npm run build` ✓; `npm run lint` 0 errors (1 pre-existing `AuthContext.tsx` warning).

### Add a meal to multiple days at once — 2026-06-09 CDT

- [x] The Weekly meal-plan editor can add the same meal to several days at once (meal + optional time + multi-day chips); each created day-entry is independent and its time can be adjusted separately afterward

**NOTES:**
- Frontend-only, no backend changes.
- `pages/PlanFormPage.tsx`: new "Add a meal to multiple days" form at the top of the Weekly timeline card (Weekly cadence only, hidden when read-only): a meal `<select>`, the existing `<input type="time">` (optional → untimed), and a Sun→Sat row of toggle chips (checkbox-backed labels, `.on` when selected). "Add to N days" button is disabled until a meal + ≥1 day are chosen. `addScheduledToMultipleDays` pushes one freshly-constructed `ScheduledRow` per checked day, each with its own unique `key` and value-copied meal/time strings — so re-timing or removing one day's entry never affects the others (verified: per-row edits go through `updateScheduled(key, patch)` with an immutable `.map`). After adding, the day selection clears but meal/time persist for quick repeat batches.
- **Single-day path kept:** the per-day "+ Add meal to {day}" quick buttons are untouched; the multi-day form is additive above them.
- Deliberately reused the existing time input (the 15-minute-interval picker is the next task, to be refined centrally).
- `index.css`: additive `.plan-multi-add`/`.plan-multi-controls`/`.plan-multi-days`/`.plan-day-chip`(`.on`) using existing CSS vars; theme files untouched.
- Verified: web `npm run build` ✓; `npm run lint` 0 errors (1 pre-existing `AuthContext.tsx` warning).

### Better UI for defining weekly meal plans (drag-reorderable timeline) — 2026-06-09 CDT

- [x] The Weekly meal-plan editor is now a day-grouped timeline (meals ordered by time, untimed grouped as "Anytime"), with drag-up/down to reorder within a day

**NOTES:**
- Frontend-only; **no backend changes** (`dotnet test` still 147/0).
- `pages/PlanFormPage.tsx`: Weekly cadence renders a "Weekly timeline" card — one section per day (Sun→Sat per the app's existing `DAYS_OF_WEEK`), plus an "Any day" bucket for day-less meals. Within a day: earliest time first, untimed ("Anytime") grouped at the end; each row has a drag handle, time marker, and the meal/day/time editors. Per-day "+ Add meal to {day}" button pins the new row to that day; the row's day selector still lets a meal move days (re-buckets live). Daily cadence keeps the original flat editor. Nutrition summary, per-day breakdown, untimed-items card, save/delete, and weight gate untouched.
- **Drag mechanism:** native HTML5 DnD (`draggable` + onDragStart/Over/Drop/End), **no new dependency**; `⠿` handle, grab cursor, dragging opacity, drop indicator + end-of-day drop zone; constrained to within one day.
- **Data-model decision (least-invasive, no schema change):** `ScheduledMeal` has only `MealId`/`DayOfWeek?`/`Time?` — no order field; `MealPlanService` preserves array order verbatim and the roll-up is order-independent. The timeline sorts each day by time with **array order as tiebreaker**; on drop, a timed meal's time is **snapped to its new neighbor** so the time-sort lands it in the dropped slot, and untimed meals reorder purely by array order. Persists via the existing array-order + time round-trip with zero backend/test changes. Extracted a shared `ScheduledMealFields` component (factored for the upcoming multi-day-select + 15-min-picker tasks).
- `index.css`: purely additive `.plan-day*`/`.plan-timeline`/`.plan-tl-*` styles using existing CSS vars (theme files untouched).
- **Limitations:** mouse/pointer drag only (no touch/keyboard); dragging a timed meal adjusts its time to slot it (a consequence of having no persisted order field).
- Verified: `dotnet test -c Release` 147/0; web `npm run build` ✓; `npm run lint` 0 errors (1 pre-existing `AuthContext.tsx` warning).

### Go back to creating meal plan after entering weight — 2026-06-09 CDT

- [x] After the new-meal-plan weight gate sends you to the weight page, recording your weight now returns you to creating the plan instead of stranding you on the weight page

**NOTES:**
- `pages/PlanFormPage.tsx`: the gate's "Record your weight first" link changed from `to="/weight"` to `to="/weight?returnTo=/plans/new"` (carries the new-plan route, served by `/plans/:id` with id `new`).
- `pages/WeightPage.tsx`: reads `returnTo` via `useSearchParams`; after the existing successful `record()` path it does `if (returnTo) navigate(returnTo)`. A `safeReturnTo()` helper only honors app-internal relative paths (single leading `/`, rejects `//host` protocol-relative and any scheme), preventing open redirects.
- **Behavior preserved for direct visitors:** the redirect is fully gated behind `if (returnTo)`, so visitors who go to `/weight` directly (no param) keep the existing behavior — form resets, stay on page.
- **Round-trip re-passes the gate:** `navigate('/plans/new')` re-mounts `PlanFormPage`, whose weight-gate `useEffect` re-runs `weightApi.list()` on mount; the just-recorded today entry is found, so the form renders instead of the gate card.
- **Mechanism choice:** query param (not router state) — simplest/robust, survives reload, matches the codebase's plain react-router usage.
- Verified: web `npm run build` ✓; `npm run lint` 0 errors (1 pre-existing `AuthContext.tsx` warning).

### Reflect actual consumption in nutrient-goal progress — 2026-06-09 CDT

- [x] Nutrient-goal progress now uses actual logged consumption (per day), falling back to planned meal-plan intake only for days with no log entries

**NOTES:**
- **Approach (decision):** *actual-overrides-planned, per day, with planned fallback.* For each calendar day in the goal's active period: if the day has any `ConsumptionEntry`, use that day's summed logged nutrition for the goal's nutrient; otherwise fall back to the planned daily intake (existing per-day plan roll-up). Reflects reality as the user logs it while still projecting un-logged days. Cleanly generalizes the old planned-only behavior (nothing logged ⇒ identical projection) and now also tracks a goal with **no meal plan** when the user logs consumption.
- **Aggregation/merge:** added `ConsumptionService.DailyNutritionInRangeAsync(user, from, to)` → per-day nutrient sums, reusing the exact per-entry nutrition computation the day view uses (refactored the entry switch into shared `ComputeWithSourceAsync`/`ComputeNutritionAsync`), so actual and planned share basis + nutrient ids. Added `IConsumptionEntryRepository.ListForUserInRangeAsync` (same materialize-then-filter-in-memory `DateOnly` pattern as the per-day method). `GoalService.ComputeNutrientProgressAsync` now derives the inclusive day window via a new `PeriodWindow(goal)` helper (recurring: period containing today anchored at start; one-time: start→target inclusive); `PeriodDays` delegates to it so the window length — and the planned-only projection — is byte-for-byte unchanged.
- **DTO/UI:** `GoalProgressDto` gained `bool BasedOnLoggedConsumption` (default false; set true only when a nutrient goal incorporates ≥1 logged day). `types.ts` mirrors it; `GoalProgressVisual.tsx` shows a minimal "📋 based on logged consumption" hint (with tooltip) only when true. No theme files touched.
- **No-regression guarantees:** weight goals never touch consumption (`ComputeWeightProgressAsync` unchanged, flag defaults false); no-consumption case yields `plannedDaily * PeriodDays` exactly as before with flag false (covered by an explicit test); "no data" UX preserved (now also satisfiable by logged consumption alone).
- DI unchanged (`GoalService` ctor injects the already-registered `ConsumptionService`).
- **Tests:** `GoalServiceTests` +5 (actual-overrides-planned, planned fallback for unlogged days, pure-actual when fully logged, no-regression when nothing logged, consumption-only with no plan) + updated setup.
- Verified (subagent): `dotnet test -c Release` → 147 passed/0 failed (142 + 5); web `npm run build` ✓; `npm run lint` 0 errors (1 pre-existing `AuthContext.tsx` warning).

### Add automatic lookup of ingredient nutrients — 2026-06-09 CDT

- [x] When creating/editing an ingredient, a "Look up nutrition" button fetches nutrition from USDA FoodData Central by name and pre-fills the (still-editable) nutrient fields

**NOTES:**
- **New API endpoint:** `GET /api/ingredients/nutrition-lookup?query=<name>` (authenticated, existing `RunAuthenticated`/`HttpResults`/`ApiException` conventions), registered before the `ingredients/{id}` route so the literal segment wins. Returns `NutritionLookupResultDto { matchedName, source, basisGrams (100), nutritionPer100g[] }`.
- **External source (server-side):** USDA FoodData Central `/foods/search`, called from the backend via `IHttpClientFactory` (`AddHttpClient<IFdcNutritionClient, FdcNutritionClient>`, 15s timeout) so the key stays off the browser and there's no CORS issue. Config (both optional with defaults): `GoalFueled:Nutrition:FdcApiKey` → default `DEMO_KEY` (USDA's public rate-limited key; no personal key required), `GoalFueled:Nutrition:FdcBaseUrl` → default `https://api.nal.usda.gov/fdc/v1/`. Documented in `README.md`.
- **Mapping:** `FdcNutrientMap` keys on the stable USDA nutrient **number** (e.g. 203=protein, 208=energy) → app catalog id (~45 mapped; untracked ignored). Units reconciled (G/MG/UG/KCAL/IU) via a microgram base to each nutrient's canonical unit; incompatible/IU-vs-mass readings dropped rather than misstored. USDA reports per 100 g and the app stores per 100 g → 1:1 after unit reconciliation. `NutritionLookupService` parses/maps/dedups (first valid reading wins), preserving catalog order.
- **Fails gracefully:** client swallows network errors/timeouts/non-success/malformed bodies to null (logged); service throws a friendly NotFound ("enter values manually"); empty query → validation error. Lookup never blocks manual entry and never auto-saves — it only pre-fills editable fields (basis set to 100 g).
- **Frontend:** `IngredientFormPage.tsx` adds a "Look up nutrition" button next to Name (disabled while loading or name empty) with loading/error/success notices reusing existing classes; `api/types.ts` `NutritionLookupResult`; `api/services.ts` `ingredientApi.nutritionLookup(query)`; one `.lookup-row` CSS rule (no theme files touched).
- **Tests:** `NutritionLookupServiceTests` (12 cases) using a faked `IFdcNutritionClient` + sample USDA JSON (NO network): mapping, g→mg conversion, catalog ordering, dropping untracked/negative/bad-unit, dedup, trimming, and all failure modes; `DependencyInjectionTests` updated.
- New files: `Domain/Contracts/NutritionLookupDtos.cs`, `Api/Services/Nutrition/{FdcModels,IFdcNutritionClient,FdcNutritionClient,FdcNutrientMap,NutritionLookupService}.cs`, `tests/.../NutritionLookupServiceTests.cs`.
- Verified (subagent): `dotnet test -c Release` → 142 passed/0 failed (130 + 12); web `npm run build` ✓; `npm run lint` 0 errors (1 pre-existing `AuthContext.tsx` warning).

### Make `+ New` a hyperlink on the Recipes page — 2026-06-09 CDT

- [x] The `/recipes` empty-state message now links the "+ New" text to the new-recipe page instead of rendering it as plain bold text

**NOTES:**
- `pages/RecipesPage.tsx`: replaced `<strong>+ New</strong>` in the "No recipes found. Create your first one with …" message with `<Link to="/recipes/new">+ New</Link>`, reusing the already-imported react-router `Link` (same nav approach as the page's header "+ New" button and recipe cards). No new styles introduced; surrounding sentence text unchanged.
- Route confirmed: there is no literal `/recipes/new` route — `App.tsx` serves the form via the dynamic `/recipes/:id` route where `new` is the `:id` param; `/recipes/new` is the established convention (matches the header button).
- Verified: web `npm run build` ✓; `npm run lint` 0 errors (1 pre-existing `AuthContext.tsx` warning).

### Add tracking of ad-hoc meals (daily consumption log) — 2026-06-09 CDT

- [x] New per-user **consumption log** vertical slice: quickly log what was actually eaten on a given day — an existing catalog **Meal** or **Recipe** (with a servings quantity, nutrition computed live from the source) and/or a **free-form** item (name + optional calories) — view a day's entries with rolled-up nutrition totals, and remove an entry.

**NOTES:**
- Built following the WeightEntry/Goals pattern across all layers.
  - Domain: `Entities/ConsumptionEntry.cs` (OwnerUserId-scoped; `DateOnly Date`, `ConsumptionSource Source`, `SourceId?`, `Name`, `Servings`, inline `List<NutrientAmount> Nutrition`, timestamps). New enum `ConsumptionSource { Meal, Recipe, FreeForm }` in `Enums.cs`. `Contracts/ConsumptionDtos.cs` (`ConsumptionEntryDto`, `ConsumptionDayDto`, `ConsumptionEntryInput`).
  - Data: `IConsumptionEntryRepository`/`ConsumptionEntryRepository` (ListForUserAndDate, GetById, Add, Delete; in-memory date filtering for provider-agnostic `DateOnly`), `ConsumptionEntries` DbSet + Cosmos container mapping (`OwnsMany(Nutrition)`), DI registration.
  - Api: `Services/ConsumptionService.cs` (per-day list + totals, log, remove; live nutrition roll-up), `Functions/ConsumptionFunctions.cs` (`GET /consumption[?date=]`, `POST /consumption`, `DELETE /consumption/{id}`). `InputLimits.ConsumptionServingsMax`. Registered in `Program.cs`.
  - Web: `pages/LogPage.tsx` ("Food Log" page: date picker, add-item form with Meal/Recipe/Free-form type selector + catalog dropdown or name+calories, servings, day entries with remove, day-total `NutritionLabel`), `/log` route in `App.tsx`, 🍪 "Food Log" nav entry in `Layout`, `consumptionApi` + consumption types/labels in `api/`.

- **Design decisions / assumptions:**
  - **Data model:** one row per logged item (NOT a per-day upsert like weight) — a day can have many items; logging appends, removal is by id. An entry is discriminated by `Source`: a catalog `Meal`/`Recipe` reference (nutrition computed live) or a `FreeForm` item (nutrition carried inline). `Servings` scales the source/inline nutrition.
  - **Free-form vs catalog:** both supported. Catalog entries reference an existing meal/recipe (so nutrition is exact and stays correct), capturing a **name snapshot** at log time so the log still reads sensibly if the source is later renamed/deleted. Free-form entries require a name and accept optional **per-serving calories** (kept deliberately minimal — the schema stores arbitrary `NutrientAmount`s, but the UI exposes only calories to avoid over-building); a free-form item with no nutrition is allowed (still a useful log line).
  - **Nutrition totals:** computed by **reusing the existing calculators** — `MealNutritionCalculator.ComputeTotal` (whole meal) and `RecipeNutritionCalculator.ComputePerServing` (per recipe serving), each scaled by `Servings` via `NutritionMath.Scale`; the day total is `NutritionMath.Sum` over all entries (same aggregation used everywhere else). If a referenced catalog item is no longer visible/exists, the entry is returned with empty nutrition and `SourceMissing = true` (name still shows) rather than failing the whole day.
  - **Per-user only:** entries are entirely private — not even admins get a cross-user view (mirrors WeightEntry/Goals); remove returns 404 for non-owners. Catalog references are validated against the caller's visibility (own + app-level + public), reusing the Meals/Recipes visibility rules, so a user can't log another user's private recipe.
  - **Goals progress left as-is:** `GoalService` still derives nutrient intake from meal **plans** (planned intake). Folding the consumption log into nutrient-goal progress was deemed not a small/clean change (plans project a per-day average over a period vs. the log being actual dated entries — mixing them cleanly needs a planned-vs-actual design decision), so Goals was intentionally not touched, to avoid breaking the existing Goal tests. Noted here for a future follow-up.
  - **Date:** optional, defaults to today (UTC) server-side; future dates rejected. `GET /consumption?date=yyyy-MM-dd` selects a day (defaults to today).
- **API endpoints added:** `GET /consumption[?date=yyyy-MM-dd]`, `POST /consumption`, `DELETE /consumption/{id}`.
- **Tests:** `ConsumptionServiceTests` (15 cases: log recipe/meal with scaled nutrition, free-form with/without inline nutrition, day totals summed across entries, per-day & default-today scoping, removal, per-user isolation + can't-log-another's-recipe, validation of missing source-id/free-form name/servings/future-date, and source-removed → `SourceMissing`). Updated `DependencyInjectionTests` to mirror the new service + repository registration.
- **Verified:** `dotnet test -c Release` → 130 passed / 0 failed (was 115, +15 new); web `npm run build` ✓; `npm run lint` → 0 errors (1 pre-existing `AuthContext.tsx` warning).

### Define goals and track progress — 2026-06-09 CDT

- [x] New per-user Goals vertical slice: define a weight or nutrient goal (one-time or recurring), with live progress computed from existing data and an SVG progress visual that celebrates/warns based on the positive/negative outcome.

**NOTES:**
- Built following the WeightEntry/Ingredients/Meals pattern across all layers.
  - Domain: `Entities/Goal.cs` (OwnerUserId-scoped; MetricType, NutrientId, TargetValue, Cadence, StartDate, TargetDate, RecurrenceDays, ExceedingIsPositive). Enums `GoalMetricType { Weight, Nutrient }` and `GoalCadence { OneTime, Recurring }` in `Enums.cs`. `Contracts/GoalDtos.cs` (`GoalDto`, `GoalInput`, `GoalProgressDto`, `GoalStatus { InProgress, Achieved, Missed, NoData }`).
  - Data: `IGoalRepository`/`GoalRepository` (ListForUserAsync newest-first, GetById, add/update/delete), `Goals` DbSet + Cosmos container mapping, DI registration.
  - Api: `Services/GoalService.cs` (CRUD + live progress), `Services/GoalNutritionRollup.cs` (per-day nutrient roll-up extracted from the meal-plan summary logic), `Functions/GoalFunctions.cs` (`GET/POST /goals`, `GET/PUT/DELETE /goals/{id}`). `InputLimits.GoalTargetValueMax`/`GoalRecurrenceDaysMax`. Registered in `Program.cs`.
  - Web: `pages/GoalsPage.tsx` (define-goal form + list), `components/GoalProgressVisual.tsx` (dependency-free SVG progress ring), `/goals` route in `App.tsx`, 🎯 nav entry in `Layout`, `goalApi` + Goal types/labels in `api/`.

- **Design decisions / assumptions:**
  - **Per-user only:** goals are entirely private — not even admins get a cross-user view (mirrors WeightEntry). `EnsureOwner` returns 404 for non-owners.
  - **One-time vs recurring:** one-time requires a `targetDate` deadline; recurring requires `recurrenceDays` (period length). For recurring goals the "active period" is the window containing today, anchored at `startDate`; the visual labels when the current period ends. The conflicting field is nulled server-side based on cadence.
  - **Weight progress:** baseline = earliest weight recorded on/after `startDate` (fallback: earliest overall); current = latest weight. Percent = travel from baseline→target, clamped 0..100. Direction (increase/decrease) inferred from target vs baseline, so the same logic handles loss and gain goals.
  - **Nutrient progress:** the app has no ad-hoc consumption log yet, so "actual intake" is derived from the user's **meal plans** (the app's model of planned intake). We sum each plan's per-day average of the target nutrient (reusing the plan daily-average roll-up), then project over the active period (`dailyIntake × periodDays`). One-time nutrient period = start→target span (inclusive); recurring = `recurrenceDays`. Compared against the target from a zero baseline.
  - **Exceeding positive/negative:** `ExceedingIsPositive` drives `OutcomePositive`. Meeting the target is always positive; beyond that, exceeding is celebrated only when the flag is on (e.g. extra weight lost, more protein) and flagged when off (e.g. over a calorie budget). A passed deadline without hitting target = Missed (negative).
  - **Visual celebration:** SVG ring tinted green (positive)/red (negative)/grey (no data), with a reactive emoji (🎉 achieved-positive, ⚠️ achieved-but-overshot-a-budget / off-track, 😞 missed, 🚀 in-progress, 📊 no data), a confetti flourish on a positive achievement, and a tone-colored status message. No new npm dependencies.
  - **NoData states:** weight goal with no entries, or nutrient goal with no meal plan / a plan that doesn't supply the nutrient, report a friendly "add data" prompt instead of fake progress.
- **API endpoints added:** `GET /goals`, `POST /goals`, `GET /goals/{id}`, `PUT /goals/{id}`, `DELETE /goals/{id}`.
- **Tests:** `GoalServiceTests` (13 cases: CRUD/ownership, validation for cadence/nutrient/target/dates, weight progress in-progress/achieved-exceeded/missed, nutrient recurring-projection/over-budget/in-progress, resolved nutrient name+unit). Updated `DependencyInjectionTests` to mirror the new registration.
- **Verified:** `dotnet test -c Release` → 115 passed / 0 failed; web `npm run build` ✓; `npm run lint` 0 errors (1 pre-existing `AuthContext.tsx` warning).

### Add date when logging weight — 2026-06-07 17:35 CDT

- [x] Users can choose the measurement date when logging weight (for historical/back-dated entries); still defaults to today

**NOTES:**
- Backend already accepted an optional `Date` end-to-end (`WeightEntryInput`, `RecordAsync` upsert-by-date), so this was mostly UI plus validation.
- `pages/WeightPage.tsx`: added a `<input type="date">` (defaults to today via existing `todayIso()`, `max={today}` to block future dates) next to weight/unit; `record()` now passes the selected `date`. Replaced the hard-coded `recordedToday` with an `alreadyLogged` flag derived from `entries.some(e => e.date === date)`, so the heading/button show "Update" vs "Record" for the *selected* date and the hint reflects it. Date resets to today after a successful save; local upsert-by-date (sorted desc) places back-dated entries correctly.
- `Services/WeightService.cs`: added defense-in-depth `Validate` rule rejecting a future date (`"Weight date cannot be in the future."`) using `DateOnly.FromDateTime(DateTime.UtcNow)`; far-past dates allowed.
- Tests: `WeightServiceTests` gained past-date-creates-on-that-date, future-date-throws, and same-past-date-twice-updates cases.
- Verified (conductor re-ran): `dotnet test -c Release` → 99 passed/0 failed; web `npm run build` ✓; `npm run lint` 0 errors (1 pre-existing warning in `AuthContext.tsx`).

### Add weight tracking — 2026-06-07 17:10 CDT

- [x] New per-user weight-tracking vertical slice (record daily weight, view history) with a meal-plan creation gate requiring a current weight

**NOTES:**
- Built following the Ingredients/Meals pattern across all layers:
  - Domain: `Entities/WeightEntry.cs` (OwnerUserId, DateOnly Date, Value, WeightUnit Unit, timestamps), `WeightUnit { Pound, Kilogram }` enum, `Contracts/WeightDtos.cs` (`WeightEntryDto`, `WeightEntryInput` with optional Date defaulting to today UTC).
  - Data: `IWeightEntryRepository`/`WeightEntryRepository` (ListForUserAsync most-recent-first, GetForUserAndDateAsync, add/update), `WeightEntries` DbSet + Cosmos container mapping, DI registration.
  - Api: `WeightService` (user-scoped list + upsert-by-day RecordAsync, validation), `WeightFunctions` (`GET /weights`, `POST /weights`); `InputLimits.WeightValueMax = 5000`; registered in `Program.cs`.
  - Web: `pages/WeightPage.tsx` (record form + history), `/weight` route, ⚖️ nav entry in `Layout`, `weightApi` + `WeightUnit`/`WeightEntry`/`WeightEntryInput`/`WEIGHT_UNIT_LABELS` types.
- **Meal-plan weight gate (assumption/decision):** "starting a meal plan" interpreted as creating a NEW meal plan. `PlanFormPage` (new-plan only) requires a weight entry dated **today** (local calendar date). On mount it calls `weightApi.list()`; if none matches today it replaces the form with a blocking "Record your weight first" card linking to `/weight`, and `save()` refuses with an inline error as a backstop. Editing an existing plan is never gated. The check **fails open** if the list request errors (transient failure can't permanently block). Gating is frontend-only.
- **Weight unit modeling:** dedicated `WeightUnit` enum (not the serving-oriented `UnitOfMeasure`), serialized as string; default `Pound`; form pre-selects the user's last-used unit.
- **One-per-day upsert:** uniqueness key (OwnerUserId, Date); recording the same day updates in place (preserves Id/CreatedAt, bumps UpdatedAt). DateOnly filtering done in memory (provider-agnostic, mirrors `CountAdminsAsync`).
- Strictly per-user: no app-level scope, no admin cross-user view.
- Tests: `WeightServiceTests` (create, same-day update-not-duplicate, multi-day ordering, date defaulting, cross-user isolation, validation) + `DependencyInjectionTests` updated.
- Verified (conductor re-ran): `dotnet test -c Release` → 96 passed/0 failed; web `npm run build` ✓; `npm run lint` 0 errors (1 pre-existing warning in `AuthContext.tsx`).

### Move menus to a hamburger menu — 2026-06-07 16:55 CDT

- [x] Removed the persistent bottom navigation bar and moved its links into a hamburger (☰) menu

**NOTES:**
- `components/Layout.tsx`: added `menuOpen` state; a `☰` button in the header (left, next to the brand) toggles a `.nav-drawer` rendered just below the header. The drawer renders the same `NAV` links plus `ADMIN_NAV`, still gated on `profile?.isActingAsAdmin`. Each link closes the drawer on click; `NavLink` active styling preserved. Brand, admin-view toggle, and Sign out untouched.
- `index.css`: replaced `.bottom-nav` rules with `.hamburger` + `.nav-drawer` (sticky below header, active/hover states using existing CSS vars); removed the `.app-shell` `padding-bottom: 64px` and the stale `.bottom-nav` media-query rule.
- Decision: a temporary nav treatment per the task ("until we decide what the UI design will look like").
- Verified: web `npm run build` ✓ and `npm run lint` 0 errors (1 pre-existing warning in `AuthContext.tsx`, unrelated).

### Show whole-recipe & per-serving nutrition side-by-side — 2026-06-07 16:55 CDT

- [x] Recipe form now shows a single nutrition table with separate "Whole recipe" and "Per serving" columns instead of two stacked tables

**NOTES:**
- `components/NutritionLabel.tsx`: extended to a backwards-compatible API — accepts either the legacy `amounts: NutrientAmount[]` prop or a new `columns: { label, amounts }[]` prop. Single-column form is normalized internally to one unlabeled column, so existing callers (`PlanFormPage`, `MealFormPage`) were left unchanged. Rows stay in catalog order (`useNutrients` `ordered`/`byId`) with `core` styling; a `<thead>` with labels renders only when >1 column; missing values per column show a muted dash. Exported `NutritionColumn` interface.
- `pages/RecipeFormPage.tsx`: replaced the two `<NutritionLabel>` tables with one using the `columns` prop ("Whole recipe" / "Per serving") under a single Nutrition heading.
- `index.css`: added minimal `.nutri-table th` / `.nutri-table th.val` header styling.
- Verified: web `npm run build` ✓ and `npm run lint` 0 errors (1 pre-existing warning, unrelated).

### Security & polish — 2026-06-07 14:30 CDT

- [x] Sanitize HTML in ingredient/recipe descriptions before rendering (XSS prevention)
- [x] Server-side input limits (string lengths, collection sizes) with consistent validation messages
- [x] Abuse guard on public-recipe submission (in-app cap)

**NOTES:**
- HTML sanitization: frontend `components/SanitizedHtml.tsx` renders descriptions via DOMPurify (`dompurify` ^3.4.8 added) — used on the Ingredient & Recipe form pages as a read-only preview. Defense-in-depth server-side `Api/Common/HtmlSanitizer.cs` applied in `IngredientService`/`RecipeService` on save.
- Input limits: `Api/Common/InputLimits.cs` applied uniformly across Ingredient/Recipe/Meal/MealPlan create+update — Name ≤ 200, Description ≤ 10,000, ImageUrl ≤ 2,000, Note ≤ 500; collection caps: recipe items ≤ 100, nutrition entries ≤ 200, meal portions ≤ 100, scheduled meals ≤ 200, untimed items ≤ 200. Consistent `ValidationException` messages via `RequireText`/`LimitText`/`LimitCount` helpers.
- Abuse guard: `RecipeService` caps a user at 10 recipes in `PendingReview` at once (`MaxPendingReviewsPerUser`), backed by new `IRecipeRepository.CountPendingReviewByOwnerAsync`; throws a clear ValidationException when exceeded. (Network rate limiting deferred to infra.)
- Tests added in `IngredientServiceTests`/`RecipeServiceTests` (oversized name/description/collections rejected; pending-review cap). 89 total pass.
- NOTE: the implementing subagent hit its session token limit just after finishing, so this entry was recorded by the conductor after independently verifying completeness + green build/tests/lint.
- Verified: `dotnet build -c Release` 0/0; `dotnet test` 89 passed; web build ✓ and lint 0 errors (1 pre-existing warning).

### Admin features & user management — 2026-06-07 10:13 CDT

- [x] Admin user-management: `GET /api/admin/users` (list) + `POST /api/admin/users/{id}/admin-role` (promote/demote); acting-admin-only (403 otherwise); cannot demote the last remaining admin
- [x] Admin "view all" on the Ingredients/Recipes/Meals/Meal-Plans list endpoints via `?all=true`, honored ONLY for acting admins (no-op for everyone else, incl. admins with the view off)
- [x] Admin-view toggle consistency: Users nav link, the four "show all" toggles, app-level form toggles, and the review-queue link are all gated on `isActingAsAdmin`

**NOTES:**
- Backend: added `UserAdminService` (list + set-admin-role, both require IsActingAsAdmin; last-admin guard via `IUserRepository.CountAdminsAsync`); added `ListAllAsync` to user + ingredient/recipe/meal/meal-plan repos; each feature `ListAsync` gained `bool all = false` honored only when `all && user.IsActingAsAdmin`. New `AdminUserFunctions`; list functions read `req.Query["all"]`. New `AdminUserDto` + `SetAdminRoleInput`. Registered `UserAdminService` in Program.cs + DependencyInjectionTests.
- Frontend: new `UsersPage` (admin-gated, promote/demote), `/users` route, `ADMIN_NAV` Users link in `Layout` (only when acting admin), `adminApi` + `AdminUser` type, `listQuery(search, all)` helper, and "Show all users' items" checkbox (admin-only) on all four list pages — sent as `isAdmin && showAll` so turning admin view off re-scopes.
- Last-admin protection: demotion checks `CountAdminsAsync()`; if `<= 1` throws ValidationException (400). An admin can step down only after a second admin exists.
- Tests: `UserAdminServiceTests` (RBAC, promote/demote, last-admin guard, self-demote when 2nd admin exists) + Ingredient `?all` tests (admin sees others' private; ignored for non-admins and when view off). 84 total pass.
- Verified: `dotnet build -c Release` 0/0; `dotnet test` 84 passed; web build ✓ and lint 0 errors (1 pre-existing warning).

### Image upload to Azure Blob Storage — 2026-06-07 10:02 CDT

- [x] `POST /api/images` endpoint: auth-required multipart upload to the `images` blob container (Azurite locally via `AzureWebJobsStorage`), content-type + 5 MB validation, `{guid}.{ext}` naming, ensures container exists (public blob), returns `{ "url": "..." }`
- [x] Replaced the image-**URL** fields on Ingredient & Recipe forms with a reusable `<ImageUpload>` component (file picker, preview, client-side type/size validation, clear/replace) populating the existing `imageUrl`

**NOTES:**
- Files created: `Api/Services/ImageUploadValidator.cs` (pure, unit-testable), `Api/Services/ImageStorageService.cs` (BlobContainerClient wrapper; container name from `GoalFueled:Storage:ImageContainer`, default `images`), `Api/Functions/ImageFunctions.cs`, web `components/ImageUpload.tsx`, `tests/GoalFueled.Tests/ImageUploadValidatorTests.cs` (no blob I/O).
- Files changed: `GoalFueled.Api.csproj` (+`Azure.Storage.Blobs` 12.22.2), `Program.cs` (register `ImageStorageService` singleton), `DependencyInjectionTests.cs` (+ registration & `AzureWebJobsStorage` config key; client constructed, no live connection), web `api/client.ts` (+`upload()` FormData helper sending auth headers, no manual Content-Type), `api/services.ts` (`imageApi.upload`), `pages/IngredientFormPage.tsx` + `RecipeFormPage.tsx` (use `<ImageUpload>`), `index.css`.
- Endpoint contract: `POST /api/images`, multipart field `file`, 200 `{ "url": ... }`; 400 unsupported-type/empty/>5MB, 401 unauthenticated (RFC7807 problem JSON).
- Tests add NO emulator dependency (validation-only unit tests); actual blob upload exercised at runtime against Azurite.
- Verified: `dotnet build -c Release` 0/0; `dotnet test` 74 passed; web build ✓ and lint 0 errors (1 pre-existing warning).

### Meal Plans feature (end-to-end) — 2026-06-07 09:53 CDT

- [x] `MealPlanService` + `IMealPlanRepository`/`MealPlanRepository` (CRUD, owner-only RBAC — meal plans are personal)
- [x] Daily & weekly cadence; scheduled meals (MealId + optional day/time) and untimed items (meal or ingredient + quantity/note)
- [x] Daily nutrition summary of total consumption (daily plan = one summary; weekly = per-day + weekly total/average)
- [x] `/api/mealplans` CRUD endpoints (replaced the last stub)
- [x] Frontend: plan builder (daily/weekly toggle, schedule UI, untimed items) with daily nutrition summary shown vs the user's goal
- [x] Tests: schedule handling + daily/weekly aggregation + RBAC

**NOTES:**
- Extracted a reusable `MealNutritionCalculator` (`Api/Services/MealNutritionCalculator.cs`) for meal roll-up; `MealService` now delegates to it (Meal behavior/tests unchanged) and `MealPlanService` reuses it.
- Summary aggregation: every-day contributions = untimed items + day-less scheduled meals; ingredient untimed item = per-serving × (quantity/servingSize), null qty = 1 serving; weekly = per-day (every-day sum + that day's pinned meals), `WeeklyTotal` = Σ surfaced days, `DailyAverage` = total ÷ 7. Missing refs skipped gracefully. `TimeOnly` serializes as "HH:mm:ss"; enums as strings — no special handling.
- Files created: `Domain/Contracts/MealPlanDtos.cs`, `Data/Repositories/IMealPlanRepository.cs` + `MealPlanRepository.cs`, `Api/Services/MealNutritionCalculator.cs`, `Api/Services/MealPlanService.cs`, `Api/Functions/MealPlanFunctions.cs`, `tests/GoalFueled.Tests/MealPlanServiceTests.cs` (15 tests), web `pages/PlansPage.tsx` + `PlanFormPage.tsx`.
- Files changed: `MealService.cs` (delegates), `Program.cs`, `DataServiceCollectionExtensions.cs`, `DependencyInjectionTests.cs`, web `api/types.ts`, `api/services.ts`, `App.tsx` (real /plans routes), `index.css`.
- Files deleted: `Api/Functions/StubFunctions.cs` (all feature stubs now implemented) and web `pages/ComingSoonPage.tsx` (no remaining references).
- Verified: `dotnet build -c Release` 0/0; `dotnet test` 58 passed; web build ✓ and lint 0 errors (1 pre-existing warning).

### Meals feature (end-to-end) — 2026-06-07 09:42 CDT

- [x] `MealService` + `IMealRepository`/`MealRepository` (CRUD, RBAC: owner or acting admin; visible = app-level + own)
- [x] Per-meal nutrition summary = Σ (portion.Servings × recipe per-serving nutrition)
- [x] `/api/meals` CRUD endpoints (replaced the stub)
- [x] Frontend: meal list + create/edit (recipe portions with serving counts) with live nutrition preview
- [x] Tests: meal portion math + RBAC

**NOTES:**
- Extracted a shared `RecipeNutritionCalculator` (`Api/Services/RecipeNutritionCalculator.cs`) holding the ingredient-resolution + unit-conversion/scaling math; refactored `RecipeService` to delegate to it (Recipe behavior/tests unchanged) and `MealService` reuses `ComputePerServing` per portioned recipe.
- Files created: `Api/Services/RecipeNutritionCalculator.cs`, `Api/Services/MealService.cs`, `Api/Functions/MealFunctions.cs`, `Domain/Contracts/MealDtos.cs`, `Data/Repositories/IMealRepository.cs` + `MealRepository.cs`, web `pages/MealsPage.tsx` + `MealFormPage.tsx`, `tests/GoalFueled.Tests/MealServiceTests.cs` (12 tests).
- Files changed: `RecipeService.cs` (delegates), `Functions/StubFunctions.cs` (removed ListMeals; Plans stub kept), `Program.cs`, `DataServiceCollectionExtensions.cs`, web `api/types.ts`, `api/services.ts`, `App.tsx` (real /meals routes), `DependencyInjectionTests.cs`.
- Decision: Meals have no review/visibility lifecycle (entity has no Visibility) → meal visibility is simply app-level + own; portions may reference any recipe the caller can see (own/app/public).
- Verified: `dotnet build -c Release` 0/0; `dotnet test` 45 passed; web build ✓ and lint 0 errors (1 pre-existing warning).

### Recipes feature (end-to-end) — 2026-06-07 09:33 CDT

- [x] `RecipeService` (CRUD, validation, RBAC: owner or acting admin)
- [x] `IRecipeRepository` + `RecipeRepository` (visible = app-level + own + public)
- [x] Total + per-serving nutrition roll-up from ingredient amounts (unit-converted, quantity-scaled)
- [x] `/api/recipes` CRUD endpoints (replaced the stub)
- [x] Public-recipe review workflow: submit-for-review, admin review queue, approve/reject, once-public-never-private, original-creator credit preserved
- [x] Recipe image kept as a URL field (direct upload deferred to the Image-upload task)
- [x] Frontend: recipe list, create/edit (pick ingredients + quantities) with live nutrition preview, submit-for-public action, admin review-queue screen
- [x] Tests: recipe RBAC, nutrition roll-up math, review-workflow transitions

**NOTES:**
- Files created: `Domain/Contracts/RecipeDtos.cs`; `Data/Repositories/IRecipeRepository.cs` + `RecipeRepository.cs`; `Api/Services/RecipeService.cs`; `Api/Functions/RecipeFunctions.cs`; `tests/GoalFueled.Tests/RecipeServiceTests.cs` (19 tests); web `pages/RecipesPage.tsx`, `RecipeFormPage.tsx`, `RecipeReviewQueuePage.tsx`.
- Files changed: `DataServiceCollectionExtensions.cs` (register repo), `Program.cs` (register service), `Functions/StubFunctions.cs` (removed ListRecipes stub; Meals/Plans stubs kept), `DependencyInjectionTests.cs` (assert RecipeService/IRecipeRepository), web `api/types.ts`, `api/services.ts`, `App.tsx` (real /recipes routes incl. `review-queue`), `index.css` (`.recipe-item-row`).
- Nutrition roll-up: per item, if unit dimension-compatible with the ingredient's serving unit, `servings = (qty * itemUnitToBase) / (servingSize * ingUnitToBase)`, then scale the ingredient's per-serving nutrition by servings and sum across items; per-serving = total / recipe servings. Incompatible units are rejected at save and skipped in compute. Frontend mirrors the same math for a live preview.
- Review workflow: submit allowed only Private/Rejected → PendingReview (owner-only); approve/reject admin-only from PendingReview; public recipes can't be resubmitted/made private; `OriginalCreatorUserId` set at create and preserved through approval.
- Verified: `dotnet build -c Release` 0/0; `dotnet test` 33 passed; web `npm run build` ✓ and `npm run lint` 0 errors (1 pre-existing `react-hooks/set-state-in-effect` warning).
