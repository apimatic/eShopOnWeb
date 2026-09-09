# Subscription billing (Maxio Advanced Billing)

An **additive, parallel** capability on top of eShopOnWeb's one-time commerce flow
(Catalog → Basket → Order). It lets a logged-in shopper browse recurring plans, subscribe,
and see their subscriptions — with **Maxio Advanced Billing** as the system of record.

## Endpoints (JWT-authenticated, on `src/PublicApi`)

| Method & route | Purpose |
|----------------|---------|
| `GET  /api/subscription-plans` | List the plans (products in the configured Maxio product family). |
| `POST /api/subscriptions` | Subscribe the caller to a plan. Body: `{ "planHandle": "eshop-pro" }`. |
| `GET  /api/my-subscriptions` | List the caller's own subscriptions. |

The caller's identity comes from the bearer token (`ClaimTypes.Name`). The stable eShopOnWeb
user id is stored as the Maxio customer `reference`, which is the idempotency key.

`POST /api/subscriptions` responds **201 Created** for a new subscription and **200 OK** when the
shopper was already subscribed (idempotent — a double-click never creates a second
customer/subscription). It is guarded three ways:

1. **Find-or-create customer** keyed on the eShopOnWeb user id (`reference` is unique in Maxio).
2. **De-duplication check** — an existing live subscription to the same plan is returned as-is.
3. **`uniqueness_token`** on the create call so Maxio rejects a racing duplicate (409), which is
   then resolved to the winning subscription.

## Architecture

- **ApplicationCore** — `ISubscriptionService` and the domain models (`SubscriptionPlan`,
  `CustomerSubscription`, `SubscriberInfo`, `SubscribeResult`). No Maxio types leak here.
- **Infrastructure/Maxio** — `MaxioApiClient` (typed `HttpClient`, auth + serialization only),
  `MaxioSubscriptionService` (find-or-create + idempotent subscribe orchestration),
  `MaxioSettings`, `MaxioApiException`, and `AddMaxioSubscriptions(...)` DI wiring.
- **PublicApi/SubscriptionEndpoints** — the three endpoints and their DTOs, following the
  project's existing `IEndpoint` convention.

Plans use `payment_collection_method: remittance` so a shopper can subscribe without a card
(the seeded plans bill the first period immediately, which would otherwise 422 with no card).

## Configuration (`Maxio:` section — values via user-secrets, never in the repo)

| Key | Source env var | Notes |
|-----|----------------|-------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | HTTP Basic username (password is `X`). |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Base URL is derived as `https://{Subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Family whose products are the plans. |
| `Maxio:BaseUrl` | — | Optional override; when set it is used verbatim instead of the derived URL. |

Load them into user-secrets (values come from the environment; nothing is written to the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

## Run & verify locally

```bash
# From the repo root. In-memory DB (no LocalDB needed); roll-forward covers SDK/runtime gaps.
export ASPNETCORE_ENVIRONMENT=Development
export UseOnlyInMemoryDatabase=true
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_URLS="https://localhost:30523;http://localhost:30524"
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

```bash
API=https://localhost:30523

# 1. Get a bearer token (the storefront cookie does NOT work here).
TOKEN=$(curl -sk -X POST "$API/api/authenticate" -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)

# 2. Browse plans.
curl -sk "$API/api/subscription-plans" -H "Authorization: Bearer $TOKEN" | jq

# 3. Subscribe (hero flow) — 201 first time, 200 (alreadyExisted) on repeat.
curl -sk -X POST "$API/api/subscriptions" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"planHandle":"eshop-pro"}' | jq

# 4. Confirm it on the account.
curl -sk "$API/api/my-subscriptions" -H "Authorization: Bearer $TOKEN" | jq
```

> With the in-memory database the demo user's id is regenerated on each restart, so the
> user ↔ Maxio-customer mapping only persists within a single run (a new Maxio customer is
> created on the next run). This is a limitation of `UseOnlyInMemoryDatabase`, not the integration.

Unit tests (idempotency logic, config binding): `dotnet test tests/UnitTests/UnitTests.csproj --filter Maxio`.
