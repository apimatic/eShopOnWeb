# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

PublicApi exposes these JWT-authenticated endpoints:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "planHandle": "..." }`
- `GET /api/my-subscriptions`

Maxio settings are read from the `Maxio` configuration section using `ApiKey`, `Subdomain`,
`ProductFamilyHandle`, and optional `BaseUrl`. For local development, set the values with
`.NET user-secrets`. The application maps `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`,
`MAXIO_DEFAULT_PRODUCT_FAMILY`, and optional `MAXIO_BASE_URL` into those keys. `MAXIO_ENVIRONMENT`
selects the documented US/EU host when `BaseUrl` is not set.
