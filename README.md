# GoalFueled

A serverless web app for defining and tracking meal plans toward a health goal (weight loss,
management, or gain). Mobile-first, also usable on larger screens.

This repository contains the **MVP scaffold plus a complete vertical slice**: the full stack is
wired end-to-end and the **Ingredients** feature is fully implemented (with authentication, roles,
and nutrition). Recipes, Meals, and Meal Plans are scaffolded (models, API stubs, and "coming soon"
screens) and ready to be built out following the Ingredients pattern.

## Architecture

| Layer | Technology |
| --- | --- |
| Frontend | TypeScript + React 19, built with Vite 7 |
| API | Azure Functions (.NET 8 isolated worker, ASP.NET Core integration) |
| Data | Azure Cosmos DB (SQL API) via EF Core; in-memory provider for local dev |
| Hosting | Azure Static Web Apps (web) + Azure Functions (API) |
| Infra | Bicep (`infra/`) |
| CI/CD | GitHub Actions (`.github/workflows/`) and Azure DevOps (`azure-pipelines.yml`) |

```
src/
  GoalFueled.Domain/   Entities, enums, nutrient catalog (FDA label), DTOs — no dependencies
  GoalFueled.Data/     EF Core DbContext (Cosmos + in-memory), repositories, seeding
  GoalFueled.Api/      Azure Functions HTTP API: auth seam, RBAC, endpoints
  web/                 Vite + React SPA
tests/
  GoalFueled.Tests/    xUnit tests (auth provisioning, RBAC, validation, DI graph)
infra/                 Bicep modules (Cosmos, Storage, Functions, Static Web App, monitoring)
```

## Prerequisites

- **.NET 8 SDK**
- **Node.js 20.19+ or 22.12+** (Vite 7 warns on older 22.x; build still works)
- **Azure Functions Core Tools v4** — to run the API locally (`func`). See the install steps below.
- *(optional)* **Azure Cosmos DB Emulator** — to use Cosmos locally instead of the in-memory store

### Installing Node + Azure Functions Core Tools (Windows / PowerShell)

> **Heads-up:** Node **22.2.0** (a common pre-installed build) breaks the npm install of `func` with
> `ERR_REQUIRE_ESM`. Pick **one** of the two strategies below.

**Strategy A — upgrade Node, then install `func` via npm (recommended).**
Uses [nvm-windows](https://github.com/coreybutler/nvm-windows) so you can keep multiple Node versions.

```powershell
# 1. Install nvm-windows (then RESTART your terminal).
winget install CoreyButler.NVMforWindows --silent --accept-package-agreements --accept-source-agreements

# 2. Install and activate the current Node LTS (24.x today; anything >= 22.12 works).
nvm install lts
nvm use lts
node --version          # e.g. v24.16.0

# 3. Install the Azure Functions Core Tools (v4) globally.
npm install -g azure-functions-core-tools@4
func --version          # expect 4.x
```

**If `func --version` errors after install:** the npm package downloads the host as a zip but can
occasionally skip extraction. Extract it in place, then retry:

```powershell
$bin = "$env:NVM_HOME\$(nvm current)\node_modules\azure-functions-core-tools\bin"
Get-ChildItem $bin -Filter '*.zip' | ForEach-Object { Expand-Archive $_.FullName -DestinationPath $bin -Force }
```

**Strategy B — keep Node 22.2.0, install `func` without npm** (avoids the broken post-install script).
Use whichever package manager you have:

```powershell
# winget (Windows 10/11 App Installer):
winget install Microsoft.Azure.FunctionsCoreTools --silent --accept-package-agreements --accept-source-agreements

# …or Chocolatey:
choco install azure-functions-core-tools -y

# verify (restart the terminal first so PATH refreshes):
func --version
```

> No `winget`? Install it from the Microsoft Store ("App Installer"), or grab the Core Tools MSI from
> <https://github.com/Azure/azure-functions-core-tools/releases> and run it. After installing, open a
> new terminal so `func` is on `PATH`.

## Running locally

Two terminals.

**1. API** (Azure Functions, port 7071):

```bash
cd src/GoalFueled.Api
func start
```

By default the API runs against the **in-memory store** with **dev auth enabled**
(see `local.settings.json`). It seeds a few app-level ingredients on first start.

**2. Web** (Vite dev server, port 5173):

```bash
cd src/web
npm install
npm run dev
```

Open http://localhost:5173. Vite proxies `/api/*` to the Functions host, so there's no CORS to deal
with. Sign in as one of the preset dev users — **the first user to sign in becomes the App Admin.**

### Using the Cosmos DB Emulator instead of in-memory

1. Install and start the Azure Cosmos DB Emulator.
2. In `src/GoalFueled.Api/local.settings.json`, set `GoalFueled:UseInMemoryStore` to `false`.
   The emulator connection string is already filled in (the well-known emulator endpoint/key).
3. Restart `func start`. The database and containers are created automatically.

### Automatic ingredient nutrition lookup

The ingredient form has a **Look up nutrition** button that auto-fills nutrient values from the free,
public [USDA FoodData Central](https://fdc.nal.usda.gov/api-guide.html) API. The external HTTP call is
made server-side (via `IHttpClientFactory`) so the API key stays off the browser and there's no CORS.

- `GoalFueled:Nutrition:FdcApiKey` — defaults to USDA's public, rate-limited `DEMO_KEY`. Set a personal
  key (free, from the USDA site) for higher limits. No key is required to use the feature.
- `GoalFueled:Nutrition:FdcBaseUrl` — defaults to `https://api.nal.usda.gov/fdc/v1/`.

USDA reports nutrients per 100 g; the app stores per-serving values, so applying a lookup sets the
serving basis to 100 g and fills the matching catalog nutrients (1:1, with unit conversion). Only
nutrients the app tracks are populated; the lookup pre-fills editable fields and never auto-saves, and
it fails gracefully (network/no-key/rate-limit/no-result) so manual entry is always available.

## Authentication & authorization

Auth is built behind a provider seam (`IIdentityProvider`) so the local dev login can be swapped for
real social login without touching the rest of the app.

- **Today (dev):** `DevIdentityProvider` reads `X-Dev-*` headers sent by the SPA. Enabled only when
  `GoalFueled:Auth:EnableDevAuth=true`. **Never enable this in a real deployment.**
- **Later (production):** `EntraExternalIdProvider` is a documented stub for Microsoft Entra External
  ID, which federates **Google, Microsoft, and Facebook**. Implement JWT validation there, register
  it in `Program.cs`, and set `EnableDevAuth=false`. The frontend's `authHeaders()` (in
  `src/web/src/api/client.ts`) is the single place to switch to `Authorization: Bearer <token>`.

**Roles:** `User` (own data only) and `AppAdmin` (view/modify all data, manage app-level content,
promote others). The first user is auto-promoted to admin. Admins can toggle their **admin view**
off to use the app as a regular user (header toggle in the app).

## What's implemented

- ✅ **Auth & profiles** — dev login, profile provisioning, first-user-becomes-admin, admin-view toggle
- ✅ **Onboarding** — first-login welcome page that captures the user's goal
- ✅ **Ingredients** — full CRUD, image URL, unit of measure, per-serving nutrition editor backed by
  an FDA-label nutrient catalog; app-level vs user-owned scoping and RBAC
- 🟨 **Recipes / Meals / Meal Plans** — domain models + API stubs + "coming soon" screens

### API endpoints

| Method | Route | Notes |
| --- | --- | --- |
| GET | `/api/health` | Liveness (anonymous) |
| GET | `/api/me` | Current profile (provisions on first call) |
| POST | `/api/me/onboarding` | Set first goal |
| POST | `/api/me/admin-view` | Toggle admin view (admin only) |
| GET | `/api/nutrients` | Nutrient catalog (reference data) |
| GET/POST | `/api/ingredients` | List (with `?search=`) / create |
| GET/PUT/DELETE | `/api/ingredients/{id}` | Read / update / delete |
| GET | `/api/recipes`, `/api/meals`, `/api/mealplans` | Stubs (coming soon) |

## Testing

```bash
dotnet test
```

Covers user provisioning & the first-user-admin rule, ingredient RBAC (owner vs other vs admin),
app-level visibility, validation, search, and a DI-graph resolution check that mirrors `Program.cs`.

## Infrastructure & deployment

`infra/main.bicep` provisions Cosmos DB (serverless), Storage (Functions backend + image blobs),
the Function App, a Static Web App, and App Insights / Log Analytics.

```bash
az group create -n rg-goalfueled-dev -l eastus
az deployment group create -g rg-goalfueled-dev -f infra/main.bicep -p infra/main.parameters.json
```

CI (`.github/workflows/ci.yml` and `azure-pipelines.yml`) builds and tests the API and web on every
push/PR. Deploy workflows are present but **manual-only and inert** until you configure the
documented secrets/variables — nothing deploys automatically.

## Roadmap (next features)

1. **Recipes** — compose ingredients, roll up nutrition totals, public-recipe review workflow.
2. **Meals** — portions of prepared recipes with a per-meal nutrition summary.
3. **Meal Plans** — daily/weekly schedules, timed & untimed items, daily nutrition summary vs goal.
4. **Image upload** — replace the image-URL field with direct upload to the `images` blob container.
5. **Real social auth** — implement `EntraExternalIdProvider` and switch the SPA to bearer tokens.
