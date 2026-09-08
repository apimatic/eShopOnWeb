# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription Endpoints

Recurring-subscription billing is exposed under `/api/` as an additive capability parallel to the one-time cart/checkout flow. Maxio Advanced Billing is the billing system of record.

All subscription endpoints require JWT authentication: obtain a bearer token from `POST /api/authenticate` and send `Authorization: Bearer {token}`.

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/subscription-plans` | Lists subscribable plans (products in the configured Maxio product family). |
| POST | `/api/subscriptions` | Subscribes the authenticated shopper to a plan (`{ "planHandle": "..." }`). Idempotent: repeated requests never create duplicate customers or subscriptions. |
| GET | `/api/my-subscriptions` | Lists the authenticated shopper's subscriptions with state, price, and next billing date. |

### Configuration

Bound from the `Maxio` configuration section (values via user-secrets or environment; never committed):

| Key | Meaning |
|-----|---------|
| `Maxio:ApiKey` | Site API key (Basic auth username; `x` is the password). |
| `Maxio:Subdomain` | Maxio site subdomain (base URL is derived as `https://{subdomain}.chargify.com`). |
| `Maxio:BaseUrl` | Optional. When set, used verbatim as the API base address instead of deriving one from the subdomain. |
| `Maxio:ProductFamilyHandle` | Handle of the product family whose products are offered as plans. |

### How identity maps to Maxio

The eShopOnWeb user id is stored as the Maxio customer `reference`. The mapping lives entirely in the billing system, so it survives local database resets (with the in-memory database provider, user ids are regenerated per run, which produces a fresh mapping each run).

### How idempotency works

- **Customer:** lookup by reference before create; Maxio enforces reference uniqueness and a lost create race falls back to lookup.
- **Subscription:** a per-user lock serializes concurrent subscribes, an existing live subscription to the plan short-circuits creation, and every create carries a Maxio `uniqueness_token` (duplicate prevention within 60 minutes).
- **Cardless signup:** products that don't require a payment method are signed up with `payment_collection_method: remittance` when the site rejects an automatic-collection signup without a payment profile.
