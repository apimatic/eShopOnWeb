# Subscription billing (Maxio Advanced Billing)

eShopOnWeb's original flow is one-time commerce: Catalog → Basket → Order. This document
describes the **parallel** recurring-subscription capability that sits beside it. Nothing in the
cart or checkout path changes; a shopper can use either, or both.

**Maxio Advanced Billing is the system of record.** eShopOnWeb persists no plans, no billing
customers and no subscriptions. Every request reads live from Maxio, so the answer the shopper
sees is the answer Maxio would give. That also means the capability works unchanged when the app
runs on the in-memory database, whose rows do not survive a restart.

## Endpoints

All three live on `src/PublicApi` and require a JWT bearer token from `POST /api/authenticate`.
The caller's identity comes from the token; a request body can never name a different subscriber.

| Method | Route | Purpose |
| ------ | ----- | ------- |
| `GET`  | `/api/subscription-plans` | Plans on offer: the non-archived products of the configured product family. |
| `POST` | `/api/subscriptions`      | Subscribe the caller to a plan. Idempotent. |
| `GET`  | `/api/my-subscriptions`   | The caller's subscriptions, newest first. |

### `POST /api/subscriptions`

```json
{ "planHandle": "eshop-pro", "idempotencyKey": "optional-caller-supplied-key" }
```

| Status | Meaning |
| ------ | ------- |
| `201 Created` | A new subscription was created. |
| `200 OK` | The caller already had a live subscription to that plan; it is returned unchanged, with `alreadySubscribed: true`. |
| `400 Bad Request` | No `planHandle`, or Maxio rejected the request (for example a plan that needs a card). |
| `401 Unauthorized` | No or invalid bearer token. |
| `404 Not Found` | The handle is not a plan in the configured product family. |
| `502 / 503` | Maxio is misconfigured, unreachable or throttling. |

The response carries the plan, price, state and next billing date:

```json
{
  "subscription": {
    "id": 94208329,
    "state": "active",
    "isLive": true,
    "planHandle": "eshop-pro",
    "planName": "Pro Plan",
    "price": 299.00,
    "priceInCents": 29900,
    "currency": "USD",
    "displayPrice": "USD 299.00 / month",
    "nextBillingAt": "2026-10-06T10:16:05+05:00",
    "customerId": 98837189,
    "customerReference": "eshoponweb-demouser@microsoft.com"
  },
  "alreadySubscribed": false
}
```

## Configuration

Bound from the `Maxio` configuration section. **No value belongs in a file in this repository** —
use user-secrets locally and a secret store in a real deployment.

| Key | Required | Notes |
| --- | -------- | ----- |
| `Maxio:ApiKey` | yes | Basic-auth user name; the password is the literal `X`. |
| `Maxio:Subdomain` | yes* | Site subdomain. Resolves to `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | Used **verbatim** as the API base address when set, instead of deriving one from the subdomain. Needed for EU-hosted sites (`https://{site}.ebilling.maxio.com`) or to point at a recording proxy. |
| `Maxio:PaymentCollectionMethod` | no | Defaults to an invoice-based method chosen from the site's billing architecture. |
| `Maxio:TimeoutSeconds` | no | Default 30. Maxio cuts requests off at 120s. |
| `Maxio:MaxRetryAttempts` | no | Default 3, for 429 / 5xx / network failures. |

\* Either `Maxio:Subdomain` or `Maxio:BaseUrl` must be set.

Loading the sandbox credentials into user-secrets:

```bash
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"              --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"       --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

As a convenience for a fresh clone, any setting configuration does not already supply is filled in
from `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_DEFAULT_PRODUCT_FAMILY` and `MAXIO_BASE_URL`.
Configuration always wins, so user-secrets override the environment rather than the other way
round.

Missing configuration does not stop the host from booting — only the three subscription endpoints
fail, with `503` and a message naming the setting that is missing.

## How the hero flow works

`POST /api/subscriptions` runs these steps:

1. **Validate the plan.** The handle must be a live product of `Maxio:ProductFamilyHandle`.
   Anything else is a `404`, and no arbitrary product on the site can be subscribed to.
2. **Serialize per subscriber.** An in-process keyed lock turns a double-clicked button into two
   sequential requests, so the second one sees what the first one created.
3. **Ensure the billing customer.** `GET /customers/lookup.json?reference=…` first; only create on
   a miss. If a concurrent writer wins the race, Maxio's uniqueness rule on `reference` returns
   `422` and the existing customer is re-read and reused.
4. **Check for an existing subscription.** If the caller already has a non-terminal subscription to
   that plan, it is returned with `200 OK` — no second subscription, no second charge.
5. **Create the subscription**, sending a `uniqueness_token` derived from the subscriber and plan.
   Maxio rejects a repeat within 60 minutes with `409`; on that the customer's subscriptions are
   re-read and the twin's result returned. If the `409` resolves to nothing — re-subscribing after
   a cancellation inside the same window — the create is retried once with a fresh token.

`uniqueness_token` is sent as a **sibling** of `subscription`, not a member of it. Nested, Maxio
ignores it and duplicate prevention silently does nothing.

### Why subscriptions are created invoice-billed

The seeded plans do not require a payment method, but the site's default collection method is
`automatic`, which tries to collect the first period's charge at signup and fails with
*"No payment method was on file"*. Creating the subscription with an invoice-based collection
method (`remittance` on Relationship Invoicing sites, `invoice` otherwise) bills by invoice
instead, so a shopper can subscribe with no card capture and no 3-DS step. The method is read from
`GET /site.json` — cached for the process — and can be overridden with
`Maxio:PaymentCollectionMethod`.

### Linking an eShopOnWeb user to a Maxio customer

The link is the Maxio customer `reference`, set to `eshoponweb-{username}` (lower-cased). It is
derived, not stored, which is what makes the mapping survive a restart on the in-memory database
and keeps the integration free of a local table and a migration. Maxio enforces that a reference is
unique per site, so it doubles as the concurrency guard on customer creation.

## Layout

| Path | Contents |
| ---- | -------- |
| `src/ApplicationCore/Interfaces/ISubscriptionBillingService.cs` | The abstraction the API depends on. |
| `src/ApplicationCore/Models/Subscriptions/` | Provider-agnostic domain models. |
| `src/ApplicationCore/Exceptions/SubscriptionBillingException.cs` | Carries the HTTP status the API should surface. |
| `src/Infrastructure/Billing/Maxio/` | The Maxio implementation: options, typed client, retry handler, site cache, orchestration. |
| `src/PublicApi/SubscriptionEndpoints/` | The three endpoints and their DTOs. |
| `tests/UnitTests/Infrastructure/Billing/Maxio/` | Idempotency, mapping and error handling against a scripted transport. |
| `tests/PublicApiIntegrationTests/SubscriptionEndpoints/` | Auth and validation contract checks. |

`MaxioApiClient` owns transport only: URLs, JSON and translating Maxio's error bodies into a status
the API can return. `MaxioSubscriptionBillingService` owns policy: plan validation, idempotency and
mapping. Retries for `429`, `5xx` and network failures use exponential backoff with jitter and
honour `Retry-After`; Maxio limits concurrency rather than request rate, so backing off is the
documented way to recover.
