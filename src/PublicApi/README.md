# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscriptions

The subscription flow is additive to catalog, basket, and order checkout. It is exposed only by the JWT-authenticated PublicApi:

- `GET /api/subscription-plans` lists active plans in the configured Maxio product family.
- `POST /api/subscriptions` with `{ "planHandle": "..." }` enrolls the caller.
- `GET /api/my-subscriptions` returns the caller's Maxio-managed subscriptions.

Configure these keys in the `Maxio` section, using user-secrets locally and a secret store in deployment: `ApiKey`, `Subdomain`, `ProductFamilyHandle`, and optional `BaseUrl`. When `BaseUrl` is absent the API base address is derived as `https://{Subdomain}.chargify.com`; an explicit `BaseUrl` is used verbatim. The API key must never be placed in appsettings files.

The adapter uses the eShop identity user ID as a unique Maxio customer reference. Maxio customer lookup plus that unique reference prevents duplicate customers. Enrollments are serialized per shopper, and Maxio's verified request-level `uniqueness_token` is deterministic per shopper and plan, preventing duplicate subscriptions across concurrent requests. A repeat enrollment returns the existing subscription with HTTP 200.
