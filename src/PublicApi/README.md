# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

`SubscriptionEndpoints/` adds recurring-subscription billing backed by Maxio Advanced Billing
(`GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`). It is
additive to the catalog/basket/order flow. See
[SubscriptionEndpoints/README.md](SubscriptionEndpoints/README.md) for the contract, the idempotency
guarantees and the `Maxio:` configuration keys.
