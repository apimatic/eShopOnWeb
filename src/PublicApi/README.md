# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Recurring subscriptions (Maxio Advanced Billing)

`SubscriptionEndpoints/` adds recurring-subscription billing alongside the existing one-time
Catalog → Basket → Order flow: `GET /api/subscription-plans`, `POST /api/subscriptions` and
`GET /api/my-subscriptions`, all JWT-authenticated.

See [docs/subscriptions-maxio.md](../../docs/subscriptions-maxio.md) for configuration, the
subscribe flow and its idempotency rules.
