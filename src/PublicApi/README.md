# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

`SubscriptionEndpoints/` adds recurring-subscription billing backed by Maxio Advanced
Billing — `GET /api/subscription-plans`, `POST /api/subscriptions` and
`GET /api/my-subscriptions`. It runs alongside the existing catalog/basket/order flow and
changes nothing in it. See [docs/maxio-subscription-billing.md](../../docs/maxio-subscription-billing.md)
for configuration, the Maxio endpoints used, and how idempotency is guaranteed.
