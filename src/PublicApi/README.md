# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

Recurring subscriptions, billed by Maxio Advanced Billing, live in
[SubscriptionEndpoints](SubscriptionEndpoints/README.md): `GET /api/subscription-plans`,
`POST /api/subscriptions` and `GET /api/my-subscriptions`. They run alongside the existing
catalog/basket/order flow and need the `Maxio` configuration section to be populated.
