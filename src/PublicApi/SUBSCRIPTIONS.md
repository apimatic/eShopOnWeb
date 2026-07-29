# Subscription billing (Maxio Advanced Billing)

An **additive, parallel** capability on top of eShopOnWeb's existing one-time commerce flow.
It lets a logged-in shopper browse plans, subscribe, and see their subscriptions — with
**Maxio Advanced Billing** as the system of record. No local subscription state is stored; Maxio
is queried live on every request.

## Endpoints (PublicApi, JWT-authenticated)

The caller's identity comes from the bearer token; the stable eShop user id is used as the Maxio
customer `reference`.

| Method | Route | Purpose |
|--------|-------|---------|
| `GET`  | `/api/subscription-plans` | List the plans available to subscribe to (products in the configured product family). |
| `POST` | `/api/subscriptions` | Subscribe the caller to a plan. Body: `{ "planHandle": "eshop-pro" }`. |
| `GET`  | `/api/my-subscriptions` | List the caller's subscriptions (plan, price, state, next billing date). |

`POST /api/subscriptions` returns **201 Created** for a new subscription and **200 OK** with
`"alreadyExisted": true` when the caller already holds that plan.

### Idempotency (the hero flow is safe to double-click)

- **One customer per user:** the user is looked up in Maxio by `reference` and only created if
  absent; a create race (HTTP 422 on the unique reference) is resolved by re-reading.
- **No duplicate subscriptions:** before creating, any existing non-terminal subscription for the
  same plan is returned as-is. The create call also carries a deterministic `uniqueness_token`, so
  a retried/duplicated POST is rejected by Maxio (HTTP 409) and resolved to the existing
  subscription instead of creating a second one.

### Payment method

The seeded plans do not require a card, so subscriptions are created with
`payment_collection_method: remittance` (invoice billing). Signup activates the subscription
without capturing a card or triggering 3-DS.

## Architecture

- `ApplicationCore/Interfaces/IBillingService.cs` + `ApplicationCore/Billing/*` — the billing
  abstraction and domain models (no dependency on Maxio).
- `Infrastructure/Maxio/*` — `MaxioBillingService` (a typed `HttpClient` against the Maxio REST
  API with HTTP Basic auth), `MaxioSettings`, wire models, and the `AddMaxioBilling` DI extension.
- `PublicApi/SubscriptionEndpoints/*` — the three endpoints (MinimalApi.Endpoint `IEndpoint`
  pattern), DTOs and mapping. The JWT scheme is pinned explicitly because this host's default
  challenge scheme is the Identity cookie.

## Configuration

Bound from the `Maxio:` section (values come from user-secrets / environment — never committed):

| Key | Source env var | Notes |
|-----|----------------|-------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | HTTP Basic username (password is a literal `X`). |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Base URL is derived as `https://{Subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | The product family whose products are the plans. |
| `Maxio:BaseUrl` | — | Optional. When set, used verbatim as the API base address instead of deriving from the subdomain. |

Load them into user-secrets for the PublicApi project (PowerShell / bash), then run:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

## Run & verify

This machine has only the .NET 10 SDK (with the ASP.NET Core 8.0 runtime) and no SQL LocalDB, so
`global.json` rolls forward to the latest major and the app runs in-memory
(`UseOnlyInMemoryDatabase=true` in `appsettings.Development.json`).

```bash
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="https://localhost:7443;http://localhost:7444"
dotnet run --project src/PublicApi --no-launch-profile

# In another shell:
B=https://localhost:7443
TOKEN=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  | grep -oE '"token":"[^"]+"' | sed 's/"token":"//;s/"//')

curl -sk $B/api/subscription-plans -H "Authorization: Bearer $TOKEN"
curl -sk -X POST $B/api/subscriptions -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"planHandle":"eshop-pro"}'
curl -sk $B/api/my-subscriptions -H "Authorization: Bearer $TOKEN"
```

Swagger UI (with a **Bearer** auth button) is served at `https://localhost:7443/swagger`.
