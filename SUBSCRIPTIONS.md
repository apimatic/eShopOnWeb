# Maxio subscription billing (PublicApi)

An additive, parallel capability on `src/PublicApi`: a logged-in shopper browses subscription
plans, subscribes to one, and sees it in their account. **Maxio Advanced Billing** is the billing
system of record; the existing Catalog → Basket → Order flow is untouched.

## What was added

| Layer | What |
|-------|------|
| `src/Maxio/` | Vendored Maxio Advanced Billing .NET SDK (generated source; builds as `netstandard2.0`). Opted out of the repo's central package management via a local `Directory.Packages.props`. |
| `src/ApplicationCore/Subscriptions/` | `ISubscriptionBillingService` + plain domain DTOs (`SubscriptionPlan`, `BillingSubscription`, `SubscribeResult`, `SubscriberIdentity`) and `SubscriptionBillingException`. No SDK type leaks past this boundary. |
| `src/Infrastructure/Maxio/` | `MaxioSettings` (bound from the `Maxio:` config section, fail-fast validated at startup), `MaxioServiceCollectionExtensions.AddMaxioBilling`, and `MaxioSubscriptionBillingService` (the SDK integration + error boundary). |
| `src/PublicApi/SubscriptionEndpoints/` | Three JWT-authenticated endpoints and their DTOs. |

### Endpoints

| Method & route | Purpose |
|----------------|---------|
| `GET /api/subscription-plans` | List the plans in the configured product family. |
| `POST /api/subscriptions` | Subscribe the authenticated user to a plan. Body: `{ "planHandle": "eshop-pro" }` (optional; defaults to `Maxio:DefaultPlanHandle`). |
| `GET /api/my-subscriptions` | List the authenticated user's subscriptions. |

The subscriber is taken from the JWT (`ClaimTypes.Name`), never from the request body, and is used
as the Maxio customer `reference` — the idempotency anchor tying one eShop user to one Maxio customer.

## Configuration

All settings bind from the `Maxio:` configuration section. **Secrets are read from the environment
and loaded into .NET user-secrets — their values never live in the repo.**

| Config key | Source env var | Required |
|------------|----------------|----------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | yes |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | yes |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | yes |
| `Maxio:BaseUrl` | — (optional override; used verbatim when set, else derived from the subdomain) | no |
| `Maxio:DefaultPlanHandle` | — (plan used when the subscribe body omits one) | no |
| `Maxio:PaymentCollectionMethod` | — (`remittance` for Relationship Invoicing sites, `invoice` for legacy; default `remittance`) | no |
| `Maxio:TimeoutSeconds` | — (per-call deadline; default 100) | no |

If any required key is missing/blank the host **refuses to start** (fail-fast) rather than failing on
the first call.

---

## Verify it yourself

Prerequisites: the .NET 10 SDK is installed (the SDK uses C# 14). `global.json` already rolls
forward (`latestMajor`); run with `DOTNET_ROLL_FORWARD=Major`. Ensure the HTTPS dev cert is trusted:
`dotnet dev-certs https --check` (add `--trust` if needed).

### 1. Load the sandbox credentials into user-secrets (values stay out of the repo)

The credentials arrive as environment variables. Load them into PublicApi's user-secrets store:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
dotnet user-secrets set "Maxio:DefaultPlanHandle"   "eshop-pro"   # default subscribe target
cd ../..
```

### 2. Run PublicApi

No SQL Server LocalDB is required — use the in-memory database. Bind to your assigned ports
(21603/21604 shown):

```bash
DOTNET_ROLL_FORWARD=Major \
ASPNETCORE_ENVIRONMENT=Development \
UseOnlyInMemoryDatabase=true \
ASPNETCORE_URLS="https://localhost:21603;http://localhost:21604" \
dotnet run --project src/PublicApi -c Debug --no-launch-profile
```

Swagger UI: <https://localhost:21603/swagger>.

### 3. Get a JWT (the storefront cookie does not work here)

```bash
TOKEN=$(curl -sk -X POST https://localhost:21603/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
AUTH="Authorization: Bearer $TOKEN"
```

### 4. List plans

```bash
curl -sk https://localhost:21603/api/subscription-plans -H "$AUTH"
```

Expect Basic Plan ($29.00/mo) and Pro Plan ($299.00/mo).

### 5. Subscribe (the hero flow)

```bash
curl -sk -X POST https://localhost:21603/api/subscriptions \
  -H "$AUTH" -H "Content-Type: application/json" -d '{"planHandle":"eshop-pro"}'
```

Expect `201 Created` with the subscription: plan `eshop-pro`, price `$299.00`, state `active`, a
`nextBillingDate`, and `"alreadySubscribed": false`.

### 6. Prove idempotency (double-click)

Run the exact same POST again:

```bash
curl -sk -X POST https://localhost:21603/api/subscriptions \
  -H "$AUTH" -H "Content-Type: application/json" -d '{"planHandle":"eshop-pro"}'
```

Expect `200 OK`, the **same** `subscriptionId`, and `"alreadySubscribed": true` — no second customer
or subscription is created.

### 7. Confirm it in the account

```bash
curl -sk https://localhost:21603/api/my-subscriptions -H "$AUTH"
```

Expect exactly one active subscription to `eshop-pro`.

Negative checks: calling any endpoint without a bearer token returns `401`; subscribing to an unknown
plan returns `400`.

### 8. Run the tests

```bash
DOTNET_ROLL_FORWARD=Major dotnet test tests/UnitTests/UnitTests.csproj
```

The `MaxioSubscriptionBillingServiceTests` exercise the integration through the SDK's `HttpClient`
seam: plan mapping, create-customer-then-subscription, the idempotent no-op, unknown-plan and
422/5xx error translation.
