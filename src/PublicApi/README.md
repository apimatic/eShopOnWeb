# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

Recurring-subscription billing runs in parallel to the one-time catalog/basket/checkout flow. Maxio Advanced Billing is the billing system of record; a local `SubscriptionRecord` (persisted in `CatalogContext`) links eShopOnWeb users to their Maxio subscriptions.

### Endpoints (JWT bearer authentication — get a token from `POST /api/authenticate`)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/subscription-plans` | Lists the plans in the configured Maxio product family, marking the default plan |
| POST | `/api/subscriptions` | Enrolls the authenticated user in a plan (`{"planHandle": "..."}`, or empty body for the default). Idempotent: a live subscription is returned instead of duplicated (`alreadySubscribed: true`) |
| GET | `/api/my-subscriptions` | Lists the authenticated user's subscriptions from Maxio, including state, price and next-billing date |

### Configuration

Settings are bound from the `Maxio:` configuration section. Supply values via .NET user secrets (Development) or environment variables — never hard-code them:

| Key | Environment variable | Notes |
|-----|----------------------|-------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | Maxio API key |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Site subdomain |
| `Maxio:Environment` | `MAXIO_ENVIRONMENT` | e.g. `US` |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Product family containing the plans |
| `Maxio:DefaultPlanHandle` | — | Plan used when the request omits `planHandle` |
| `Maxio:BaseUrl` | — | Optional; when set, used verbatim as the API base address instead of deriving one from the subdomain |

Example: `dotnet user-secrets set "Maxio:ApiKey" <value> --project src/PublicApi`

### Idempotency

- The Maxio customer is keyed by the ASP.NET Identity user id used as the Maxio customer `reference`; it is looked up before creation, and a duplicate-reference race falls back to lookup.
- Before creating a subscription, Maxio (customer id + product handle) and the local record store are checked for a live subscription on the same plan; an existing one is returned instead of creating a second.


