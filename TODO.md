# Features to do

## Instructions

Ignore all files in the `Future Features` section.

## Tasks

- [] **Search for existing ingredients when adding a new one**: to help prevent duplicate ingredients from being added, when a user is entering a name for a new ingredient, do a lookup of existing ingredients so they can see that it already exists. If they select an existing item from the lookup list, add the nutritional info for that item into the form they are on.

- [] **When adding a `free-form` `fuel` item to the `Log` do an ingredient lookup**: when a user is adding a `free-form` type `fuel` item to the `Log`, do an ingredient lookup as they enter the `item` description. If they pick a match, then insert its name and per-serving calories.

## FUTURE FEATURES

>[AI Agent] skip ALL items in this section.

- [] **Shopping Lists**: generate a weekly shopping list based on the meal plan.

- [] **Recipes for the week**: adjusted based on number of servings needed for the week

- [] **Integrate with Google Health**: synchronize weight data with Google Health. What else can we sync with it?

- [] **Add log sharing**: create a way for users to view their log with their doctor/trainer/etc.

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
