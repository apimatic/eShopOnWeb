# Subscription billing (Maxio Advanced Billing)

An **additive, parallel** capability alongside the existing one-time commerce flow
(Catalog → Basket → Order). Maxio Advanced Billing is the **system of record** for
recurring subscriptions; eShopOnWeb stores no subscription state of its own.

## Endpoints (`src/PublicApi`, JWT-authenticated)

All three require a bearer token from `POST /api/authenticate`; the caller's identity is
taken from the token's name claim (never the request body).

| Method & route | Purpose |
|---|---|
| `GET /api/subscription-plans` | Lists the plans in the configured product family. |
| `POST /api/subscriptions` | Subscribes the caller to a plan. Body: `{ "planHandle": "<handle>" }`. Idempotent. |
| `GET /api/my-subscriptions` | Lists the caller's own subscriptions. |

## Design

- **Layering.** `ISubscriptionBillingService` + provider-agnostic models
  (`SubscriptionPlan`, `CustomerSubscription`, `SubscribeRequest`, `SubscribeResult`) live in
  `ApplicationCore`. The Maxio implementation (`MaxioBillingService`) and DI wiring
  (`AddMaxioBilling`) live in `Infrastructure/Maxio`. Endpoints live in
  `PublicApi/SubscriptionEndpoints`. No SDK type leaks out of the Infrastructure layer.
- **Identity → customer.** The eShop user identity (username == email) is used as the Maxio
  customer `reference`, so the user ↔ customer mapping is stable and does not depend on the
  in-memory database surviving a restart.
- **Idempotency.** Subscribing ensures a single customer exists (look up by `reference`, create
  only on 404, reconcile on a create race) and reuses an existing *live* subscription to the same
  plan instead of creating a duplicate — so a double-clicked subscribe never creates two
  customers or two subscriptions.
- **Failure handling.** Every provider/transport failure is converted to a single
  `SubscriptionBillingException` carrying a caller-safe message and an HTTP status: a provider 4xx
  the caller can act on is preserved (e.g. 400 unknown plan, 422 rejection), while transport
  failures (503) and unreadable/unknown responses (502) surface as 5xx. The API exception
  middleware maps that status onto the HTTP response.

## Configuration (`Maxio:` section)

Bound from configuration — **no secret values live in the repository**; load them into .NET
user-secrets.

| Key | Source env var | Required | Notes |
|---|---|---|---|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | yes | Basic-auth username (password is the literal `x`). |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | yes* | Derives the base address `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | yes | Product family whose products are the plans. |
| `Maxio:BaseUrl` | — | no | Verbatim base-address override; when set, used as-is instead of deriving from the subdomain. |
| `Maxio:Currency` | — | no | Display currency (Maxio's product model has no currency field). Default `USD`. |
| `Maxio:PaymentCollectionMethod` | — | no | Collection method for new subscriptions. Default `remittance`. |

\* Either `Maxio:Subdomain` or `Maxio:BaseUrl` must be provided.

Load the secrets (values never printed):

```bash
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"              --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"       --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

## Running & verifying locally

This machine has only the .NET 10 SDK and no SQL Server LocalDB, so run against the in-memory
database. `global.json` uses `rollForward: latestMajor`, so the .NET 10 SDK builds the net8.0
projects, which run on the installed ASP.NET Core 8.0 runtime.

```bash
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="https://localhost:9563;http://localhost:9564" \
UseOnlyInMemoryDatabase=true \
dotnet run --project src/PublicApi --no-launch-profile
```

Then, using a bearer token from `POST /api/authenticate`
(`demouser@microsoft.com` / `Pass@word1`):

```bash
# list plans
curl -sk https://localhost:9563/api/subscription-plans -H "Authorization: Bearer $TOKEN"
# subscribe (repeat to see idempotent reuse)
curl -sk -X POST https://localhost:9563/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"planHandle":"<plan-handle>"}'
# list my subscriptions
curl -sk https://localhost:9563/api/my-subscriptions -H "Authorization: Bearer $TOKEN"
```

Interactive Swagger UI is at `https://localhost:9563/swagger` (use **Authorize** with
`Bearer <token>`).
