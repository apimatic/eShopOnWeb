# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

Recurring subscriptions run in parallel to the one-time cart/checkout flow. Maxio
Advanced Billing is the billing system of record; the endpoints below use it live.

| Endpoint | Auth | Description |
|---|---|---|
| `GET /api/subscription-plans` | anonymous | Lists the plans (Maxio products) shoppers can subscribe to |
| `POST /api/subscriptions` | JWT bearer | Subscribes the authenticated user to `{ "planHandle": "..." }` |
| `GET /api/my-subscriptions` | JWT bearer | Lists the authenticated user's subscriptions (state, plan, price, next billing date) |

Behavior:

- A Maxio customer is created for each eShopOnWeb user on first use, keyed by the
  user's ASP.NET Identity id as the Maxio customer `reference` — idempotent, so
  repeated or concurrent requests never create duplicate customers or subscriptions
  (Billing API reference uniqueness + duplicate-prevention `uniqueness_token`).
- Subscriptions are enrolled on invoice (`remittance`) billing, matching plans that
  do not require a payment method at signup.
- Configuration is read from the `Maxio:` section: `Maxio:ApiKey`,
  `Maxio:Subdomain`, `Maxio:ProductFamilyHandle` (required) and `Maxio:BaseUrl`
  (optional override used verbatim when set). In development these come from user
  secrets, sourced from the `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN` and
  `MAXIO_DEFAULT_PRODUCT_FAMILY` environment variables. Values are never hard-coded.

Implementation lives in `src/Infrastructure/Maxio` (typed HTTP client + orchestration
service) with the endpoint classes in `src/PublicApi/SubscriptionEndpoints`.