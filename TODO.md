# Features to do

## Instructions

Ignore all files in the `Future Features` section.

## Tasks

- [] **Ensure that the `Fuel Type` for a `Fuel Source` doesn't change**: once a `Recipe Portion` has been added to a `Fuel Source`, the `Fuel Type` should not be able to be changed unless all the `Recipe Portions` for that `Fuel Source` are first removed.

- [] **Add a `Fuel Type` filter to the `Fuel Sources` page** at '/fuel-sources`.

- [] **Add an `About` page** that explains the purpose of the app and shows the date that it was last updated.

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
