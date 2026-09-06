# Recurring subscriptions (Maxio Advanced Billing)

eShopOnWeb's original flow is one-time commerce: Catalog → Basket → Order. This adds a **parallel**
capability — recurring subscriptions — with **Maxio Advanced Billing as the system of record**. Nothing
about the existing cart or checkout changes, and eShopOnWeb stores no billing state of its own.

## Endpoints (`src/PublicApi`, JWT-authenticated)

All three require a bearer token from `POST /api/authenticate`. **The shopper being billed is taken from
the token and from nothing else**, so no caller can act on another shopper's behalf.

| Endpoint | Purpose |
|---|---|
| `GET /api/subscription-plans` | Plans in the configured product family — handle, name, price, interval, currency, whether a card is required |
| `POST /api/subscriptions` | Subscribe the caller to a plan, by handle. **Idempotent** |
| `GET /api/my-subscriptions` | The caller's subscriptions — plan, price, state, next billing date |

`POST /api/subscriptions` takes `{"planHandle":"eshop-pro"}` and answers:

* **`201`** with `"created": true` — the shopper was enrolled by this request.
* **`200`** with `"created": false` — an equivalent live subscription already existed and is returned
  unchanged. This is the path a double-click, a retry or a refresh takes.

Plans are addressed by **handle**, never by numeric id: Maxio reassigns ids when a catalog is re-seeded,
so the product family handle is resolved to its current id at runtime and cached.

## Configuration

Bound from the `Maxio` configuration section. No value is compiled in — the same build runs against a
different Maxio site and a different catalog by changing configuration alone.

| Key | Required | Meaning |
|---|---|---|
| `Maxio:ApiKey` | yes | Maxio API key. Sent as the HTTP Basic username |
| `Maxio:Subdomain` | yes, unless `BaseUrl` is set | Maxio site subdomain. A sandbox site is selected here — Maxio has no separate "sandbox" environment |
| `Maxio:ProductFamilyHandle` | yes | Handle of the product family holding the subscribable plans |
| `Maxio:BaseUrl` | no | Verbatim API base address. When set it is used **as-is**, instead of deriving one from the subdomain |
| `Maxio:Environment` | no | `US` (default) or `EU` — the Maxio server region |
| `Maxio:PaymentCollectionMethod` | no | Override the collection method (`remittance`, `invoice`, `automatic`, `prepaid`). Derived from the site when unset |
| `Maxio:RequestTimeout` | no | Total budget for one billing operation, retries included. Default `00:00:30` |
| `Maxio:AttemptTimeout` | no | Budget for a single HTTP attempt. Default `00:00:08` |
| `Maxio:MaxRetries` | no | Extra attempts after the first. Default `1` |
| `Maxio:CatalogCacheDuration` | no | How long the family id, site settings and plan list are cached. Default `00:05:00` |
| `Maxio:CustomerReferencePrefix` | no | Namespaces the Maxio customer reference. Default `eshoponweb` |
| `Maxio:LogRequests` | no | Log every outbound Maxio call's method, URL and response status. Default `false` |

**Secrets never belong in the repository.** Load them with user-secrets:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

If the section is missing or incomplete, only the three subscription endpoints fail — with `503` and a
clear message. The rest of the API keeps working.

## How the idempotency guarantee is built

A double-clicked *Subscribe* button must never produce two customers or two subscriptions. Maxio offers no
idempotency key on subscription creation, so the guarantee is assembled from four layers:

1. **A stable customer reference.** The Maxio customer is keyed on `{prefix}:{username}` derived from the
   JWT. Maxio enforces that a reference identifies at most one customer, so find-or-create by reference is
   idempotent by construction. The account name is used rather than the Identity primary key because the
   primary key does not survive a restart on the in-memory provider, whereas the account name does — so a
   shopper's subscriptions still resolve after the app restarts.
2. **A pre-existing-subscription check.** Before creating, the customer's subscriptions are listed and
   matched on plan handle plus a live state (`active`, `assessing`, `pending`, `trialing`, `paused`, and
   `awaiting_signup` — a signup already in flight is exactly the duplicate we must not make). A match short-
   circuits to `200 created:false` without writing anything.
3. **A per-shopper lock.** Concurrent subscribe requests for the same shopper are serialized in-process, so
   the second one observes the first one's result instead of racing past the check.
4. **A write-once guard.** The SDK's retry pipeline re-sends on a transport failure regardless of HTTP verb,
   and retries cannot be switched off — so a connection reset could otherwise enroll a shopper twice. A
   `DelegatingHandler` counts sends in an `AsyncLocal` scope (a marker on the request object would be lost,
   since each retry builds a fresh request) and refuses a re-send with a sentinel exception that is
   deliberately *not* an `HttpRequestException`, which would itself be retried. A refused re-send means the
   outcome is **unknown**, not failed — so the integration then **reconciles** by re-reading Maxio state, and
   only reports failure if the write genuinely did not land.

Layers 1–2 are durable across processes; 3–4 are per-instance. Behind multiple instances, 1 and 2 remain the
load-bearing defences.

## Payment handling

This API captures no card. Both demo plans have `require_credit_card = false`, but a site's default
collection method still attempts to charge the first period immediately, which fails with *"No payment
method was on file"*. Subscriptions are therefore created with an explicit collection method that invoices
rather than charges — `remittance` on Relationship Invoicing sites, `invoice` on legacy Statements sites,
chosen from the site's own `relationship_invoicing_enabled` flag. Override with
`Maxio:PaymentCollectionMethod` if a deployment needs something else.

Plans whose `require_credit_card` is true are rejected up front with `400`, before any write. Note that
Maxio's `request_credit_card` is a **deprecated** legacy hosted-page field and is deliberately not read as a
payment requirement.

## Failures

Every provider failure — API error, transport failure, or an unreadable payload — is translated at the
integration boundary into a single `BillingException` carrying a failure *kind*. Only a caller-safe message
crosses the wire; provider bodies and stack traces stay in the log.

| Kind | HTTP | When |
|---|---|---|
| `InvalidRequest` | `400` | Missing plan handle, or a plan that requires a card |
| `NotFound` | `404` | No such plan |
| `Rejected` | `422` | Maxio deterministically refused. Its messages are surfaced in `billingMessages` |
| `Configuration` | `503` | Missing API key, unknown product family, or a `401` from Maxio — never the caller's fault |
| `Unavailable` | `503` | Maxio unreachable or too slow |
| `OutcomeUnknown` | `502` | The write may have taken effect. **Re-read before retrying** |

`OutcomeUnknown` is kept distinct on purpose: telling a caller "failed" when the subscription may exist is
how shoppers get billed twice.

## Layout

| Path | What |
|---|---|
| `src/ApplicationCore/Interfaces/ISubscriptionBillingService.cs` | The abstraction. No SDK types |
| `src/ApplicationCore/Subscriptions/` | `SubscriberIdentity`, `SubscriptionPlan`, `CustomerSubscription`, `SubscribeResult` |
| `src/ApplicationCore/Exceptions/BillingException.cs` | The one failure type the integration raises |
| `src/Infrastructure/Billing/Maxio/` | The Maxio adapter: options, DI registration, call scope, write-once handler, per-key lock, the service |
| `src/PublicApi/SubscriptionEndpoints/` | The three endpoints, their DTOs, and the failure→HTTP mapping |
| `tests/UnitTests/Infrastructure/Billing/Maxio/` | 22 tests against a faked `HttpClient`, including write-once under a transport fault |
| `tests/PublicApiIntegrationTests/SubscriptionEndpoints/` | Endpoint authorization tests |

The SDK client is registered by hand over a **named** `HttpClient` rather than through the SDK's own DI
extension, because that extension resolves the default unnamed factory client — the per-attempt timeout and
the write-once handler would otherwise leak onto every other unnamed `HttpClient` consumer in the app.
