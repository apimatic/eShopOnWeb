# Subscription billing (Maxio Advanced Billing)

eShopOnWeb ships one-time commerce (Catalog → Basket → Order). This document covers the
**additive** recurring-subscription capability that runs alongside it: a shopper browses plans,
subscribes to one, and sees the result on their account.

**Maxio Advanced Billing is the system of record.** eShopOnWeb persists no plan or subscription
state of its own — every read and write goes to Maxio, and the shopper is identified there by a
reference derived from their eShopOnWeb user name. That is deliberate: the app runs on an
in-memory database in development, which would otherwise lose the shopper ↔ subscription mapping
on every restart.

The contract for every Maxio interaction is the OpenAPI specification in [`maxio-spec/`](../maxio-spec).
Paths, query parameters, request/response schemas, the `BasicAuth` security scheme and the
`https://{site}.chargify.com` server template all come from it.

## Endpoints

All three live on **`src/PublicApi`** and require a JWT bearer token. The shopper is taken from the
token (`ClaimTypes.Name`), never from the request body or query string.

| Method | Route | Purpose |
|--------|-------|---------|
| `GET` | `/api/subscription-plans` | Plans on offer, read live from the configured product family. |
| `POST` | `/api/subscriptions` | Subscribe the caller to a plan. `201` when created, `200` when they already held it. |
| `GET` | `/api/my-subscriptions` | Every subscription the caller holds, newest first. |

`POST /api/subscriptions` body:

```json
{
  "planHandle": "eshop-pro",
  "firstName": "Jane",
  "lastName": "Doe",
  "organization": "Acme"
}
```

`planHandle` is the only field that matters; the name fields are used once, when the billing
customer is created, and are otherwise derived from the shopper's email address. `planHandle` may
be omitted if `Maxio:DefaultPlanHandle` is configured.

### Status codes

| Status | Meaning |
|--------|---------|
| `200` | Already subscribed to this plan; the existing subscription is returned. |
| `201` | The shopper was enrolled. |
| `401` | No or invalid bearer token. |
| `404` | The requested plan is not in the configured billing catalog. |
| `422` | Maxio rejected the request; its messages are returned verbatim in `Errors`. |
| `502` | Maxio was unreachable, failed, or rejected this deployment's credentials. |
| `503` | Subscription billing is not configured on this deployment. |

## Configuration

Bound from the `Maxio` configuration section. Nothing here is hard-coded, so the same build runs
against a different site and a different catalog.

| Key | Required | Notes |
|-----|----------|-------|
| `Maxio:ApiKey` | yes | Sent as the HTTP Basic *username* with the literal password `x`, per the spec's `BasicAuth` scheme. |
| `Maxio:Subdomain` | yes, unless `BaseUrl` is set | Fills the `site` variable of the spec's server template `https://{site}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are the plans on offer. Addressed as `handle:<value>`, because handles are stable across catalog re-seeds and numeric ids are not. |
| `Maxio:BaseUrl` | no | Base address override, used **verbatim** when set. Needed for EU-hosted sites (`https://{site}.ebilling.maxio.com`) and useful for pointing tests at a stub. |
| `Maxio:PaymentCollectionMethod` | no | Defaults to `remittance`. See below. |
| `Maxio:DefaultPlanHandle` | no | Plan used when a subscribe request does not name one. |
| `Maxio:PlanCacheSeconds` | no | Plan catalog cache lifetime, default 60. `0` disables caching. |
| `Maxio:TimeoutSeconds` | no | Per-request timeout, default 30. |
| `Maxio:MaxRetryAttempts` | no | Retries for throttled and transient failures, default 3. |

**Secrets never enter the repository.** In development, load them into user-secrets:

```powershell
dotnet user-secrets --project src/PublicApi/PublicApi.csproj set "Maxio:ApiKey"              $env:MAXIO_API_KEY
dotnet user-secrets --project src/PublicApi/PublicApi.csproj set "Maxio:Subdomain"           $env:MAXIO_SITE_SUBDOMAIN
dotnet user-secrets --project src/PublicApi/PublicApi.csproj set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY
```

Elsewhere, supply them through environment variables (`Maxio__ApiKey`, …) or a vault. When the
settings are missing the app still starts — it logs a warning and the three endpoints answer `503`,
because subscriptions are additive and must not take the storefront down with them.

### Why `remittance` is the default collection method

Both demo plans are configured with *payment method not required*, so no card is captured. With
Maxio's default `automatic` collection, signup immediately attempts a charge and fails with
`No payment method was on file for the $299.00 balance` (HTTP 422). `remittance` — invoice billing,
one of the values of the spec's `Collection-Method` schema — creates the subscription in `active`
state with an open balance instead. On a site that captures a payment method first, set
`Maxio:PaymentCollectionMethod` to `automatic`.

## How the hero flow works

`POST /api/subscriptions` runs these steps, all of them idempotent:

1. **Validate the plan.** `GET /product_families/handle:{family}/products.json` (cached). An unknown
   handle is a `404` from eShopOnWeb rather than an opaque rejection from Maxio.
2. **Serialise per shopper.** An in-process lock keyed on the customer reference means a
   double-clicked subscribe button cannot run two enrollments at once. Correctness does not depend
   on it — the following steps are safe on their own.
3. **Ensure the billing customer.** `GET /customers/lookup.json?reference={reference}`; when absent,
   `POST /customers.json` with that reference. A create that loses a race to another instance comes
   back `422` and is resolved by re-reading the customer, never by creating a second one.
4. **Return an existing subscription if there is one.** `GET /customers/{id}/subscriptions.json`,
   filtered to the requested plan and to states that still belong to the shopper. A hit answers
   `200` with `"created": false`.
5. **Enroll.** `POST /subscriptions.json` with `product_handle`, `customer_id`, a reference of
   `eshoponweb:{user}:{plan}` and the configured collection method. If that reference is already
   taken by an earlier, ended subscription, a timestamp suffix is appended — Maxio requires
   references to be unique. A `422` is reconciled by re-reading the customer's subscriptions before
   the rejection is surfaced, which covers a create that another instance had already completed.

References written into Maxio:

- customer: `eshoponweb:{userName}` — e.g. `eshoponweb:demouser@microsoft.com`
- subscription: `eshoponweb:{userName}:{planHandle}`

## Layout

| Path | Contents |
|------|----------|
| `src/ApplicationCore/Interfaces/ISubscriptionBillingService.cs` | The capability, expressed without any Maxio detail. |
| `src/ApplicationCore/Models/Subscriptions/` | `SubscriptionPlan`, `CustomerSubscription`, `SubscribeCommand`, `SubscribeResult`, live-state classification. |
| `src/ApplicationCore/Exceptions/Billing*.cs` | Configuration, validation and gateway failures. |
| `src/Infrastructure/Maxio/` | `MaxioApiClient` (one member per spec operation), spec-shaped models, retry handler, settings, and the service that implements the capability. |
| `src/PublicApi/SubscriptionEndpoints/` | The three endpoints, their request/response contracts and mappings. |
| `tests/UnitTests/Infrastructure/Maxio/` | Client, settings, retry and billing-service behaviour. |
| `tests/PublicApiIntegrationTests/SubscriptionEndpoints/` | The endpoints end to end against an in-memory Maxio stub. |

## Operational notes

- **Retries.** Throttling (`429`) is retried for any request, since a throttled call was never
  processed. Transient server and transport failures are retried for reads only: repeating a
  `POST /subscriptions.json` whose response was lost could enroll the shopper twice, so that case is
  reconciled by looking the subscription up instead. `Retry-After` is honoured; otherwise the delay
  is exponential with jitter.
- **Logging.** Every Maxio call is logged with method, path and status. The API key is never logged,
  and never appears in an exception message.
- **Caching.** The plan catalog and the site currency are cached in memory (60 seconds and 1 hour).
  A failure to read the site currency degrades to an unset currency rather than failing the catalog.
