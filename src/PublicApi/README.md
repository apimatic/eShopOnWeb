# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription endpoints

`SubscriptionEndpoints/` adds recurring-subscription billing (`/api/subscription-plans`,
`/api/subscriptions`, `/api/my-subscriptions`) on top of Maxio Advanced Billing, additive to the
one-time Catalog/Basket/Order flow. See [docs/subscriptions.md](../../docs/subscriptions.md) for the
contract, configuration keys and idempotency guarantees.
