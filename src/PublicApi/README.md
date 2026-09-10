# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

An additive, parallel capability to the one-time Catalog/Basket/Order flow. Recurring
subscriptions are managed in **Maxio Advanced Billing**, which is the system of record —
there is no local persistence of subscriptions. eShopOnWeb users are mapped to Maxio
customers by a stable `reference` (the user's username), so the mapping survives even when
the app runs against the in-memory database.

### Endpoints (all JWT-authenticated; identity comes from the token)

| Method & route | Purpose |
|----------------|---------|
| `GET /api/subscription-plans` | Lists the plans in the configured product family. |
| `POST /api/subscriptions` | Subscribes the caller to a plan. Body: `{ "planHandle": "eshop-pro" }`. Returns **201** for a new subscription, **200** when a matching live subscription already existed (idempotent). |
| `GET /api/my-subscriptions` | Lists the caller's subscriptions. |

Get a bearer token from `POST /api/authenticate` first (e.g. `demouser@microsoft.com` /
`Pass@word1`), then send it as `Authorization: Bearer <token>`.

The subscribe flow is **idempotent**: it ensures a Maxio customer exists (lookup-or-create,
tolerant of concurrent creation), skips creating a duplicate when a live subscription to the
same plan already exists, serializes concurrent requests per customer in-process, and sends a
Maxio `uniqueness_token` so a transparently retried request is de-duplicated.

### Configuration

Settings are bound from the `Maxio` configuration section. Provide them via **user-secrets**
or environment variables — never commit the values:

| Key | Source env var | Notes |
|-----|----------------|-------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | Used as the HTTP Basic auth username. |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Base URL is derived as `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Product family whose products are the plans. |
| `Maxio:BaseUrl` | — | Optional. When set, used verbatim instead of deriving from the subdomain. |

Example (from the repo root):

```bash
dotnet user-secrets --project src/PublicApi set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets --project src/PublicApi set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets --project src/PublicApi set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```
