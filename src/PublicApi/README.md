# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

PublicApi exposes JWT-authenticated Maxio Advanced Billing endpoints:

- `GET /api/subscription-plans` lists active products in the configured family.
- `POST /api/subscriptions` accepts `{ "planHandle": "..." }` and is idempotent for the same shopper and plan.
- `GET /api/my-subscriptions` reads the shopper's current Maxio subscriptions.

Configure the `Maxio` section with `ApiKey`, `Subdomain`, and `ProductFamilyHandle`. `BaseUrl` is optional; when present it overrides the derived `https://{Subdomain}.chargify.com/` address. Keep the API key in user-secrets or the deployment secret store. Maxio test/sandbox sites use their normal site hostname; `MAXIO_ENVIRONMENT` identifies the supplied environment during local setup.
