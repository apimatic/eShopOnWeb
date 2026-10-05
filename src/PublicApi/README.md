# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription endpoints (Maxio Advanced Billing)

Recurring-subscription billing is provided by Maxio Advanced Billing (the billing system
of record) and exposed on PublicApi as a parallel capability to the one-time
cart/checkout flow. All endpoints are JWT-authenticated (`/api/authenticate` first,
then `Authorization: Bearer <token>`).

| Endpoint | Description |
|---|---|
| `GET /api/subscription-plans` | Lists subscribable plans (products of the configured Maxio product family). |
| `POST /api/subscriptions` | Subscribes the authenticated user to a plan. Body: `{ "productHandle": "eshop-pro" }`. Idempotent: a double-click / repeated call returns the existing subscription (`wasExisting: true`). |
| `GET /api/my-subscriptions` | Lists the authenticated user's subscriptions with plan, price, state and next billing date. |

Configuration is bound from the `Maxio:` section - `Maxio:ApiKey`,
`Maxio:Subdomain`, `Maxio:ProductFamilyHandle` and optional `Maxio:BaseUrl`
override. Values come from user-secrets / environment (never committed):
set them with `dotnet user-secrets set "Maxio:ApiKey" <key> --project src/PublicApi`,
`... "Maxio:Subdomain" ...`, `... "Maxio:ProductFamilyHandle" ...`.

Design notes:

- A Maxio customer is keyed to the eShopOnWeb user id via the Maxio customer
  `reference` (`eshoponweb-user:<userId>`), so no local userId->customer mapping is
  required and customer creation is idempotent across restarts.
- Subscriptions carry a deterministic Maxio `reference` (`eshoponweb-sub:<userId>:<productHandle>`)
  plus a deterministic `uniqueness_token`, making signup idempotent even under
  concurrent duplicate requests (Maxio duplicate-prevention is relied on for recovery).
- Subscriptions are created with `payment_collection_method: remittance` so they can be
  activated without a stored payment method (matches the demo catalog configuration).
