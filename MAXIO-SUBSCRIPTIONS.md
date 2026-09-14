# Maxio subscription billing (additive)

Recurring-subscription billing for eShopOnWeb, with **Maxio Advanced Billing** as the system of
record. This is **additive and parallel** to the existing one-time Catalog → Basket → Order flow —
nothing in that flow changed.

## What it does — the Subscribe flow

A logged-in shopper browses plans, subscribes to one, and sees it on their account. On subscribe the
integration ensures a Maxio customer exists for the eShopOnWeb user (idempotent on the user's identity,
so a double-click never creates two customers or two subscriptions), enrols them, and returns the
plan / price / state / next-billing-date.

## HTTP surface (`src/PublicApi`, JWT-authenticated)

| Method & route | Purpose |
|---|---|
| `GET  /api/subscription-plans` | List plans in the configured product family |
| `POST /api/subscriptions`      | Subscribe the caller to a plan (body: `{ "planHandle": "eshop-pro" }`) |
| `GET  /api/my-subscriptions`   | List the caller's subscriptions |

The caller's identity comes only from the JWT (its name claim, an email) — never from the request body.
That identity is the stable Maxio customer `reference`, which is what makes provisioning idempotent and
survives an in-memory-database restart.

## Architecture (clean, matches eShopOnWeb layering)

- **ApplicationCore** `Subscriptions/` — the port `ISubscriptionBillingService`, domain models
  (`SubscriptionPlan`, `CustomerSubscription`, `SubscriberIdentity`) and `BillingException`. No SDK dependency.
- **Infrastructure** `Billing/` — the Maxio adapter (`MaxioBillingService`), settings (`MaxioSettings`),
  the client factory (`MaxioClientFactory`) and DI (`AddMaxioBilling`). This is the only project that
  references the Maxio SDK (`AsadAli.AdvancedBilling.Sdk`).
- **PublicApi** `SubscriptionEndpoints/` — the three endpoints + DTOs, following the project's
  `IEndpoint` convention.

Notes on the adapter: subscriptions are billed by **invoice/remittance** (no card is captured, matching
the "payment method not required" plans); every SDK failure — typed errors, raw errors, unparseable
bodies, transport failures — is normalised to a `BillingException` carrying a caller-safe message and an
HTTP status (provider 4xx the caller can act on stays 4xx; everything else becomes 502/503).

## Configuration (`Maxio:` section — no secrets in the repo)

Bind from these keys (values via user-secrets / environment, never hard-coded):

| Key | From env var | Meaning |
|---|---|---|
| `Maxio:ApiKey`              | `MAXIO_API_KEY`               | API key (HTTP Basic username) |
| `Maxio:Subdomain`           | `MAXIO_SITE_SUBDOMAIN`        | Site subdomain (used to derive the base URL) |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY`| Product family whose plans are offered |
| `Maxio:BaseUrl`             | *(optional)*                 | Explicit base URL; when set, used verbatim instead of deriving from the subdomain |

Load them into user-secrets for `src/PublicApi` (values stay outside the repo):

```bash
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"               --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"        --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

## Verify

```bash
# 1. Run PublicApi (SDK rolls forward to the installed .NET; app uses the in-memory database)
ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true DOTNET_ROLL_FORWARD=Major \
  ASPNETCORE_URLS="https://localhost:6903" dotnet run --project src/PublicApi --no-launch-profile

# 2. Get a bearer token (demo user)
TOKEN=$(curl -sk -X POST https://localhost:6903/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | python -c 'import sys,json;print(json.load(sys.stdin)["token"])')

# 3. Browse plans, subscribe, confirm
curl -sk https://localhost:6903/api/subscription-plans -H "Authorization: Bearer $TOKEN"
curl -sk -X POST https://localhost:6903/api/subscriptions -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"planHandle":"eshop-pro"}'
curl -sk https://localhost:6903/api/my-subscriptions -H "Authorization: Bearer $TOKEN"
```

Automated tests: `dotnet test tests/PublicApiIntegrationTests` (the subscription endpoint tests stub the
billing port, so they need no network / Maxio credentials).
