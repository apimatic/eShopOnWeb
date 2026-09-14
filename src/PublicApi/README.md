# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

An additive, JWT-authenticated capability for recurring-subscription billing, backed by
**Maxio Advanced Billing** as the system of record. It runs in parallel to the existing
one-time commerce flow (Catalog → Basket → Order) and does not change it.

Endpoints (all under `/api/`, all require a bearer token from `POST /api/authenticate`):

| Method & route | Purpose |
|----------------|---------|
| `GET /api/subscription-plans` | Lists the plans a shopper can subscribe to (the products of the configured product family). |
| `POST /api/subscriptions` | Subscribes the caller to a plan. Body: `{ "planHandle": "eshop-pro" }`. Ensures a Maxio customer exists for the user (idempotent) and enrolls them, returning the confirmed plan, price, state and next billing date. Returns `201 Created` for a new subscription or `200 OK` when an active subscription already exists. |
| `GET /api/my-subscriptions` | Lists the caller's subscriptions as reflected in their billing account. |

The caller's identity comes entirely from the JWT. The resolved user name is used as the
Maxio customer `reference`, and each `{user, plan}` maps to a deterministic subscription
`reference`; both are unique in Maxio, so a double-click never creates a second customer or
a duplicate subscription.

### Configuration

Settings are bound from the `Maxio` configuration section. Provide values via **.NET
user-secrets** (never commit secret values):

| Key | Source env var | Notes |
|-----|----------------|-------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | Maxio (Chargify) API key. Used as HTTP Basic username; password is a literal `x`. |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Site subdomain. Base URL is derived as `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Handle of the product family whose products are offered as plans. |
| `Maxio:BaseUrl` | — | Optional. When set, used verbatim as the API base address instead of deriving it from the subdomain. |

```bash
dotnet user-secrets --project src/PublicApi set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets --project src/PublicApi set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets --project src/PublicApi set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

The integration is built strictly against the Maxio OpenAPI specification in `maxio-spec/`.
