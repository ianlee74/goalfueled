# DONE TO-DO ITEMS

## Instructions

[AI Agent] add completed tasks to the top of the `Completed Items` section after they are completed. Add the date & time that the item was completed. Also, add any notes about configuration, assumptions, or decisions made in a `NOTES` section below the item.

## Completed Items

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
