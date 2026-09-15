# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio Advanced Billing subscriptions

The shopper-facing, JWT-authenticated endpoints are:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "..." }`
- `GET /api/my-subscriptions`

Maxio is the billing system of record. The integration resolves the configured Product Family by its handle, uses an eShopOnWeb Identity user ID as the unique Maxio customer reference, and reads a customer's subscriptions directly from Maxio.

Configure these user-secrets (never appsettings or source): `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optionally `Maxio:BaseUrl`. With no override, the API base address is derived as `https://{subdomain}.chargify.com/`. The service authenticates to Maxio using HTTPS Basic authentication with the API key and the documented `X` password.

The subscribe operation is retry-safe: it checks the customer's existing live subscription for the requested plan, serializes concurrent local requests, and sends Maxio's `uniqueness_token` POST parameter. A conflict is re-read from Maxio rather than creating another subscription.
