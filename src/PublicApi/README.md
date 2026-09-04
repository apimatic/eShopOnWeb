# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscriptions

The JWT-authenticated subscription endpoints are:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "planHandle": "<Maxio product handle>" }`
- `GET /api/my-subscriptions`

Configure the integration through the `Maxio` configuration section. `ApiKey`,
`Subdomain`, and `ProductFamilyHandle` are required; `BaseUrl` is optional and overrides
the derived direct Billing API address. For local development, keep these values in .NET
user-secrets and map the supplied `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, and
`MAXIO_DEFAULT_PRODUCT_FAMILY` environment variables to those settings. No payment
details are accepted by this API; the signup uses Maxio's remittance collection method.
