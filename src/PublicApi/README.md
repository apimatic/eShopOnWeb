# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscriptions

The JWT-protected subscription API is additive to the catalog/basket/order workflow:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "planHandle": "..." }`
- `GET /api/my-subscriptions`

Maxio is the billing system of record. Configure it outside the repository in the `Maxio` section:
`ApiKey`, `Subdomain`, `ProductFamilyHandle`, and optionally `BaseUrl`. If `BaseUrl` is omitted,
the API root is derived as `https://{Subdomain}.chargify.com`; an explicit `BaseUrl` is used unchanged.

For local development, set the first three values with `dotnet user-secrets` and run with
`UseOnlyInMemoryDatabase=true`. The checked-in identity migration persists the user-to-Maxio
customer and subscription links for SQL Server deployments.
