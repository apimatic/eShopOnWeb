# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

`SubscriptionEndpoints/` adds recurring-subscription billing on top of Maxio Advanced Billing:
`GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`. It is
additive — the catalog endpoints and the storefront's one-time order flow are untouched. See
[docs/subscription-billing.md](../../docs/subscription-billing.md).
