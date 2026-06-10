# Features to do

## Instructions

Ignore all files in the `Future Features` section.

## Tasks

- [x] **Capture basic info during first login**: when a user first creates their account, let's prompt them for a few basic pieces of information (one time). We'll also need a user profile page where they can update these attributes later if necessary.
  - Sex at Birth: male/female
  - Current Weight
  - Height
  - Goal: (already asking)

- [] **Create a `Kindle` cost**: people burn calories by just staying alive. Automatically add a daily `burn` cost to the user's log equal to their daily sedentary calorie usage. To calculate this, use the Mifflin–St Jeor BMR Formula multiplied by 1.2.

- [] **Create a landing page dashboard**: the user should land on a page that gives them an interesting view of their current progress and these actions:
  - List next 3 upcoming meals from their meal plan (with times to consume shown) and an option to mark them as completed.
  - Log their weight (if it's due today).
  - See a chart of their progress over the past 7 days in regards to `fuel` minus `burn` minus `kindle` cost with an estimate of weight lost. 

## FUTURE FEATURES

>[AI Agent] skip ALL items in this section.

- [] **Shopping Lists**: generate a weekly shopping list based on the meal plan.

- [] **Recipes for the week**: adjusted based on number of servings needed for the week

- [] **Integrate with Google Health**: synchronize weight data with Google Health. What else can we sync with it?

### Real authentication (Microsoft Entra External ID)

- [ ] Provision an Entra External ID tenant + user flow; add **Google**, **Microsoft**, **Facebook** social IdPs.
- [ ] Register the SPA and API app registrations; expose an API scope.
- [ ] Implement `EntraExternalIdProvider`: validate the Bearer JWT (issuer/audience/signing keys) and map `sub`/`email`/`name` claims; register it in `Program.cs`.
- [ ] Frontend: MSAL integration; replace the dev login with real social-login buttons; send `Authorization: Bearer <token>` (swap the single `authHeaders()` in `src/web/src/api/client.ts`).
- [ ] Set `GoalFueled:Auth:EnableDevAuth=false` in deployed environments; keep dev auth local-only.
- [ ] Account linking: allow one profile reached via multiple providers (the `ExternalLogins`/`LoginKeys` model already supports it).

### Deployment & infrastructure activation (infra authored, not yet deployed)

- [ ] Provision Azure resources via `infra/main.bicep` (Cosmos, Storage, Functions, Static Web App, monitoring).
- [ ] Configure CI/CD secrets/variables and run the deploy workflows (GitHub Actions + Azure DevOps).
- [ ] Switch the API to the Cosmos provider in cloud envs (`GoalFueled:UseInMemoryStore=false`) and verify against a real Cosmos account.
- [ ] Tighten the Function App **CORS** to the Static Web App hostname (currently a placeholder).
- [ ] Move secrets to **Key Vault + managed identity** (replace inline connection strings in `functionApp.bicep`).
