# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

The JWT-authenticated subscription endpoints are additive to the catalog and checkout API:

- `GET /api/subscription-plans` returns active plans from the configured Maxio product family.
- `POST /api/subscriptions` accepts `{ "planHandle": "..." }` and returns the Maxio-backed subscription.
- `GET /api/my-subscriptions` returns the authenticated user's current Maxio subscriptions.

Maxio settings are read from the `Maxio` configuration section: `ApiKey`, `Subdomain`,
`ProductFamilyHandle`, and optional `BaseUrl`. The normal base address is derived from the
configured site subdomain; `BaseUrl` is used when supplied. For local development, keep
the values in .NET user-secrets or environment variables and never commit them.
