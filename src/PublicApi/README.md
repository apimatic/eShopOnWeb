# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription endpoints

`SubscriptionEndpoints/` adds recurring-subscription billing on top of the one-time
Catalog/Basket/Order flow, with Maxio Advanced Billing as the system of record:

| Route | Purpose |
|---|---|
| `GET /api/subscription-plans` | The plans available to subscribe to |
| `POST /api/subscriptions` | Subscribe the authenticated caller to a plan (idempotent) |
| `GET /api/my-subscriptions` | The authenticated caller subscriptions |

All three are JWT-authenticated and take the shopper from the token, never from the request body.
They follow the `Ardalis.ApiEndpoints` convention already used by `AuthEndpoints`.

Design notes: [docs/maxio-subscriptions.md](../../docs/maxio-subscriptions.md).
Step-by-step verification: [docs/verify-maxio-subscriptions.md](../../docs/verify-maxio-subscriptions.md).
