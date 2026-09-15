# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

The JWT-authenticated subscription endpoints are:

* `GET /api/subscription-plans`
* `POST /api/subscriptions` with `{ "planHandle": "<Maxio product handle>" }`
* `GET /api/my-subscriptions`

Maxio settings are read from the `Maxio` configuration section: `ApiKey`, `Subdomain`, `Environment`, `ProductFamilyHandle`, and the optional `BaseUrl`. The sandbox environment variables `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT`, and `MAXIO_DEFAULT_PRODUCT_FAMILY` are mapped to the first four settings. `BaseUrl`, when present, takes precedence over derived hosting URLs.

Customer and subscription references are deterministic and are used with Maxio's reference lookup endpoints to make retries and double-clicks idempotent. The local identity database stores a user-to-subscription correlation index; Maxio remains authoritative for subscription state, plan, price, and next billing date.
