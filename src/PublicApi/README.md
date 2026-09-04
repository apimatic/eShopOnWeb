# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

The JWT-authenticated subscription endpoints are additive to the existing commerce API:

* `GET /api/subscription-plans` lists the active products in the configured Maxio product family.
* `POST /api/subscriptions` enrolls the caller in the selected plan. Send `{ "productHandle": "..." }`, or omit the body to use the default Pro-named plan when one is available.
* `GET /api/my-subscriptions` returns the caller's Maxio subscription state and next billing date.

PublicApi binds the following keys from the `Maxio` configuration section: `ApiKey`, `Subdomain`, `ProductFamilyHandle`, and optional `BaseUrl`. When `BaseUrl` is empty, the client derives the documented Billing API host from `Subdomain`; when set, it is used as the API base address. For local development, keep these values in user-secrets. The provided environment variable names are `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT`, and `MAXIO_DEFAULT_PRODUCT_FAMILY`; the environment selector is used by the development setup, while the four bound configuration keys remain the API key, subdomain, product family handle, and optional base URL.
