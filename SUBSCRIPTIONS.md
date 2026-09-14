# Subscription Billing (Maxio Advanced Billing)

An **additive, parallel** capability on top of eShopOnWeb's one-time commerce flow: a
logged-in shopper browses plans, subscribes, and sees the enrollment in their account.
**Maxio Advanced Billing (Chargify)** is the billing system of record — eShopOnWeb stores no
subscription state of its own.

## What was added

HTTP endpoints on **`src/PublicApi`** (JWT-authenticated; caller identity comes from the token):

| Method & route | Purpose |
|---|---|
| `GET /api/subscription-plans` | List subscribable plans (products in the configured product family). |
| `POST /api/subscriptions` | Subscribe the caller to a plan. Idempotent. Body: `{ "planHandle": "eshop-pro" }` (optional; falls back to `Maxio:DefaultPlanHandle`). |
| `GET /api/my-subscriptions` | List the caller's subscriptions. |

### Architecture (Clean Architecture, matching the existing layering)

- **`ApplicationCore/Subscriptions`** + `Interfaces/ISubscriptionService.cs` — provider-agnostic
  domain models (`SubscriptionPlan`, `CustomerSubscription`, `SubscriberIdentity`, `SubscribeResult`)
  and the service contract. No Maxio types leak here.
- **`Infrastructure/Maxio`** — the Maxio integration, built to the OpenAPI spec in `maxio-spec/`:
  - `IMaxioClient` / `MaxioClient` — typed HTTP client; each method maps to one spec operation
    (`listProductsForProductFamily`, `readCustomerByReference`, `createCustomer`,
    `listCustomerSubscriptions`, `createSubscription`). HTTP Basic auth (API key as username,
    `x` as password), snake_case JSON, base URL `https://{subdomain}.chargify.com`.
  - `MaxioSubscriptionService` — implements `ISubscriptionService` and owns the idempotency logic.
  - `MaxioSettings` + `MaxioServiceCollectionExtensions.AddMaxioBilling(...)` — config binding
    (validated on startup) and DI wiring.
- **`PublicApi/SubscriptionEndpoints`** — the three endpoints, DTOs, and a `SubscriberResolver`
  that turns the JWT principal into a `SubscriberIdentity`.

### Idempotency (a double-click never duplicates)

- **One customer per shopper:** the shopper's ASP.NET Identity user id is stored as the Maxio
  customer **`reference`** (Maxio enforces reference uniqueness), so the shopper↔customer mapping
  lives entirely in Maxio — no extra local table is needed. Ensure = look up by reference, create
  only if absent, and recover from a lost create race (`422`) by re-looking-up. With a persistent
  Identity store the user id is stable, so the mapping holds across restarts; with the in-memory
  database it is fully idempotent **within a run** (the in-memory Identity store regenerates user
  ids on restart, per the environment note).
- **One live subscription per plan:** before creating, existing subscriptions for the customer are
  checked; a subscription to the same plan in a live state is returned as-is
  (`alreadyExisted: true`). Terminal states (canceled/expired/…) allow re-subscribing.
- Subscribe is **serialized per shopper** with an in-process lock so concurrent requests collapse
  to a single customer + subscription.

Plans use `payment_collection_method: remittance`, so subscribing works without capturing a card
(the seeded plans also have `require_credit_card = false`).

## Configuration

Bound from the `Maxio:` configuration section — **values come from user-secrets / environment,
never the repo**:

| Key | Source env var | Required | Notes |
|---|---|---|---|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | yes | HTTP Basic username. |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | yes* | Used to derive `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | yes | Product family holding the plans. |
| `Maxio:BaseUrl` | — | no | Explicit base-URL override; used verbatim when set (e.g. EU hosting). |
| `Maxio:DefaultPlanHandle` | — | no | Plan used when `POST /api/subscriptions` omits `planHandle`. |

\* Either `Maxio:Subdomain` or `Maxio:BaseUrl` must be present.

Load the secrets into .NET user-secrets for `src/PublicApi` (PowerShell):

```powershell
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              $env:MAXIO_API_KEY
dotnet user-secrets set "Maxio:Subdomain"           $env:MAXIO_SITE_SUBDOMAIN
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY
dotnet user-secrets set "Maxio:DefaultPlanHandle"   "eshop-pro"   # optional convenience
```

## Run & verify

This machine's runtime notes: `global.json` uses `rollForward: latestMajor`; run with
`DOTNET_ROLL_FORWARD=Major`, and `UseOnlyInMemoryDatabase=true` (no LocalDB). Bind only to the
assigned port block.

```powershell
$env:DOTNET_ROLL_FORWARD   = "Major"
$env:UseOnlyInMemoryDatabase = "true"
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile `
  --urls "https://localhost:7223;http://localhost:7224"
```

Then (curl uses `-k` because of the HTTPS dev cert):

```bash
# 1. Get a JWT from PublicApi (storefront cookie does NOT work here)
TOKEN=$(curl -sk -X POST https://localhost:7223/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  | grep -oE '"token":"[^"]+"' | cut -d'"' -f4)

# 2. Browse plans
curl -sk https://localhost:7223/api/subscription-plans -H "Authorization: Bearer $TOKEN"

# 3. Subscribe (hero flow). First call -> alreadyExisted:false; repeat -> alreadyExisted:true
curl -sk -X POST https://localhost:7223/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}'

# 4. Confirm it on the account
curl -sk https://localhost:7223/api/my-subscriptions -H "Authorization: Bearer $TOKEN"
```

Swagger UI is at `https://localhost:7223/swagger` (Authorize with `Bearer <token>`).

Automated coverage lives in `tests/UnitTests/Infrastructure/Maxio` (idempotency, race recovery,
plan resolution, settings, error parsing): `dotnet test tests/UnitTests/UnitTests.csproj`.
