# Maxio Advanced Billing — Recurring Subscriptions

This is an **additive, parallel** capability on top of eShopOnWeb's one-time commerce flow
(Catalog → Basket → Order). It lets a logged-in shopper browse subscription plans, subscribe
to one, and see it reflected in their account. **Maxio Advanced Billing** is the billing
system of record; eShopOnWeb stores nothing about subscriptions itself.

Every Maxio interaction is built to the **Maxio OpenAPI specification** in `maxio-spec/`
(`openapi.yaml`), which is the authoritative contract.

## Endpoints (src/PublicApi, JWT-authenticated)

| Method & route | Purpose |
|---|---|
| `GET /api/subscription-plans` | List plans (products in the configured product family). |
| `POST /api/subscriptions` | Enroll the caller in a plan. Idempotent. Body: `{ "planHandle": "eshop-pro" }`. |
| `GET /api/my-subscriptions` | List the caller's subscriptions. |

The caller's identity always comes from the JWT (`ClaimTypes.Name` = the user's email),
never from the request body, so a shopper can only ever act on their own billing account.

## How it maps to Maxio (all operations are from the spec)

- **Plans** — `GET /product_families/handle:{ProductFamilyHandle}/products.json`
  (`listProductsForProductFamily`). Uses the stable family **handle**, not a numeric id.
- **Ensure customer (idempotent)** — `GET /customers/lookup.json?reference=…`
  (`readCustomerByReference`); on 404, `POST /customers.json` (`createCustomer`). The Maxio
  customer `reference` is set to `eshoponweb:{email}`, which Maxio enforces as unique — so a
  double-click can never create two customers. A lost create race (HTTP 422 "reference must
  be unique") is recovered by re-reading the customer.
- **Subscribe (idempotent)** — before creating, the customer's existing subscriptions
  (`GET /customers/{id}/subscriptions.json`) are checked for a live subscription to the same
  plan; if found it is returned as-is. Otherwise `POST /subscriptions.json`
  (`createSubscription`) with `product_handle` + `customer_id` +
  `payment_collection_method: remittance`.
- **List subscriptions** — `GET /customers/{id}/subscriptions.json`
  (`listCustomerSubscriptions`); an absent customer yields an empty list.

### Card-less enrollment

The seeded plans have **payment method not required**, but creating an automatic-collection
subscription still fails with *"No payment method was on file"*. Enrollment therefore uses
`payment_collection_method: remittance` (invoice billing, per the spec's `Collection-Method`
enum), which activates the subscription immediately without capturing a card or 3-DS.

## Configuration

Bound from the `Maxio:` section (no values are hard-coded, so the same build runs against any
site/catalog):

| Key | Source env var | Notes |
|---|---|---|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | HTTP Basic auth username (password is `x`), per the spec's `BasicAuth` scheme. |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Used to derive the base URL `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Family whose products are offered as plans. |
| `Maxio:BaseUrl` | — | Optional. When set, used verbatim instead of the derived URL. |

**Secrets are read from the environment into .NET user-secrets** (never committed):

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

## Code layout

- `src/ApplicationCore/Subscriptions/*`, `Interfaces/ISubscriptionBillingService.cs` —
  dependency-free domain model + abstraction.
- `src/Infrastructure/Maxio/*` — `MaxioSettings`, spec-faithful DTOs (`Models/`),
  `MaxioApiClient` (typed `HttpClient`, Basic auth), `MaxioBillingService` (orchestration +
  idempotency), `AddMaxioBilling` DI extension.
- `src/PublicApi/SubscriptionEndpoints/*` — the three endpoints + DTOs, following the
  project's `MinimalApi.Endpoint` `IEndpoint` conventions.

## Run & verify

```bash
export DOTNET_ROLL_FORWARD=Major            # SDK pinned to 8.0.x; roll forward to installed SDK
export ASPNETCORE_ENVIRONMENT=Development    # loads user-secrets
export ASPNETCORE_URLS="https://localhost:6763;http://localhost:6764"
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile -- --UseOnlyInMemoryDatabase=true
```

```bash
B=https://localhost:6763
TOKEN=$(curl -sk -X POST $B/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['token'])")

curl -sk $B/api/subscription-plans -H "Authorization: Bearer $TOKEN"
curl -sk -X POST $B/api/subscriptions -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"planHandle":"eshop-pro"}'   # 201, alreadyEnrolled:false
curl -sk -X POST $B/api/subscriptions -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"planHandle":"eshop-pro"}'   # 200, alreadyEnrolled:true (same id)
curl -sk $B/api/my-subscriptions -H "Authorization: Bearer $TOKEN"
```

Or use Swagger at `https://localhost:6763/swagger` (Authorize with `Bearer <token>` first).
