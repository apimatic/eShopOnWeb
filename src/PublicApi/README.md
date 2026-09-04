# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

The JWT-protected subscription endpoints are:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "..." }`
- `GET /api/my-subscriptions`

Maxio Advanced Billing is the system of record. Configure the PublicApi user-secrets store with:

- `Maxio:ApiKey`
- `Maxio:Subdomain`
- `Maxio:ProductFamilyHandle`
- `Maxio:BaseUrl` (optional; when present it overrides the derived `https://{subdomain}.chargify.com` base URL)

The application resolves plans from the configured product-family handle, so no Maxio numeric catalog IDs are required.
