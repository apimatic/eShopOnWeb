# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

The subscription endpoints use Maxio Advanced Billing as the billing system of record:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "planHandle": "..." }`
- `GET /api/my-subscriptions`

All three endpoints require a PublicApi JWT. Configure the `Maxio` section with `ApiKey`,
`Subdomain`, `ProductFamilyHandle`, and the optional `BaseUrl`. An explicit `BaseUrl` is
used as-is; otherwise the client derives the US Advanced Billing address from `Subdomain`.
Keep the values in .NET user-secrets or another external configuration provider.
