# Features to do

## Instructions

Ignore all files in the `Future Features` section.

## Tasks

- [] **Delete confirmation dialog**: when I delete an ingredient, I get a system confirmation dialog. This doesn't match the style of our app. Please create a custom confirmation dialog that matches the style of our app.

- [] **Customized ingredient is always "Saving..."**: when I create a customized ingredient, the `Save` button appears to always be in the `Saving...` state instead of waiting for me to press a `Save` button. I should need to press the `Save` button before the custom ingredient is created.

- [] **Admin review features?**: I can't find the page where I review pending recipes. Was that lost in the UI redesign or maybe my user isn't appearing as the App Admin anymore? Investigate. Also, if I'm an app admin, show an option in the `/you` page where I can switch my view from being an admin vs a regular user.

- [] **Submit ingredient to app**: add an option to `Submit to public` for `ingredients` like we have for `recipes`.

- [] **`Look up nutrition` button on the `/ingredient/new` page still doesn't work**: where does this information come from? Is there something I need to know in order to use it? Try and make this work as it should.

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
