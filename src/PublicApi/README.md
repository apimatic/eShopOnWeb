# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

An additive, parallel capability alongside the one-time Catalog → Basket → Order flow. It
lets a signed-in shopper browse plans, subscribe, and see their subscriptions. Maxio
Advanced Billing is the system of record; eShopOnWeb stores no subscription state.

All three endpoints are JWT-authenticated (get a bearer token from `POST /api/authenticate`).
The caller's identity comes from the token — never the request body.

| Method & route | Purpose |
|----------------|---------|
| `GET /api/subscription-plans` | Lists the plans (products in the configured Maxio product family). |
| `POST /api/subscriptions` | Subscribes the caller to a plan. Body: `{ "planHandle": "eshop-pro" }` (optional — defaults to the premium plan). **Idempotent**: ensures one Maxio customer per user and returns the existing subscription if already enrolled in that plan, so a double-click never creates duplicates. |
| `GET /api/my-subscriptions` | Lists the caller's own subscriptions. |

### Configuration

Settings are bound from the `Maxio:` configuration section (values come from configuration /
user-secrets — never hard-coded, so the same build runs against any Maxio site and catalog):

| Key | Source env var | Notes |
|-----|----------------|-------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | Per-site API key (Basic-auth username; password is the literal `x`). |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Site subdomain; the API base URL is derived as `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Product family whose products are the subscribable plans. |
| `Maxio:BaseUrl` | — | Optional. When set, used verbatim as the API base address instead of deriving one from the subdomain. |

Load the secrets into .NET user-secrets for the PublicApi project (values are read from the
environment, never written into the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

The subscription endpoints require Maxio to be configured; the rest of the API boots and runs
without it (validation is deferred to first use).
