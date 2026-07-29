# Maxio Subscription Billing (eShopOnWeb)

An **additive, parallel** capability that lets a logged-in shopper subscribe to a recurring
plan, with **Maxio Advanced Billing** as the system of record. It does not touch the existing
Catalog → Basket → Order flow.

The hero flow — **Subscribe** — is exposed as three JWT-authenticated HTTP endpoints on the
**`src/PublicApi`** project:

| Method | Route | Purpose |
| ------ | ----- | ------- |
| `GET`  | `/api/subscription-plans` | List the plans a shopper can subscribe to (from the configured Maxio product family). |
| `POST` | `/api/subscriptions`      | Subscribe the caller to a plan (`{ "planHandle": "eshop-pro" }`). Idempotent. |
| `GET`  | `/api/my-subscriptions`   | List the caller's subscriptions. |

The caller's identity comes from the JWT; the eShop user id is used as the Maxio customer
**`reference`**, so the user ↔ customer mapping is stable and creation is idempotent.

---

## How it works

- **Ensure a Maxio customer exists** (idempotent): look the customer up by `reference` (the eShop
  user id); create it only if absent. A create that loses a race to a concurrent request (HTTP 422
  on the unique reference) falls back to a re-lookup — a double-click never creates two customers.
- **Subscribe** (idempotent): if a live subscription to the same plan already exists it is returned
  as-is (HTTP `200`, `alreadyExisted: true`); otherwise a new one is created (HTTP `201`). A
  per-(user, plan) in-process gate serializes concurrent attempts so the pre-check reliably
  de-duplicates near-simultaneous double-clicks, and each create carries a `uniqueness_token` that
  protects network-timeout retries.
- **No card capture**: the seeded plans do not require a payment method, so subscriptions are created
  with `payment_collection_method: "remittance"` (invoice billing) — no card / no 3-DS. Plans that
  *do* require a stored payment method are rejected with a clear `422` (this integration does not
  collect card details).
- **All Maxio calls** go through a typed `HttpClient` using HTTP Basic auth (API key as username,
  `X` as password) with transient-fault retries (HTTP 429 / 5xx / network) and Maxio's 120s timeout.

### Where the code lives

| Layer | Files |
| ----- | ----- |
| **ApplicationCore** (provider-agnostic port + models) | `Interfaces/ISubscriptionBillingService.cs`, `Subscriptions/*.cs`, `Exceptions/SubscriptionBillingException.cs` |
| **Infrastructure** (Maxio adapter) | `Maxio/MaxioBillingService.cs`, `Maxio/MaxioApiClient.cs`, `Maxio/MaxioSettings.cs`, `Maxio/MaxioServiceCollectionExtensions.cs`, `Maxio/Models/MaxioModels.cs` |
| **PublicApi** (endpoints + identity glue) | `SubscriptionEndpoints/*Endpoint.cs`, `SubscriptionEndpoints/CurrentUserSubscriptionService.cs`, DTOs; wired in `Program.cs`; error mapping in `Middleware/ExceptionMiddleware.cs` |

---

## Configuration

Settings are bound from the **`Maxio:`** configuration section — nothing is hard-coded, so the same
build runs against any Maxio site/catalog:

| Key | Source env var | Notes |
| --- | -------------- | ----- |
| `Maxio:ApiKey` | `MAXIO_API_KEY` | HTTP Basic auth username. **Secret.** |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Used to derive `https://{subdomain}.chargify.com/`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Product family whose products are the plans. |
| `Maxio:BaseUrl` | — | Optional. When set, used verbatim instead of deriving from the subdomain. |

**Secrets never enter the repository.** The Maxio credentials are stored in **.NET user-secrets**
(outside the repo). Registration is **boot-safe**: with no Maxio settings the app still starts and
only the subscription endpoints report a clear `503 Not configured` when invoked.

Load the secrets from the environment (values stay in the shell, never written to a file):

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

---

## Verify it yourself

### 0. Prerequisites (this machine)

- Only the .NET 10 SDK is installed but the app targets `net8.0`. `global.json` uses
  `rollForward: latestMajor`; run every `dotnet` command with **`DOTNET_ROLL_FORWARD=Major`**.
- No SQL Server LocalDB here — run with **`UseOnlyInMemoryDatabase=true`**.
  (In-memory data resets on restart, so a subscribe from a previous run maps to a new user id and
  a new Maxio customer on the next run — the mapping only persists within a single run.)
- Ensure the HTTPS dev cert is trusted: `dotnet dev-certs https --check`.
- The secrets above are loaded into user-secrets.

### 1. Build

```bash
export DOTNET_ROLL_FORWARD=Major
dotnet build src/PublicApi/PublicApi.csproj
```

### 2. Run PublicApi

```bash
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development
export UseOnlyInMemoryDatabase=true
export ASPNETCORE_URLS="https://localhost:7423;http://localhost:7424"
dotnet run --project src/PublicApi/PublicApi.csproj
```

Wait for `Now listening on: https://localhost:7423`. (Swagger UI: `https://localhost:7423/swagger`.)

### 3. Get a bearer token

```bash
B=https://localhost:7423
TOKEN=$(curl -sk -X POST "$B/api/authenticate" \
  -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  | grep -o '"token":"[^"]*"' | cut -d'"' -f4)
AUTH="Authorization: Bearer $TOKEN"
```

### 4. List plans

```bash
curl -sk "$B/api/subscription-plans" -H "$AUTH"
```
Returns the `basic-plan` ($29.00/mo) and `eshop-pro` ($299.00/mo) plans.

### 5. Subscribe (the hero flow)

```bash
curl -sk -X POST "$B/api/subscriptions" -H "$AUTH" \
  -H 'Content-Type: application/json' -d '{"planHandle":"eshop-pro"}'
```
Returns **HTTP 201** with the subscription — `state: active`, `formattedPrice: "299.00"`,
`nextBillingAt` one month out, `alreadyExisted: false`.

### 6. Prove idempotency (double-click)

Run the exact same `POST` again:
```bash
curl -sk -X POST "$B/api/subscriptions" -H "$AUTH" \
  -H 'Content-Type: application/json' -d '{"planHandle":"eshop-pro"}'
```
Returns **HTTP 200** with the **same subscription id** and `alreadyExisted: true` — no duplicate.

### 7. Confirm it's on the account

```bash
curl -sk "$B/api/my-subscriptions" -H "$AUTH"
```
Lists exactly one active `eshop-pro` subscription. You can also confirm it in the Maxio UI under the
customer whose reference equals the eShop user id.

### Expected error behavior

| Case | Result |
| ---- | ------ |
| No / invalid bearer token | `401 Unauthorized` |
| Unknown `planHandle` | `404` with the list of available handles |
| Missing `planHandle` | `422` |
| Plan requires a stored payment method | `422` (card capture is out of scope) |
| Maxio not configured | `503` |

---

## Notes & caveats

- **Customer name.** eShop identities carry only an email; Maxio requires a name, so a first/last
  name is derived from the email local part. The email and `reference` remain the meaningful keys.
- **Single-instance idempotency.** The concurrent-double-click guard is an in-process lock. A
  multi-instance deployment would complement it with a distributed lock; the pre-check + Maxio's
  `uniqueness_token` still limit duplicates across instances.
- **All Maxio knowledge** in this integration comes from the Maxio documentation (via the maxio-docs
  reference); no external/guessed API behavior is relied upon.
