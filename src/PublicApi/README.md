# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

`SubscriptionEndpoints/` adds recurring-subscription billing on top of the one-time commerce flow,
with Maxio Advanced Billing as the system of record:

- `GET /api/subscription-plans` — the plans on offer
- `POST /api/subscriptions` — subscribe the caller to a plan (idempotent per shopper and plan)
- `GET /api/my-subscriptions` — the caller's subscriptions

All three require a JWT bearer token from `POST /api/authenticate`. See
[docs/subscription-billing.md](../../docs/subscription-billing.md) for configuration, the flow and
how to verify it.
