# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription endpoints

`SubscriptionEndpoints/` adds recurring-subscription billing backed by Maxio Advanced Billing:
`GET /api/subscription-plans`, `POST /api/subscriptions` and `GET /api/my-subscriptions`. It runs
alongside - not instead of - the one-time Catalog/Basket/Order flow. See
[docs/subscriptions-maxio.md](../../docs/subscriptions-maxio.md) for configuration and design notes.
