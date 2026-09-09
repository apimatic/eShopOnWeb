# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription endpoints (Maxio Advanced Billing)

Recurring-subscription billing is an additive capability parallel to the one-time
cart/checkout flow. Maxio Advanced Billing is the billing system of record; these
JWT-authenticated endpoints (bearer token from `POST /api/authenticate`) expose it:

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/subscription-plans` | Lists plans (Maxio products) in the configured product family |
| POST | `/api/subscriptions` | Subscribes the authenticated user to a plan (`{"planHandle": "..."}`, optional — defaults to the first active plan). Idempotent: returns the existing subscription instead of creating a duplicate |
| GET | `/api/my-subscriptions` | Lists the authenticated user's subscriptions with live state from Maxio |

Configuration is bound from the `Maxio` section — `Maxio:ApiKey`, `Maxio:Subdomain`
(or `Maxio:BaseUrl` to override the derived `https://{subdomain}.chargify.com`), and
`Maxio:ProductFamilyHandle`. Load them via .NET user-secrets or environment
variables (`MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_DEFAULT_PRODUCT_FAMILY`);
never commit their values.

Enrollments use `payment_collection_method: remittance` (no card capture at signup).
The local `SubscriptionsDbContext` only caches the user-to-subscription mapping;
with `UseOnlyInMemoryDatabase=true` that mapping is lost on restart, and the
integration re-adopts existing Maxio subscriptions on the next subscribe call.


