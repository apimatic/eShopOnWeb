---
name: project-maxio-subscription-integration
description: eShopOnWeb PublicApi now has a Maxio Advanced Billing subscription-billing integration, additive to the existing one-time-commerce flow
metadata:
  type: project
---

Added recurring-subscription billing to eShopOnWeb via Maxio Advanced Billing (formerly
Chargify), as an additive capability parallel to the existing Catalog/Basket/Order flow — not
a replacement.

**Endpoints** (JWT-authenticated, `src/PublicApi/SubscriptionEndpoints/`):
- `GET /api/subscription-plans` — lists active plans in the configured product family.
- `POST /api/subscriptions` — idempotent subscribe: ensures a Maxio customer exists for the
  caller (keyed on `ApplicationUser.Id` as the Maxio customer `reference`), then enrolls them.
  A double-click reuses the existing customer/live-subscription rather than duplicating.
- `GET /api/my-subscriptions` — lists the caller's Maxio subscriptions.

**Architecture**: `IMaxioSubscriptionService` in `ApplicationCore/Maxio/`, implemented in
`Infrastructure/Maxio/` (`MaxioApiClient` wraps the raw HTTP calls behind `IMaxioApiClient` for
testability; `MaxioSubscriptionService` holds the idempotency/business logic). No local DB
persistence of the customer/subscription mapping — Maxio itself is the system of record,
looked up live via the customer `reference` field, which avoids relying on the sandbox's
in-memory-only EF Core provider (data doesn't survive restarts here).

**Config**: bound from `Maxio:ApiKey` / `Maxio:Subdomain` / `Maxio:ProductFamilyHandle` /
`Maxio:BaseUrl` (optional override). Values live only in .NET user-secrets for
`src/PublicApi` (UserSecretsId `19de4987-eed2-4b80-9c19-10a12ffe3d38`), sourced from env vars
`MAXIO_API_KEY` / `MAXIO_SITE_SUBDOMAIN` / `MAXIO_DEFAULT_PRODUCT_FAMILY` — never committed.

**Sandbox** (site `cp-exp-3`): product family handle `eshop-subscribe`, plans `eshop-pro`
($299/mo) and `basic-plan` ($29/mo). Numeric Maxio IDs are unstable across re-seeds; only
handles are relied on in code.

**Why**: [[feedback-maxio-remittance-payment-collection]] — a key non-obvious fix was required
to make cardless signup actually work against the real sandbox.

**How to apply**: when extending this integration (e.g. cancel/upgrade endpoints), keep
building strictly against `maxio-spec/openapi.yaml` as the contract, and keep the
`IMaxioApiClient` / `IMaxioSubscriptionService` split so business logic stays unit-testable
without live network calls.
