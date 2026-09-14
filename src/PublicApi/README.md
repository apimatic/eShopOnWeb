# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

`SubscriptionEndpoints` adds recurring subscriptions on top of the one-time storefront flow, with
Maxio Advanced Billing as the system of record:

* `GET /api/subscription-plans`
* `POST /api/subscriptions`
* `GET /api/my-subscriptions`

They are JWT-authenticated and take the shopper from the token. Configuration, design notes and a
step-by-step verification guide are in [docs/subscription-billing.md](../../docs/subscription-billing.md).
