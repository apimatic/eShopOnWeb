# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

The JWT-protected subscription API is available at:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "planHandle": "<Maxio product handle>" }`
- `GET /api/my-subscriptions`

Maxio settings are read from the `Maxio` configuration section. `ApiKey`, `Subdomain`, and
`ProductFamilyHandle` are required. `BaseUrl` is optional; when present it is used as the
API base address. `Environment` selects the `US` or `EU` server from the Maxio OpenAPI
specification when `BaseUrl` is not set.

For local development, keep the values out of the repository and set the user-secrets keys
from the environment variables `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`,
`MAXIO_DEFAULT_PRODUCT_FAMILY`, and `MAXIO_ENVIRONMENT`.
