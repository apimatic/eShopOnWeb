# Subscription billing (Maxio Advanced Billing)

eShopOnWeb sells one-time orders through Catalog → Basket → Order. This capability adds
**recurring subscriptions** alongside that flow. It does not touch it: no shared entities, no
shared endpoints, no changes to checkout.

**Maxio Advanced Billing is the system of record.** eShopOnWeb stores no plan, no billing
customer and no subscription of its own — not even a mapping table. Everything is resolved
through references derived from the signed-in user, which is what keeps the integration correct
across restarts and across instances (and what lets it run on the in-memory database, where
Identity keys are regenerated on every boot).

## Endpoints

All three live on `src/PublicApi`, follow that project's `IEndpoint` convention, and require a
JWT bearer token from `POST /api/authenticate`. **The subscriber is always taken from the token** —
no request carries a user or customer identifier.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/subscription-plans` | Plans the shopper can subscribe to |
| `POST` | `/api/subscriptions` | Subscribe the caller to a plan |
| `GET` | `/api/my-subscriptions` | The caller's own subscriptions |

### `POST /api/subscriptions`

```jsonc
// request
{ "planHandle": "eshop-pro" }
```

```jsonc
// 201 Created - a new subscription
{
  "created": true,
  "subscription": {
    "id": 94209321,
    "reference": "eshoponweb-demouser@microsoft.com:eshop-pro",
    "state": "active",
    "isLive": true,
    "planHandle": "eshop-pro",
    "planName": "Pro Plan",
    "price": 299,
    "priceInCents": 29900,
    "currency": "USD",
    "interval": 1,
    "intervalUnit": "month",
    "balanceInCents": 29900,
    "currentPeriodStartedAt": "2026-09-06T14:24:51+05:00",
    "currentPeriodEndsAt": "2026-10-06T14:24:51+05:00",
    "nextBillingAt": "2026-10-06T14:24:51+05:00",
    "activatedAt": "2026-09-06T14:24:52+05:00",
    "canceledAt": null,
    "customerId": 98837876,
    "customerReference": "eshoponweb-demouser@microsoft.com"
  }
}
```

Repeating the call returns **`200 OK` with `"created": false`** and the same subscription.

`planHandle` may be omitted only when `Maxio:DefaultPlanHandle` is configured; otherwise the call
is rejected with `400`, because picking a plan on the shopper's behalf would commit them to a
recurring charge they never chose.

### Status codes

| Status | When |
| --- | --- |
| `201` | A new subscription was created |
| `200` | The caller already had a live subscription to that plan, or a read succeeded |
| `400` | No plan requested and no default configured |
| `401` | Missing or invalid bearer token |
| `404` | The requested plan handle is not in the configured product family |
| `422` | The plan requires a stored payment method, which this API does not capture |
| `502` | Maxio rejected the call or could not be reached |
| `503` | The `Maxio` configuration section is missing or invalid |

## Configuration

Bound from the `Maxio` section (`MaxioSettings`, validated by `MaxioSettingsValidator`).

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | **Secret.** HTTP Basic user name; the password is the literal `x`. |
| `Maxio:Subdomain` | yes, unless `BaseUrl` is set | Site subdomain, e.g. `acme`. |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products become plans. |
| `Maxio:BaseUrl` | no | Used **verbatim** as the API base address when set; otherwise derived as `https://{Subdomain}.chargify.com`. EU-hosted sites use `https://{Subdomain}.ebilling.maxio.com`. |
| `Maxio:DefaultPlanHandle` | no | Plan used when a subscribe request names none. Unset by default. |
| `Maxio:PaymentCollectionMethod` | no | Default `remittance`. See below. |
| `Maxio:CustomerReferencePrefix` | no | Default `eshoponweb`. Change it when two applications share one Maxio site. |
| `Maxio:CatalogCacheSeconds` | no | Default `60`. `0` disables catalog caching. |
| `Maxio:TimeoutSeconds` | no | Default `30`. |
| `Maxio:MaxRetryAttempts` | no | Default `3`. |

Nothing here is hard-coded: the same build runs against a different Maxio site and a different
catalog by changing configuration alone.

### Secrets

`Maxio:ApiKey` never belongs in a file in this repository. Load it from the environment:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

Outside Development, where user-secrets are not loaded, use environment variables
(`Maxio__ApiKey`, `Maxio__Subdomain`, …) or your platform's secret store.

### Why `remittance`

`payment_collection_method` defaults to `automatic` in Maxio, which makes Maxio attempt to collect
the first invoice from a stored payment profile at signup. eShopOnWeb captures no card details, so
that fails with *"No payment method was on file for the $299.00 balance"* even on a plan whose
`require_credit_card` is false. `remittance` invoices the customer instead, which is the correct
choice for a signup flow without card capture on a Relationship Invoicing site.

Plans whose `require_credit_card` is true are rejected up front with `422` rather than being sent
to Maxio to fail.

## Idempotency

> A double-click must never create two customers or two subscriptions.

Maxio's current Advanced Billing API exposes no general idempotency-key header. Idempotency is
therefore built on two constraints Maxio *does* enforce — **customer references and subscription
references are unique per site** — plus deterministic derivation of both from the signed-in user.

`MaxioReferences` derives:

- customer reference: `eshoponweb-{userName, lower-cased}`
- subscription reference: `{customerReference}:{planHandle}`

`MaxioSubscriptionService.SubscribeAsync` then:

1. takes a per-subscriber in-process lock, so one shopper's concurrent requests are serialised;
2. finds-or-creates the Maxio customer; if Maxio answers `422 Reference: must be unique` because
   another instance won the race, it re-reads and continues;
3. lists the customer's subscriptions and returns any **live** one for the requested plan
   (`created: false`) instead of creating a second;
4. otherwise creates the subscription with the stable reference. If Maxio rejects that reference as
   taken, it looks up the owner: a live subscription belonging to this customer is returned as-is
   (another instance won), while a canceled or expired one means the shopper is genuinely
   resubscribing, so a timestamped reference is minted and the create retried.

Only the lock in step 1 is in-process; steps 2-4 hold across instances and restarts. States
`canceled`, `expired`, `failed_to_create` and `trial_ended` count as terminal; everything else,
including `past_due` and `on_hold`, counts as live and is not duplicated.

## Reliability

`MaxioRetryHandler` retries with exponential back-off and jitter, honouring `Retry-After`:

- **429** is retried for any method — Maxio rejected the request outright, so a replay cannot
  double-charge.
- **5xx and transport faults** are retried for `GET`/`HEAD` only. A `POST` that failed after Maxio
  began processing it could otherwise create a second customer or subscription.

Maxio's error bodies are parsed by `MaxioErrorReader`, which handles the array-of-strings,
per-field-object and bare-string shapes and falls back to the raw body.

Configuration is validated lazily rather than at start-up, so a host with no billing configured
still boots and the rest of the API keeps working; only the subscription endpoints report `503`.

## Layout

```
src/ApplicationCore/Subscriptions/     ISubscriptionService and its models (no Maxio types)
src/ApplicationCore/Exceptions/        Billing*.cs, SubscriptionPlan*.cs, PaymentMethodRequired*.cs
src/Infrastructure/Maxio/              The Maxio adapter
  MaxioSettings.cs / MaxioSettingsValidator.cs
  MaxioApiClient.cs                    One method per Maxio endpoint, no orchestration
  MaxioSubscriptionService.cs          Find-or-create, idempotency, mapping
  MaxioReferences.cs                   Reference derivation
  MaxioRetryHandler.cs                 Transient-failure policy
  KeyedAsyncLock.cs                    Per-subscriber serialisation
  Models/MaxioWireModels.cs            Wire DTOs
src/PublicApi/SubscriptionEndpoints/   The three endpoints, DTOs and the subscriber resolver
```

## Maxio contract

Every endpoint, field and shape used here was confirmed against Maxio's own published contract and
then exercised against a live sandbox site before being relied on.

| Operation | Maxio call |
| --- | --- |
| Site currency | `GET /site.json` |
| List plans | `GET /product_families/handle:{handle}/products.json` |
| Find customer | `GET /customers/lookup.json?reference=` (404 when absent) |
| Create customer | `POST /customers.json` |
| List a customer's subscriptions | `GET /customers/{customer_id}/subscriptions.json` |
| Create subscription | `POST /subscriptions.json` |
| Find subscription by reference | `GET /subscriptions/lookup.json?reference=` (404 when absent) |

Sources:

- Official OpenAPI (Swagger 2.0) export of the Advanced Billing API:
  <https://developers.maxio.com/static/exports/maxio-advanced-billing-swagger20.json>
- Maxio Advanced Billing developer portal: <https://developers.maxio.com/>
- Official Maxio .NET SDK, used as a cross-check on routes and models:
  <https://github.com/maxio-com/ab-dotnet-sdk>

Two behaviours worth recording, both confirmed against a sandbox site rather than inferred:

- **Address the product family by handle, not id.** `product_family_id` accepts `handle:{handle}`;
  numeric ids are reassigned when a catalog is re-seeded.
- **Attach subscriptions with `customer_id`, not `customer_reference`.** Maxio's
  `customer_reference` lookup on `POST /subscriptions.json` does not resolve references containing
  a `+`, failing with *"A Customer must be specified for the subscription to be valid."*
  eShopOnWeb derives customer references from email addresses, which routinely contain `+`, so the
  numeric id from the find-or-create step is used instead.

## Not in scope

The sandbox product family also carries a metered `api-call` component. Reporting usage against it
is a separate capability (Maxio's usage/allocation endpoints) and is not implemented here; the
hero flow is subscribing to a plan.
