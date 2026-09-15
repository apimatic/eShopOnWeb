# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

The JWT-authenticated subscription endpoints are:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "..." }`
- `GET /api/my-subscriptions`

Maxio Advanced Billing is the source of truth. Configure the `Maxio` section with `ApiKey`,
`Subdomain`, and `ProductFamilyHandle`; `BaseUrl` is optional and, when supplied, overrides
the derived `https://{Subdomain}.chargify.com/` address. For local setup, keep these values in
user-secrets. The supported environment variable names are `MAXIO_API_KEY`,
`MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT`, and `MAXIO_DEFAULT_PRODUCT_FAMILY`.
