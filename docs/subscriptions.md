# Recurring subscriptions (Maxio Advanced Billing)

eShopOnWeb's original flow is one-time commerce: Catalog → Basket → Order. This capability adds
**recurring subscription billing** alongside it. It is strictly additive — no existing entity,
table, page or endpoint changes behaviour, and the two flows share no state.

**Maxio Advanced Billing is the system of record.** Nothing about plans, customers or
subscriptions is mirrored into the eShopOnWeb database, so the flow behaves the same on SQL Server
and on the in-memory provider, and survives a restart.

Everything sent to Maxio is built from the OpenAPI specification in [`maxio-spec/`](../maxio-spec):
endpoints, path and query parameters, request and response schemas, the auth scheme and the server
URL templates all come from there.

## Endpoints

All three live on `src/PublicApi` and require a JWT bearer token from `POST /api/authenticate`.
The shopper is taken from the token, never from the request body, so a caller can only ever act on
their own subscriptions.

| Method | Route | Purpose |
|--------|-------|---------|
| `GET`  | `/api/subscription-plans` | Plans offered by the configured product family, cheapest first. |
| `POST` | `/api/subscriptions` | Subscribe the caller to a plan. Creates their billing customer on first use. |
| `GET`  | `/api/my-subscriptions` | The caller's subscriptions, newest first. |

`POST /api/subscriptions` takes `{ "planHandle": "eshop-pro", "idempotencyKey": "optional" }` and
answers **201** when it created the subscription, **200** when it returned an existing one. The
response body reports which happened:

```json
{
  "subscription": { "id": 94209203, "state": "active", "planHandle": "eshop-pro",
                    "price": 299, "nextBillingAt": "2026-10-06T14:02:26+05:00", "...": "..." },
  "created": true,
  "outcome": "created"
}
```

`outcome` is one of `created`, `already_subscribed` (the shopper already held a live subscription to
that plan) or `idempotent_replay` (a previous request with the same idempotency key created it).

Plans are addressed by **handle**. Maxio reassigns numeric ids when a catalog is re-seeded, so ids
are exposed for diagnostics only and never used to address anything.

## How subscribing stays idempotent

A double-clicked "Subscribe" must not produce two customers or two subscriptions. Three layers,
strongest last:

1. **In-process guard** — concurrent subscribe calls for the same shopper are serialised
   ([`StripedAsyncLock`](../src/Infrastructure/Maxio/StripedAsyncLock.cs)), so the second one sees
   what the first created instead of racing it.
2. **Live-subscription check** — before creating anything, the shopper's existing subscriptions are
   read; a live one for the same plan is returned as-is.
3. **Unique references** — Maxio enforces uniqueness on both the customer and the subscription
   `reference`, and eShopOnWeb writes deterministic ones
   ([`MaxioReferences`](../src/Infrastructure/Maxio/MaxioReferences.cs)):

   | Record | Reference |
   |--------|-----------|
   | Customer | `eshoponweb:demouser@microsoft.com` |
   | Subscription | `eshoponweb:demouser@microsoft.com:eshop-pro` |
   | Subscription (re-subscribe after cancelling) | `eshoponweb:demouser@microsoft.com:eshop-pro:2` |
   | Subscription (caller supplied an idempotency key) | `eshoponweb:demouser@microsoft.com:key:checkout-42` |

   A duplicate write comes back as `422 Reference: must be unique`, which is treated as "someone
   else already did this" and resolved by reading the record that exists. Only layer 3 holds across
   processes; layers 1 and 2 just save a wasted round trip.

References are derived from the user name in the token because it is stable across restarts, unlike
the identity store's row ids under the in-memory provider.

Callers wanting a hard guarantee should send an `Idempotency-Key` header (or `idempotencyKey` in the
body). The key identifies the *request*, not the plan, so a replay always returns the original
subscription.

## Configuration

Bound from the `Maxio` configuration section. Nothing is hard-coded — the same build runs against a
different Maxio site and a different catalog by changing configuration alone.

| Key | Required | Meaning |
|-----|----------|---------|
| `Maxio:ApiKey` | yes | API key. Sent as the HTTP Basic user name with the fixed password `x`, per the spec's `BasicAuth` scheme. |
| `Maxio:Subdomain` | unless `BaseUrl` is set | Site subdomain, templated into the server URL. |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | Absolute base address used **verbatim** instead of deriving one from the subdomain. |
| `Maxio:Environment` | no | `US` (default) → `https://{site}.chargify.com`; `EU` → `https://{site}.ebilling.maxio.com`. From the spec's server configuration. |
| `Maxio:PaymentCollectionMethod` | no | Defaults to `remittance` (invoice billing), because this integration does not capture card details. |
| `Maxio:ReferencePrefix` | no | Namespace for the references written into Maxio. Defaults to `eshoponweb`. |
| `Maxio:TimeoutSeconds` | no | Per-request timeout. Default 30. |
| `Maxio:MaxRetryAttempts` | no | Transient-failure retries. Default 3. |

**Secrets never go in the repository.** Load them into user-secrets from your environment:

```bash
dotnet user-secrets --project src/PublicApi set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets --project src/PublicApi set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets --project src/PublicApi set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
dotnet user-secrets --project src/PublicApi set "Maxio:Environment"         "$MAXIO_ENVIRONMENT"
```

Outside Development, supply the same keys through environment variables (`Maxio__ApiKey`,
`Maxio__Subdomain`, …) or a secret store. When the section is missing or incomplete the app still
starts — it logs what is missing and the three endpoints answer `503` with the same list. No other
part of eShopOnWeb is affected.

## Payment methods

The seeded demo plans have *payment method not required*, so a shopper is enrolled with no card
capture and no 3-D Secure. Subscriptions are therefore created with `payment_collection_method:
remittance` (invoice billing); with the default `automatic`, Maxio rejects the signup with
"No payment method was on file for the $299.00 balance".

A plan whose `require_credit_card` is true cannot be subscribed to here. The request is refused with
`422` **before** any customer or subscription is written, rather than failing halfway through.

## Error handling

| Situation | Response |
|-----------|----------|
| Unknown plan handle | `404` |
| Missing `planHandle`, or a token with no usable e-mail | `400` |
| Plan requires a stored payment method, or Maxio rejected the request | `422`, with Maxio's own messages |
| Maxio unreachable, timed out, or rejected our credentials | `502` |
| Maxio rate limited us, or the `Maxio` section is not configured | `503` |

All errors use the API's existing `{ "statusCode": …, "message": … }` shape. Transient failures
(network errors, timeouts, 429, 5xx) are retried with exponential backoff and full jitter before
surfacing; `Retry-After` is honoured. Only reads are retried on 5xx — writes rely on the unique
references above rather than on blind repetition.

## Layout

| Path | What lives there |
|------|------------------|
| `src/ApplicationCore/Subscriptions/` | Provider-agnostic model: plan, subscription, subscriber, command/result. |
| `src/ApplicationCore/Interfaces/ISubscriptionService.cs` | The capability's contract. |
| `src/Infrastructure/Maxio/` | Options, typed HTTP client (one member per spec operation), retry handler, mapping, orchestration. |
| `src/PublicApi/SubscriptionEndpoints/` | The three HTTP endpoints and their DTOs. |
| `tests/UnitTests/Infrastructure/Maxio/` | Idempotency, reference, options and mapping tests against an in-memory Maxio. |
| `tests/PublicApiIntegrationTests/SubscriptionEndpoints/` | Authorization tests for the endpoints. |

Swapping billing providers means adding one `ISubscriptionService` implementation; nothing in
ApplicationCore or PublicApi knows Maxio's field names.
