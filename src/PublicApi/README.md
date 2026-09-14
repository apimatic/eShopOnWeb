# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscription billing

The JWT-authenticated subscription endpoints are:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "..." }`
- `GET /api/my-subscriptions`

Configure the integration through the `Maxio` configuration section. `Maxio:ApiKey`,
`Maxio:Subdomain`, and `Maxio:ProductFamilyHandle` are required. `Maxio:BaseUrl` is an
optional absolute base-address override; otherwise the address is derived from the site
subdomain using the server template in `maxio-spec/openapi.yaml`.

For local development, keep credentials outside the repository with .NET user-secrets.
Production deployments can provide the same keys through the normal ASP.NET Core
configuration providers (for example, environment variables with `__` section separators).
