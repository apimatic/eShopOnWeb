# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription endpoints

`SubscriptionEndpoints/` adds recurring-subscription billing on top of the existing one-time
Catalog/Basket/Order flow, with Maxio Advanced Billing as the system of record:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` (idempotent)
- `GET /api/my-subscriptions`

All three are JWT-authenticated and act only on the caller's own billing records. Configuration,
idempotency design and the Maxio API surface used are documented in
[docs/subscriptions.md](../../docs/subscriptions.md).
