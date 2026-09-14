# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscription billing

Subscription billing is exposed as a JWT-authenticated capability alongside the existing catalog and basket APIs:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "..." }`
- `GET /api/my-subscriptions`

Set `Maxio:ApiKey`, `Maxio:Subdomain`, and `Maxio:ProductFamilyHandle` through configuration. For local development, the corresponding source environment variables are `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, and `MAXIO_DEFAULT_PRODUCT_FAMILY`. `Maxio:BaseUrl` is an optional full base-address override.

The integration resolves catalog entities by handle, uses deterministic Maxio customer and subscription references for idempotency, and persists only eShop-to-Maxio links locally. Plan, price, lifecycle state, currency, and next billing time are always read from Maxio Advanced Billing.
