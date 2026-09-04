# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

The JWT-authenticated subscription endpoints use Maxio Advanced Billing as the system of record:

- `GET /api/subscription-plans` lists products in the configured product family.
- `POST /api/subscriptions` accepts `{ "planHandle": "..." }` and is safe to retry for the same user and plan.
- `GET /api/my-subscriptions` returns the authenticated user's Maxio subscriptions in that family.

Configure the `Maxio` section with `ApiKey`, `Subdomain`, `ProductFamilyHandle`, and the optional `BaseUrl`. `BaseUrl`, when present, overrides the derived `https://{Subdomain}.chargify.com` address. For local development, populate the first three values from `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, and `MAXIO_DEFAULT_PRODUCT_FAMILY` using .NET user-secrets; no credentials belong in appsettings files.
