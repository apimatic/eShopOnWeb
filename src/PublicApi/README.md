# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

`SubscriptionEndpoints/` adds recurring-subscription billing on top of the existing one-time
commerce flow, with Maxio Advanced Billing as the system of record:
`GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`.

See [docs/subscription-billing.md](../../docs/subscription-billing.md) for configuration, the
idempotency design and the Maxio operations used.
