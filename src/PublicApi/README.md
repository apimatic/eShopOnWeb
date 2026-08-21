# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

The JWT-authenticated subscription API is additive to the existing catalog, basket, and
order endpoints:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "<handle>" }`
- `GET /api/my-subscriptions`

Maxio Advanced Billing is the billing system of record. Configure the integration through
the `Maxio` configuration section with `ApiKey`, `Subdomain`, `ProductFamilyHandle`, and
the optional `BaseUrl` override. For local development, keep credentials out of settings
files and use the PublicApi project's .NET user-secret store. Run the host with
`UseOnlyInMemoryDatabase=true` when SQL Server LocalDB is unavailable.
