---
name: project-maxio-subscription-integration
description: eShopOnWeb PublicApi now has Maxio Advanced Billing subscription endpoints; key gotcha on payment-method-optional plans
metadata:
  type: project
---

Added recurring-subscription billing to eShopOnWeb via Maxio Advanced Billing (formerly
Chargify), as an additive capability parallel to the existing Basket/Order checkout flow.
Implemented 2026-09-05.

**What was built** (all on `src/PublicApi`, JWT-authenticated):
- `GET /api/subscription-plans` — lists plans from the configured Maxio product family.
- `POST /api/subscriptions` — idempotently ensures a Maxio customer (keyed on the caller's
  identity/email as the Maxio customer `reference`) and enrolls them in a plan.
- `GET /api/my-subscriptions` — lists the caller's own Maxio subscriptions.
- Maxio is the sole system of record: no subscription state is persisted in eShopOnWeb's own
  database. `IMaxioBillingService` (ApplicationCore interface) / `MaxioBillingService`
  (Infrastructure/Maxio) is the HTTP client, using Basic Auth (`apikey:x`) against
  `https://{subdomain}.chargify.com`.
- Idempotency: Maxio has no server-side idempotency-key mechanism for customer or
  subscription creation. Guarded with (a) customer lookup-by-reference before create, with a
  422-then-relookup fallback for creation races, and (b) a per-buyer in-process
  `KeyedAsyncLock` around "check existing subscription, then create" — sufficient for a
  single-instance deployment, not for multi-instance.

**Non-obvious gotcha (confirmed by live testing against sandbox site `cp-exp-4`):** a Maxio
product configured with `require_credit_card: false` still causes subscription creation to
fail with a 422 ("No payment method was on file for the $X.XX balance") on a non-zero-price
plan, *unless* the subscription-create request also sets
`payment_collection_method: "invoice"`. `require_credit_card: false` only means the API won't
reject the request for missing card data — Maxio's default `automatic` collection still tries
to charge a card on file at signup and fails with none present. This was discovered only by
testing directly against the real sandbox (docs don't spell this interaction out) and is now
hard-coded as the default in `CreateSubscriptionRequestWire.PaymentCollectionMethod`.

**Config**: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`
(optional override), sourced from user-secrets (UserSecretsId
`c247bed6-c0db-4f91-8a70-3291d77e4797` on `src/PublicApi`) — never committed to the repo.

**How to apply**: if asked to extend or debug this integration, start from
`src/Infrastructure/Maxio/MaxioBillingService.cs` and its unit tests in
`tests/UnitTests/Infrastructure/Maxio/MaxioBillingServiceTests.cs` (uses a fake
`HttpMessageHandler`, no live network). Endpoint-level tests (auth + wiring, fake
`IMaxioBillingService`) are in `tests/PublicApiIntegrationTests/SubscriptionEndpoints/`.
