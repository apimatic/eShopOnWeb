# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Recurring subscription billing (Maxio Advanced Billing)

This is an additive, parallel capability to the one-time Catalog → Basket → Order flow. It lets an
authenticated shopper browse plans, subscribe, and review their subscriptions, with **Maxio Advanced
Billing** as the system of record. A Maxio customer is created/looked up per eShopOnWeb user using the
user's username as the Maxio customer `reference`, which makes customer and subscription creation
idempotent.

Endpoints (JWT bearer required):

| Method | Route                      | Description                                                     |
|--------|----------------------------|-----------------------------------------------------------------|
| GET    | `/api/subscription-plans`  | Lists the plans (products) in the configured Maxio product family |
| POST   | `/api/subscriptions`       | Subscribes the caller to a plan (`{ "planHandle": "eshop-pro" }`) |
| GET    | `/api/my-subscriptions`    | Lists the caller's subscriptions in the configured product family |

Implementation notes:

- All Maxio interactions are implemented against the OpenAPI specification in `maxio-spec/` (see
  `src/PublicApi/Maxio/`).
- Configuration is bound from the `Maxio` section. Values are read from the `MAXIO_API_KEY`,
  `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT` and `MAXIO_DEFAULT_PRODUCT_FAMILY` environment
  variables (or .NET user-secrets) and never stored in this repository. `Maxio:BaseUrl` may be set
  to override the base address derived from the subdomain.


