# Maxio subscription billing (eShopOnWeb)

Recurring-subscription billing added to eShopOnWeb, with **Maxio Advanced Billing** as the system of
record. This is **additive and parallel** to the existing one-time commerce flow (Catalog → Basket →
Order); it does not replace it. Nothing is persisted locally — Maxio is the source of truth, and the
eShopOnWeb user is mapped to a Maxio customer via a stable `reference` derived from their user name.

## Endpoints (on `src/PublicApi`, JWT-authenticated)

All three require a Bearer JWT from `POST /api/authenticate`; the caller's identity comes from the token.

| Method & route | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | Lists the plans a shopper can subscribe to (the products in the configured Maxio product family). |
| `POST /api/subscriptions` | Subscribes the caller to a plan. Body: `{ "planHandle": "eshop-pro" }`. Ensures a Maxio customer exists for the user and enrolls them. **Idempotent.** |
| `GET /api/my-subscriptions` | Lists the caller's subscriptions (plan, price, state, next billing date). |

### Idempotency (double-click safe)
`POST /api/subscriptions` never creates duplicate customers or subscriptions:
1. The eShopOnWeb user maps to a Maxio customer by a stable, per-site-unique `reference`
   (their lower-cased user name). The customer is looked up first and only created if missing.
2. Before creating a subscription, the user's existing subscriptions are checked; if a **live**
   subscription to the same plan already exists it is returned (`alreadySubscribed: true`) instead of
   creating another.
3. Concurrent requests for the same user are serialized by an in-process per-user lock, so a genuine
   double-click resolves to a single subscription. (A cross-process duplicate-customer race would hit
   Maxio's per-site `reference` uniqueness and is recovered by re-looking up the customer.)

## Configuration

Settings bind from the `Maxio:` configuration section (no values are committed to the repo):

| Key | Source env var | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | `MAXIO_API_KEY` | Used as HTTP Basic auth username (password is the literal `x`). |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | API base is derived as `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | The family whose products are offered as plans. |
| `Maxio:BaseUrl` | — | Optional. When set, used verbatim as the API base address instead of deriving one from the subdomain. |

Load the sandbox credentials into .NET user-secrets for `src/PublicApi` (never into a repo file):

```bash
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"              --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"       --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

## Design / where the code lives

- **`src/ApplicationCore/Interfaces/ISubscriptionService.cs`** + **`src/ApplicationCore/Subscriptions/`** —
  the domain-facing abstraction and plain models (`SubscriptionPlan`, `CustomerSubscription`,
  `SubscribeResult`). No Maxio types leak into the domain.
- **`src/Infrastructure/Maxio/`** — the integration: `MaxioSettings`, a thin typed `MaxioApiClient`
  (HttpClient over the confirmed Maxio REST endpoints), `MaxioSubscriptionService` (orchestration,
  idempotency, mapping), internal JSON DTOs, and typed exceptions.
- **`src/PublicApi/SubscriptionEndpoints/`** — the three `IEndpoint` endpoints + request/response DTOs,
  following the project's existing minimal-API conventions.
- **`src/PublicApi/Configuration/MaxioServiceCollectionExtensions.cs`** — DI wiring (binds settings,
  registers the typed client with base address + Basic auth, registers the service).

The integration talks to Maxio over plain HTTP (typed `HttpClient`) rather than the SDK, chiefly so the
optional `Maxio:BaseUrl` override can be honored verbatim. Every endpoint, field, and shape was confirmed
against the official Maxio .NET SDK contract docs and verified live against the sandbox before use.

## Running & verifying — see the steps in the project handoff / README notes below

This machine has an SDK/runtime quirk: `global.json` rolls forward (`latestMajor`) so the installed
.NET 10 SDK is used, and the app must run with `UseOnlyInMemoryDatabase=true` (no LocalDB present).

```bash
# from repo root
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development           # so user-secrets load
export UseOnlyInMemoryDatabase=true                 # no LocalDB on this machine
export ASPNETCORE_URLS="https://localhost:6983;http://localhost:6984"
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile

# in another shell — get a token, then call the endpoints
API=https://localhost:6983/api
TOKEN=$(curl -sk -X POST $API/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  | grep -o '"token":"[^"]*"' | sed 's/"token":"//;s/"//')

curl -sk $API/subscription-plans -H "Authorization: Bearer $TOKEN"
curl -sk -X POST $API/subscriptions -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"planHandle":"eshop-pro"}'
curl -sk $API/my-subscriptions -H "Authorization: Bearer $TOKEN"
```
