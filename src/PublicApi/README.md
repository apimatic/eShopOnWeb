# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription endpoints

`SubscriptionEndpoints/` adds recurring-subscription billing backed by Maxio Advanced Billing:
`GET /api/subscription-plans`, `POST /api/subscriptions` and `GET /api/my-subscriptions`. See
[docs/subscription-billing.md](../../docs/subscription-billing.md).
