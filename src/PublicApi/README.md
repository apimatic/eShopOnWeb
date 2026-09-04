# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

PublicApi exposes JWT-authenticated Maxio Advanced Billing endpoints:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "planHandle": "eshop-pro", "idempotencyKey": "optional-client-key" }`
- `GET /api/my-subscriptions`

Maxio settings are bound from the `Maxio` configuration section using exactly
`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional
`Maxio:BaseUrl`. The first three are required; `BaseUrl`, when supplied, is used
verbatim. Product-family and plan handles are used instead of numeric Maxio IDs.

The PublicApi host targets `net10.0` because the pinned Maxio SDK release brings a
.NET 10 framework-adjacent dependency closure. The durable subscription idempotency
record is stored in the identity database; `UseOnlyInMemoryDatabase=true` is suitable
for local verification but loses that record on restart (the provider reference lookup
still reconciles an already-created subscription).
