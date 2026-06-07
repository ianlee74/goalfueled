# DONE TO-DO ITEMS

## Instructions

[AI Agent] add completed tasks to the top of the `Completed Items` section after they are completed. Add the date & time that the item was completed. Also, add any notes about configuration, assumptions, or decisions made in a `NOTES` section below the item.

## Completed Items

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
