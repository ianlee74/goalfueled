# DONE TO-DO ITEMS

## Instructions

[AI Agent] add completed tasks to the top of the `Completed Items` section after they are completed. Add the date & time that the item was completed. Also, add any notes about configuration, assumptions, or decisions made in a `NOTES` section below the item.

## Completed Items

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
