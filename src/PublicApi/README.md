# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

`SubscriptionEndpoints/` adds recurring-subscription billing on top of the one-time
Catalog/Basket/Order flow, with Maxio Advanced Billing as the system of record:

- `GET /api/subscription-plans`
- `POST /api/subscriptions`
- `GET /api/my-subscriptions`

Configuration, the idempotency design and the sourced Maxio contract are documented in
[docs/subscription-billing-maxio.md](../../docs/subscription-billing-maxio.md).
