# Subscription billing (Maxio Advanced Billing)

An **additive, parallel** capability to eShopOnWeb's one-time Catalog → Basket → Order flow.
A logged-in shopper can browse recurring plans, subscribe to one, and see it in their account.
**Maxio Advanced Billing is the system of record** — customers and subscriptions live in Maxio and are
keyed to eShopOnWeb users by the customer `reference`; nothing subscription-related is persisted locally.

## Endpoints (on `src/PublicApi`, JWT-authenticated)

| Method & route | Purpose |
|---|---|
| `GET /api/subscription-plans` | List subscribable plans in the configured product family. |
| `POST /api/subscriptions` | Subscribe the caller to a plan (`{ "planHandle": "eshop-pro" }`). Idempotent. |
| `GET /api/my-subscriptions` | List the caller's subscriptions as reported by Maxio. |

The caller's identity comes solely from the JWT. The stable user name is used as the Maxio customer
`reference`, so the same user always maps to the same Maxio customer.

## Design

- **Contract in `ApplicationCore`** (`ISubscriptionBillingService`, `SubscriptionPlan`, `CustomerSubscription`,
  `SubscriberInfo`) — SDK-agnostic and persistence-ignorant.
- **Implementation in `Infrastructure/Maxio`** using the official `Maxio.AdvancedBillingSdk` package
  (`MaxioSubscriptionBillingService`), plus settings (`MaxioSettings`), a client factory, and an optional
  base-url override handler.
- **Endpoints in `PublicApi/SubscriptionEndpoints`** follow the project's `MinimalApi.Endpoint` conventions;
  scoped services are resolved per request (no captive dependencies).

**Idempotency (a double-click never creates a second customer/subscription):**
1. The customer is looked up by `reference`; only created if absent (unique-reference races are re-read, not duplicated).
2. Before creating a subscription, an existing *live* subscription to the same plan is returned unchanged.
3. Subscribe operations are serialised per user with an in-process lock.

**Card-free enrolment:** the demo plans do not require a stored payment method, so subscriptions are created
with `payment_collection_method = remittance` (invoice) — they activate without card capture / 3-DS.

## Configuration

Settings bind from the `Maxio` configuration section — **values are never committed**; load them from the
environment into .NET user-secrets (see below):

| Key | From env var | Notes |
|---|---|---|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | Basic-auth username (password is the literal `x`). |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Advanced Billing site subdomain. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Family holding the plans. Handles are stable; numeric IDs are not. |
| `Maxio:BaseUrl` | — | *Optional* override; when set, used verbatim as the API base address instead of deriving from the subdomain. |
| `Maxio:Environment` | `MAXIO_ENVIRONMENT` | Optional, `US` (default) or `EU`. |

---

## Verify it yourself

> Prerequisites on this machine: only the .NET 10 SDK is installed and there is no LocalDB, so roll the SDK
> forward and use the in-memory database. `global.json` is already set to `rollForward: latestMajor`.

### 1. Load the Maxio credentials from the environment into user-secrets (once)

```bash
export DOTNET_ROLL_FORWARD=Major
P=src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"               --project "$P"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"        --project "$P"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project "$P"
dotnet user-secrets set "Maxio:Environment"         "$MAXIO_ENVIRONMENT"           --project "$P"
```

### 2. Run the PublicApi

```bash
export DOTNET_ROLL_FORWARD=Major
DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true \
  ASPNETCORE_URLS="https://localhost:6963;http://localhost:6964" \
  dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

Swagger is at <https://localhost:6963/swagger>. (Stop any previous instance first.)

### 3. Exercise the hero flow

```bash
B=https://localhost:6963

# Get a bearer token (PublicApi has its own JWT auth; the storefront cookie won't work here)
TOKEN=$(curl -sk -X POST "$B/api/authenticate" -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')

# Browse plans
curl -sk "$B/api/subscription-plans" -H "Authorization: Bearer $TOKEN"

# Subscribe (the hero flow) — confirms plan, price, state and next billing date
curl -sk -X POST "$B/api/subscriptions" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"planHandle":"eshop-pro"}'

# Repeat it — idempotent: same subscription id, no duplicate customer/subscription
curl -sk -X POST "$B/api/subscriptions" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"planHandle":"eshop-pro"}'

# See it reflected in the account
curl -sk "$B/api/my-subscriptions" -H "Authorization: Bearer $TOKEN"
```

Expected: an `active` subscription to **Pro Plan** at **$299.00/month** with a next-billing date one month out;
the two subscribe calls return the **same** subscription id. Calling any endpoint without a token returns **401**.

### 4. Run the tests

```bash
export DOTNET_ROLL_FORWARD=Major
dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj   # auth guards + existing suite
dotnet test tests/IntegrationTests/IntegrationTests.csproj                     # base-url override handler
dotnet test tests/UnitTests/UnitTests.csproj
```
