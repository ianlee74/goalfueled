# Features to do

## Instructions

Ignore all files in the `Future Features` section.

## Tasks

> Context: The MVP scaffold + the **Ingredients** vertical slice are complete (full CRUD, nutrition,
> app/user scoping, RBAC). Auth seam + local dev login, profile provisioning, first-user-becomes-admin,
> admin-view toggle, and first-login onboarding are done. The items below are the remaining MVP work.
> Build each feature following the Ingredients pattern: Domain model → Data repository → Api service +
> functions (with RBAC) → React pages.

## FUTURE FEATURES

>[AI Agent] skip ALL items in this section.

- [skip] **Define goals and track progress**: we need to gamify the app by allowing the user to define a `goal` and tracking their progress towards that goal. A goal is a target metric that a user defines that has a target date that it should be achieved. A metric can be weight or a nutritional metric (i.e. calories, protein, vitamin C, etc). A goal could be either one-time (ex. "loose 10 lbs by 12/31/2026") or recurring (ex. "consume 2000 calories every 7 days starting on Monday"). There should be a visual (chart? other fun graphic?) that shows progress. A goal should have an option to indicate if exceeding the goal is a positive thing (ex. "you lost 2 more lbs than your goal!") or a negative thing (ex. you're on a weight loss goal and you consumed too many calories). The visual should celebrate appropriately based on a positive or negative final result.

- [skip] **Add option to add tracking of ad-hoc meals**: if the user eats a snack or other meal that was not part of the plan, then they should be able to easily add that to their daily consumption log.

- [skip] **Integrate with Google Health**: synchronize weight data with Google Health. What else can we sync with it?

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
