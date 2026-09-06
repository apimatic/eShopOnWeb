# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing endpoints

`SubscriptionEndpoints/` adds recurring-subscription billing on top of the existing one-time
Catalog → Basket → Order flow. The two are independent: subscribing changes nothing about the
cart, and eShopOnWeb stores no subscription state of its own. **Maxio Advanced Billing is the
system of record** for plans, billing customers and enrollments; every read goes back to it.

See [`docs/subscription-billing.md`](../../docs/subscription-billing.md) for the design, the
configuration keys, and a step-by-step verification walkthrough.

| Route | Auth | Purpose |
|-------|------|---------|
| `GET /api/subscription-plans` | Bearer | Plans on offer in the configured product family |
| `POST /api/subscriptions` | Bearer | Subscribe the caller to a plan (idempotent) |
| `GET /api/my-subscriptions` | Bearer | The caller's own subscriptions |

All three take the shopper's identity from the JWT alone — no caller can act on another's behalf.
