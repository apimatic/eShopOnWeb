# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.



## Recurring subscriptions (Maxio Advanced Billing)

`SubscriptionEndpoints/` adds a recurring-billing capability alongside the one-time catalog flow:
`GET /api/subscription-plans`, `POST /api/subscriptions` and `GET /api/my-subscriptions`, all
JWT-authenticated. Maxio is the system of record; the client is built against the OpenAPI specification in
`maxio-spec/`.

See [docs/maxio-subscriptions.md](../../docs/maxio-subscriptions.md) for configuration, status codes and
the idempotency design.
