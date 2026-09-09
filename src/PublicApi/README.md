# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

An additive, parallel capability to the one-time Catalog → Basket → Order flow: shoppers can
subscribe to recurring plans, with **Maxio Advanced Billing** as the system of record. It does not
change the existing storefront checkout.

All endpoints are JWT-authenticated (obtain a bearer token from `POST /api/authenticate`). The
caller's identity is taken from the token (`ClaimTypes.Name`, which is the eShopOnWeb username/email)
and mapped to a Maxio customer via the customer `reference` field.

| Method | Route | Purpose |
|--------|-------|---------|
| `GET`  | `/api/subscription-plans` | List the plans a shopper can subscribe to (products in the configured Maxio product family). |
| `POST` | `/api/subscriptions` | Subscribe the caller to a plan. Body: `{ "planHandle": "eshop-pro" }`. Returns `201 Created` for a new subscription or `200 OK` (`alreadyExisted: true`) if the shopper is already subscribed. |
| `GET`  | `/api/my-subscriptions` | List the caller's own subscriptions. |

The subscribe flow is **idempotent**: it ensures at most one Maxio customer per user and at most one
live subscription per (user, plan). A concurrent burst (e.g. a double-click) collapses to a single
subscription — see `Infrastructure/Maxio/MaxioBillingService`.

### Configuration

Settings bind from the `Maxio` configuration section. **Secrets must not be committed** — load them
into .NET user-secrets. The keys, and the environment variables they come from:

| Config key | From environment variable | Notes |
|------------|---------------------------|-------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | Maxio API key (HTTP Basic username; password is `X`). |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Site subdomain; the API base is derived as `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Product family whose products are offered as plans. |
| `Maxio:BaseUrl` | _(optional)_ | Explicit API base; used verbatim when set, overriding the subdomain-derived value. |

Example (PowerShell), reading the values from the environment so they never appear in a file:

```pwsh
dotnet user-secrets --project src/PublicApi set "Maxio:ApiKey" $env:MAXIO_API_KEY
dotnet user-secrets --project src/PublicApi set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN
dotnet user-secrets --project src/PublicApi set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY
```
