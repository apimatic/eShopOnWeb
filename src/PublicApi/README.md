# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

The JWT-protected subscription endpoints are:

- `GET /api/subscription-plans` — lists active recurring products in `Maxio:ProductFamilyHandle`.
- `POST /api/subscriptions` — accepts `{ "planHandle": "..." }`, creates or reuses the Maxio customer and subscription, and returns the plan, price, state, and next billing date.
- `GET /api/my-subscriptions` — returns the authenticated user’s Maxio subscriptions in the configured product family.

Configure `Maxio:ApiKey`, `Maxio:Subdomain`, and `Maxio:ProductFamilyHandle` with user-secrets or environment configuration. `Maxio:BaseUrl` is optional; when present it is used as the API base URL. The client follows the local `maxio-spec` contract and authenticates with the API key as the Basic Auth username and `x` as the password.
