# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscriptions

The JWT-authenticated subscription routes are:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "planHandle": "..." }`
- `GET /api/my-subscriptions`

Maxio settings are bound from `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and the optional `Maxio:BaseUrl`. The first three may be supplied from `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, and `MAXIO_DEFAULT_PRODUCT_FAMILY`; `MAXIO_ENVIRONMENT` selects the US or EU server template when `BaseUrl` is not set.
