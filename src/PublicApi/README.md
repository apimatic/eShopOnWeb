# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

An additive, parallel capability to the one-time Catalog → Basket → Order flow. It lets a
logged-in shopper browse recurring plans, subscribe, and see their subscriptions. **Maxio
Advanced Billing is the system of record** — no subscription state is persisted locally.

All three endpoints are JWT-authenticated (obtain a token from `POST /api/authenticate`); the
caller's identity is taken from the token, never from the request body.

| Method & route | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | Lists the plans a shopper can subscribe to (the products of the configured Maxio product family). |
| `POST /api/subscriptions` | Subscribes the caller to a plan. Body: `{ "planHandle": "eshop-pro" }`. Idempotent — a repeated or double-clicked request never creates a duplicate customer or subscription. |
| `GET /api/my-subscriptions` | Lists the caller's subscriptions as reported by Maxio. |

### How it works

- The `Microsoft.eShopWeb.Infrastructure.Maxio` namespace holds a thin, spec-faithful typed
  HTTP client (`MaxioApiClient`) built directly against the OpenAPI contract in `maxio-spec/`,
  plus `MaxioSubscriptionService`, which orchestrates the subscribe flow.
- Idempotency: each eShopOnWeb user maps to a single Maxio customer via the customer
  `reference` (the shopper's stable username). The subscribe flow looks the customer up before
  creating one, serializes concurrent requests per shopper, and returns an existing live
  subscription to the requested plan instead of creating a second one.

### Configuration

Settings bind from the `Maxio` configuration section. Provide them via **.NET user-secrets**
(never commit values):

| Key | Source env var | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | `MAXIO_API_KEY` | Used for HTTP Basic auth (`api_key:x`). |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Base URL is derived as `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Product family whose products are the plans. |
| `Maxio:BaseUrl` | _(optional)_ | When set, used verbatim instead of deriving from the subdomain. |

```bash
dotnet user-secrets --project src/PublicApi set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets --project src/PublicApi set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets --project src/PublicApi set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```
