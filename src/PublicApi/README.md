# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

PublicApi exposes the additive Maxio Advanced Billing flow below. Every endpoint requires a
JWT bearer token obtained from `POST /api/authenticate`; the token's identity, not a request
body field, selects the eShop shopper.

- `GET /api/subscription-plans` lists active products in `Maxio:ProductFamilyHandle`.
- `POST /api/subscriptions` accepts `{ "planHandle": "eshop-pro" }` and returns the Maxio
  subscription's plan, price, state, and next billing date.
- `GET /api/my-subscriptions` returns the authenticated shopper's Maxio subscriptions.

The integration follows the operations in `maxio-spec/openapi.yaml`: Maxio customer references
use the eShop Identity user ID and subscription references use `eshop:<user-id>`. The local
`SubscriptionMappings` table is an integration index only; Maxio remains the billing system of
record. Customer and subscription references plus a per-user lock make retries and double-clicks
idempotent. A second plan cannot be silently added; it returns `409 Conflict`.

Configure these values through the `Maxio` configuration section (user-secrets or a deployment
secret store): `ApiKey`, `Subdomain`, and `ProductFamilyHandle`. `BaseUrl` is optional; when set,
it overrides the spec-derived `https://{Subdomain}.chargify.com/` address.
