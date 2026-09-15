# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscriptions

The subscription endpoints use Maxio Advanced Billing as the billing system of record:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "planHandle": "..." }`
- `GET /api/my-subscriptions`

All three require a bearer token issued by `POST /api/authenticate`. Configure the PublicApi user-secrets from the environment-provided `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, and `MAXIO_DEFAULT_PRODUCT_FAMILY` values using the `Maxio:ApiKey`, `Maxio:Subdomain`, and `Maxio:ProductFamilyHandle` keys. `Maxio:BaseUrl` is optional; when absent the client derives the US Maxio URL from the site subdomain according to `maxio-spec/openapi.yaml`.

The identity migration `20260905230000_AddMaxioSubscriptionLinks` stores only local user-to-Maxio linkage and enrollment idempotency records. Plan and subscription state are always read from Maxio.
