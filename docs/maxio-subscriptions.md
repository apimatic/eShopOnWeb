# Maxio Advanced Billing — Subscription integration

Adds recurring-subscription billing to eShopOnWeb with **Maxio Advanced Billing** as the
system of record. This is an **additive, parallel** capability — the existing one-time
commerce flow (Catalog → Basket → Order) is untouched.

The hero flow: a logged-in shopper browses plans, subscribes to one, and sees it in their
account. A single Maxio customer is ensured per shopper (idempotent — a double-click never
creates two customers or two subscriptions), enrolled, and the plan/price/state/next-billing
date is confirmed back.

## Endpoints (src/PublicApi, JWT-authenticated)

The caller's identity comes from the JWT (the username claim, which is the shopper's email).
All three endpoints require a bearer token from `POST /api/authenticate`.

| Method & route | Purpose |
|---|---|
| `GET /api/subscription-plans` | Lists the plans (Maxio products in the configured product family). |
| `POST /api/subscriptions` | Subscribes the caller to a plan. Body: `{ "planHandle": "eshop-pro", "pricePointHandle": null }`. Idempotent: returns the existing subscription (HTTP 200) when already subscribed, otherwise creates it (HTTP 201). |
| `GET /api/my-subscriptions` | Lists the caller's subscriptions. |

## Architecture

- **ApplicationCore** — `ISubscriptionBillingService` plus framework-neutral models
  (`SubscriptionPlan`, `CustomerSubscription`, `SubscriberIdentity`). No vendor types leak here.
- **Infrastructure/Maxio** — `MaxioApiClient` (typed `HttpClient`, one method per spec operation),
  `MaxioBillingService` (orchestration, idempotency, mapping), `MaxioSettings`,
  `MaxioTransientFaultHandler` (retry with backoff), and `AddMaxioBilling(...)` DI wiring.
- **PublicApi/SubscriptionEndpoints** — the three endpoints + DTOs, following the project's
  `MinimalApi.Endpoint` + `[Authorize(AuthenticationSchemes = JwtBearerDefaults...)]` convention.

The Maxio OpenAPI spec in `maxio-spec/` is the authoritative contract. Operations used:
`GET /product_families/{handle:...}/products.json`, `GET /customers/lookup.json`,
`POST /customers.json`, `GET /customers/{id}/subscriptions.json`, `POST /subscriptions.json`.

### Idempotency

- **Customer**: keyed on a stable customer *reference* (the shopper's email). Ensure = lookup by
  reference; create only if absent; a losing create race (HTTP 422 "reference taken") re-reads.
- **Subscription**: before creating, the shopper's non-terminal subscription to the same plan is
  reused if present. Ensure-customer + duplicate-check + create are serialized per shopper with an
  in-process keyed lock so concurrent double-clicks create exactly one subscription.

## Configuration

Bound from the `Maxio:` configuration section. **Secret values never live in the repository** —
they are loaded into .NET user-secrets from environment variables.

| Key | Source env var | Notes |
|---|---|---|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | HTTP Basic username (password is the literal `x`). |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Base URL derives as `https://{Subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Product family whose products are the plans. |
| `Maxio:BaseUrl` | — | Optional; when set, used verbatim instead of the derived URL. |
| `Maxio:PaymentCollectionMethod` | — | Optional; defaults to `remittance` so shoppers subscribe without card capture. Use `invoice` for legacy sites, `automatic` to require a card. |

Load the secrets (from a shell where the env vars are set), run from `src/PublicApi`:

```bash
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

## Run & verify

This machine has only the .NET 10 SDK and no LocalDB, so:

```bash
# global.json already uses rollForward: latestMajor
export DOTNET_ROLL_FORWARD=Major
cd src/PublicApi
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="https://localhost:9763" \
  UseOnlyInMemoryDatabase=true dotnet run
```

Then (PowerShell; `-SkipCertificateCheck` because it is the dev cert):

```powershell
$base = "https://localhost:9763"
$auth = Invoke-RestMethod -SkipCertificateCheck -Uri "$base/api/authenticate" -Method Post `
  -ContentType "application/json" `
  -Body (@{ username = "demouser@microsoft.com"; password = "Pass@word1" } | ConvertTo-Json)
$H = @{ Authorization = "Bearer $($auth.token)" }

Invoke-RestMethod -SkipCertificateCheck -Uri "$base/api/subscription-plans" -Headers $H
Invoke-RestMethod -SkipCertificateCheck -Uri "$base/api/subscriptions" -Method Post -Headers $H `
  -ContentType "application/json" -Body (@{ planHandle = "eshop-pro" } | ConvertTo-Json)
Invoke-RestMethod -SkipCertificateCheck -Uri "$base/api/my-subscriptions" -Headers $H
```

The in-memory database is reset on each restart, but idempotency survives restarts because the
Maxio customer reference is the shopper's (stable) email — re-subscribing finds the same customer
and subscription in Maxio.
