# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

The `SubscriptionEndpoints` folder adds recurring-subscription billing on top of the existing
one-time commerce flow, using **Maxio Advanced Billing** as the system of record. All three
endpoints are JWT-authenticated and derive the caller's identity from the token.

| Method & route | Purpose |
|----------------|---------|
| `GET /api/subscription-plans` | List the plans a shopper can subscribe to (products in the configured Maxio product family). |
| `POST /api/subscriptions` | Subscribe the authenticated user to a plan (`{ "planHandle": "eshop-pro" }`). Ensures a Maxio customer exists (idempotent) and never creates a duplicate subscription for a plan the user already has. Returns `201` when newly created, `200` with `alreadySubscribed: true` when the user was already subscribed. |
| `GET /api/my-subscriptions` | List the authenticated user's subscriptions. |

### Configuration

Settings are bound from the `Maxio` configuration section and must be supplied via
**.NET user-secrets** (never committed to the repository):

| Key | Sourced from | Notes |
|-----|--------------|-------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | Used as the HTTP Basic username (password is `x`). |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Base URL is derived as `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | The family whose products are offered as plans. |
| `Maxio:BaseUrl` | _(optional)_ | When set, used verbatim as the API base address instead of deriving it from the subdomain. |

Set them once with:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

The integration is built against the authoritative Maxio OpenAPI specification in `maxio-spec/`.
