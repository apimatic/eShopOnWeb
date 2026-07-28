# Maxio Subscription Billing (eShopOnWeb)

Recurring-subscription billing added **alongside** the existing one-time commerce flow
(Catalog → Basket → Order). Maxio Advanced Billing is the system of record. Nothing in the
existing cart/checkout path was changed.

The capability is exposed as JWT-authenticated HTTP endpoints on **`src/PublicApi`**:

| Method & route | Purpose |
|---|---|
| `GET /api/subscription-plans` | List the plans in the configured Maxio product family. |
| `POST /api/subscriptions` | Subscribe the authenticated user to a plan (idempotent). |
| `GET /api/my-subscriptions` | List the authenticated user's subscriptions. |

The caller's identity always comes from the bearer token — never the request body.

## Architecture

- **`ApplicationCore`** — provider-agnostic abstraction: `IMaxioBillingService`
  (`Interfaces/`), domain models (`Billing/`: `SubscriptionPlan`, `SubscriptionSummary`,
  `SubscribeCommand`, `SubscribeResult`), and billing exceptions (`Exceptions/`).
- **`Infrastructure/Maxio`** — everything Maxio-specific: `MaxioSettings` (config binding +
  base-URL resolution), wire models (`Models/`, faithful to the OpenAPI schemas),
  `MaxioApiClient` (typed HTTP client, Basic auth), `MaxioBillingService` (orchestration +
  idempotency), and `AddMaxioBilling` DI wiring.
- **`PublicApi/SubscriptionEndpoints`** — the three endpoints, following the project's
  existing `MinimalApi.Endpoint` `IEndpoint` convention.

The Maxio OpenAPI spec in **`maxio-spec/`** is the authoritative contract: base URL
(`https://{subdomain}.chargify.com`), Basic auth (username = API key, password = `x`),
`GET /product_families/{id}/products.json`, `GET /customers/lookup.json`, `POST /customers.json`,
`POST /subscriptions.json`, and `GET /customers/{id}/subscriptions.json`.

### Idempotency (double-click safe)

1. The eShopOnWeb user's stable identity id is used as the Maxio customer **`reference`**, so a
   customer is only ever created once (looked up before create; a concurrent unique-reference
   `422` is tolerated by re-reading).
2. Before creating a subscription, any existing non-terminal subscription to the same plan is
   reused instead of creating a duplicate.
3. Concurrent subscribe calls for the same user are serialized by a per-reference in-process
   lock. (Note: the lock is process-local; combined with the reuse check above it makes the
   demo double-click safe under the in-memory-database constraint.)

Plans without a required payment method are billed by invoice, so subscriptions are created
with `payment_collection_method: remittance` — no card capture / 3-DS needed.

## Configuration

Settings bind from the **`Maxio`** configuration section — **no values are hard-coded**, so the
same build runs against a different Maxio site/catalog:

| Key | Meaning | Sourced from env var |
|---|---|---|
| `Maxio:ApiKey` | Advanced Billing API key | `MAXIO_API_KEY` |
| `Maxio:Subdomain` | Site subdomain (derives the base URL) | `MAXIO_SITE_SUBDOMAIN` |
| `Maxio:ProductFamilyHandle` | Product family containing the plans | `MAXIO_DEFAULT_PRODUCT_FAMILY` |
| `Maxio:BaseUrl` | Optional explicit base-URL override (used verbatim when set) | — |

**Secrets never live in the repo.** Load them into .NET user-secrets for `src/PublicApi`:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
# Optional: dotnet user-secrets set "Maxio:BaseUrl" "https://<host>"
```

## Run & verify

This machine has only the .NET 10 SDK and no SQL LocalDB, so roll the SDK forward and use the
in-memory database:

```bash
export DOTNET_ROLL_FORWARD=Major
cd src/PublicApi
ASPNETCORE_ENVIRONMENT=Development \
UseOnlyInMemoryDatabase=true \
ASPNETCORE_URLS="https://localhost:6803;http://localhost:6804" \
dotnet run
```

Then (the storefront cookie won't work here — get a bearer token from PublicApi first):

```bash
B=https://localhost:6803
TOKEN=$(curl -sk -X POST $B/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  | grep -oE '"token":"[^"]*"' | sed 's/"token":"//;s/"//')

# 1) Browse plans
curl -sk $B/api/subscription-plans -H "Authorization: Bearer $TOKEN"

# 2) Subscribe (hero flow) -> 201 Created, alreadyExisted:false
curl -sk -X POST $B/api/subscriptions -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"planHandle":"eshop-pro"}'

# 3) Subscribe again -> 200 OK, alreadyExisted:true, SAME subscriptionId (idempotent)
curl -sk -X POST $B/api/subscriptions -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"planHandle":"eshop-pro"}'

# 4) See it on the account
curl -sk $B/api/my-subscriptions -H "Authorization: Bearer $TOKEN"
```

`POST /api/subscriptions` with an empty body `{}` subscribes to the lowest-priced plan.
Swagger UI is at `https://localhost:6803/swagger`.

Tests: `dotnet test tests/UnitTests/UnitTests.csproj` and
`dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj`
(set `DOTNET_ROLL_FORWARD=Major`).
