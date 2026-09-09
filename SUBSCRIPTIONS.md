# Subscription billing (Maxio Advanced Billing)

An additive, parallel capability on top of eShopOnWeb's one-time commerce: logged-in shoppers can
browse recurring plans, subscribe, and see their subscriptions. **Maxio Advanced Billing** is the
system of record — eShopOnWeb stores no subscription state of its own. The
[`maxio-spec/`](maxio-spec/) OpenAPI document is the authoritative contract for every Maxio call.

## Endpoints (on `src/PublicApi`, JWT-authenticated)

All three require a Bearer token (obtain one from `POST /api/authenticate`). The subscriber identity is
taken from the token, never from the request body.

| Method & route | Purpose |
|----------------|---------|
| `GET  /api/subscription-plans` | Lists the plans in the configured Maxio product family. |
| `POST /api/subscriptions` | Subscribes the caller to a plan. Body: `{ "planHandle": "eshop-pro" }`. Idempotent — a repeat returns the existing subscription (`alreadyExisted: true`) instead of creating a duplicate. Returns `201` when created, `200` when it already existed. |
| `GET  /api/my-subscriptions` | Lists the caller's subscriptions (empty if they never subscribed). |

The `POST` response confirms the plan, price, state and next billing date back to the caller.

## How it works

- **`src/ApplicationCore/Subscriptions/`** — provider-agnostic abstraction: `ISubscriptionBillingService`
  plus the `SubscriptionPlan`, `CustomerSubscription`, `SubscribeResult`, and `BillingUser` models.
- **`src/Infrastructure/Maxio/`** — the Maxio implementation: a typed `MaxioClient` (`IMaxioClient`) built
  against the spec, the orchestrating `MaxioBillingService`, options (`MaxioSettings`), and DI wiring
  (`AddMaxioBilling`).
- **`src/PublicApi/SubscriptionEndpoints/`** — the three HTTP endpoints.

**Idempotency.** The eShopOnWeb user's email is used as the stable Maxio customer `reference`, so
"ensure customer" is idempotent across requests and restarts. Subscribe also serializes concurrent
requests per user (guarding against a double-click) and skips creation when a live subscription for the
same plan already exists. A concurrent customer-creation race (Maxio `422` on the unique reference) is
recovered by re-reading the customer.

**Card-free subscribe.** Subscriptions are created with `payment_collection_method: remittance`, so no
card capture / 3-DS is attempted at signup (the demo plans are configured with payment not required).

## Configuration

Settings bind from the `Maxio` configuration section. **No values live in the repo** — they are supplied
at runtime (user-secrets / environment). Keys:

| Config key | Sourced from env var | Notes |
|------------|----------------------|-------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | HTTP Basic username (password is the literal `x`). |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | API base URL is derived as `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Product family whose products are the plans. |
| `Maxio:BaseUrl` | — | Optional. When set, used verbatim instead of deriving from the subdomain. |

Load the sandbox credentials into user-secrets for the PublicApi project (values are read from your
environment; they are never written to a file in the repo):

```bash
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"               --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"        --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

## Run & verify

```bash
# From the repo root, run the PublicApi (in-memory DB; ports 30403/30404).
ASPNETCORE_ENVIRONMENT=Development \
UseOnlyInMemoryDatabase=true \
ASPNETCORE_URLS="https://localhost:30403;http://localhost:30404" \
DOTNET_ROLL_FORWARD=Major \
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile

# In another shell: authenticate, list plans, subscribe, confirm.
B=https://localhost:30403
TOKEN=$(curl -sk -X POST "$B/api/authenticate" -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)

curl -sk "$B/api/subscription-plans"  -H "Authorization: Bearer $TOKEN"
curl -sk -X POST "$B/api/subscriptions" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"planHandle":"eshop-pro"}'
curl -sk "$B/api/my-subscriptions"    -H "Authorization: Bearer $TOKEN"
```

Interactive docs are also available at `https://localhost:30403/swagger` (tag **SubscriptionEndpoints**).
