# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscription billing

The JWT-authenticated subscription API is additive to the existing catalog and checkout APIs:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "<handle>" }`
- `GET /api/my-subscriptions`

Configuration is bound from `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and the optional `Maxio:BaseUrl` override. Keep the API key in user-secrets or another external configuration provider. When `Maxio:BaseUrl` is empty, the API address follows the bundled OpenAPI server template: `https://{subdomain}.chargify.com`.

The integration uses stable customer and subscription references derived from the authenticated eShop Identity user ID. A unique local enrollment record coordinates concurrent requests, while Maxio remains the source of truth for plan and subscription state.
