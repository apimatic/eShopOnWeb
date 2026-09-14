# Subscription billing with Maxio Advanced Billing

eShopOnWeb is a one-time-commerce sample: Catalog → Basket → Order. This document describes the
recurring-subscription capability added alongside it. The two are independent — subscribing does not
touch a basket, an order, or the catalog.

**Maxio Advanced Billing is the system of record.** eShopOnWeb persists no subscription state at all.
Every read goes to Maxio, and the link between an eShopOnWeb user and their Maxio records is a
reference derived from the user name rather than a stored mapping. That is what lets the feature work
correctly on the in-memory database, where nothing survives a restart.

## Layout

| Location | Contents |
| --- | --- |
| `src/ApplicationCore/Subscriptions/` | Provider-agnostic models: `SubscriptionPlan`, `CustomerSubscription`, `SubscribeRequest`, `SubscribeResult`, `SubscriptionStates`. |
| `src/ApplicationCore/Interfaces/ISubscriptionService.cs` | The capability, expressed without reference to Maxio. |
| `src/ApplicationCore/Exceptions/` | `SubscriptionPlanNotFoundException`, `BillingProviderException`, `BillingConfigurationException`. |
| `src/Infrastructure/Billing/Maxio/` | The Maxio implementation: typed HTTP client, wire contracts, retry handler, settings, and `MaxioSubscriptionService`. |
| `src/Infrastructure/Billing/` | Provider-neutral helpers: `KeyedAsyncLock`, `AsyncTtlCache<T>`, DI registration. |
| `src/PublicApi/SubscriptionEndpoints/` | The three HTTP endpoints and their DTOs. |
| `tests/UnitTests/Infrastructure/Billing/` | 54 tests, including concurrency, race-loss and retry-policy cases, over an in-memory fake that enforces Maxio's reference-uniqueness rule. |

Swapping billing providers means writing one more `ISubscriptionService`; nothing above
`ApplicationCore` mentions Maxio.

## The Maxio API surface used

Plain HTTPS with `System.Net.Http`, registered as a typed client. Every path below was taken from the
API surface Maxio publishes (cross-checked against the endpoint definitions in Maxio's own
`ab-dotnet-sdk`) and exercised against a live sandbox site before being coded against.

| Call | Used for |
| --- | --- |
| `GET /site.json` | The site currency. Products do not carry one, but prices are quoted in it. |
| `GET /product_families.json` | Resolving the configured family handle to its id. |
| `GET /product_families/{id}/products.json` | The products published as plans. |
| `GET /customers/lookup.json?reference=…` | Finding the Maxio customer for an eShopOnWeb user. `404` when absent. |
| `POST /customers.json` | Creating that customer on first use. |
| `GET /customers/{id}/subscriptions.json` | Everything the user holds — the source for both idempotency and `my-subscriptions`. |
| `POST /subscriptions.json` | Signing the user up. |

Authentication is HTTP Basic with the API key as the user name and a literal `x` as the password.
The base address is `https://{subdomain}.chargify.com` unless `Maxio:BaseUrl` overrides it.

### Why numeric ids are never configured

Maxio reassigns product-family and product ids when a site is re-seeded; handles are stable. So the
family id is resolved from its handle at runtime (and refreshed with the cached catalog), and
subscriptions are created with `product_handle` rather than `product_id`.

### Why signups use `remittance`

The seeded plans set `require_credit_card: false`, but that alone is not sufficient. The site default
collection method is `automatic`, and Maxio refuses an `automatic` signup that has an immediate
balance and no stored payment method:

```
422 {"errors":["No payment method was on file for the $299.00 balance"]}
```

This integration captures no card details, so subscriptions are created with
`payment_collection_method: "remittance"` — Maxio invoices the customer instead of charging a card.
`Maxio:PaymentCollectionMethod` overrides this for a deployment that does capture payment methods.

## Idempotency

The requirement is that a double-clicked subscribe button never produces two Maxio customers or two
subscriptions. Three mechanisms combine, each covering what the previous one cannot.

**1. Deterministic references.** The customer reference is `{prefix}:{username}`; a subscription's is
`{customer-reference}:{plan-handle}`. Maxio enforces uniqueness on both within a site, which turns a
duplicate write into a rejection rather than a duplicate record. Nothing random is involved, so a
retry lands on the same reference as the attempt it is retrying.

**2. Read before write.** Subscribe looks the customer up before creating one, and lists the
customer's subscriptions before signing them up. A live subscription to the requested plan short-
circuits the signup and comes back with `alreadySubscribed: true` and HTTP `200`.

**3. A per-user in-process lock.** Concurrent requests from the same user are serialised through the
check-then-create sequence, so the second observes the first's work instead of racing it.
`KeyedAsyncLock` reference-counts its entries, so a long-lived instance does not accumulate one
semaphore per user ever seen.

The lock is process-local and so cannot serialise two application instances. That case is caught by
(1): the loser gets `422 Reference: must be unique`, re-reads the customer, and adopts the winner's
subscription. Both callers end up with the same subscription; no third mechanism (or shared store) is
needed. `LosingTheCreateRaceAdoptsTheWinnerInsteadOfFailing` in the unit tests covers exactly this.

A **subscription state** is treated as live unless it is `canceled`, `expired`, or
`failed_to_create`. Unknown states — including any Maxio adds later — count as live, because the safe
failure is "returned the existing subscription", not "enrolled the shopper twice".

**Re-subscribing after cancellation** is still possible: because the old subscription holds the
unsuffixed reference, the new one takes the lowest free numeric suffix (`…:pro:2`). The choice is
derived from what Maxio already holds, so it stays deterministic and a retry still collides rather
than duplicating.

## Resilience

`MaxioRetryHandler` retries with exponential backoff and jitter, honouring `Retry-After`. What it
retries is deliberately narrow, since blindly retrying a signup would enroll a shopper twice:

- **HTTP 429** is retried for any method — a throttled request was rejected before being processed.
- **5xx, 408 and network faults** are retried for `GET` only; for those the write may well have
  landed and only the response was lost.

A non-idempotent call that fails that way surfaces to `MaxioSubscriptionService`, which re-reads
Maxio rather than guessing.

The plan catalog and site currency are cached for `Maxio:CatalogCacheDuration` (5 minutes by
default) behind a single-flight cache, so a burst on a cold cache makes one upstream call rather than
one per request.

## Failure mapping

`ExceptionMiddleware` translates the domain exceptions to status codes:

| Condition | Status |
| --- | --- |
| Plan handle not on offer, or several plans and no default configured | `404` |
| Missing or unusable `Maxio:` configuration | `503`, naming the keys to set |
| Maxio rejected the call or could not be reached | `502`, with a generic message; the Maxio detail goes to the log, not the caller |

## Security

- The subscriber is taken from the bearer token and only from the token. `CreateSubscriptionRequest`
  (the wire contract) carries no user field at all, so there is nothing a caller could set to enroll
  or inspect somebody else. `CreateSubscriptionCommand` — the type the handler acts on — is built
  server-side from the validated principal.
- Maxio credentials are never in the repository. They are read from the environment into user-secrets
  in development, and from environment variables or a key vault elsewhere.
- Maxio error text is logged but not returned to callers, so upstream internals are not echoed to the
  internet.

## Known limits

- eShopOnWeb stores no real name for a user, so the Maxio customer's first and last name are derived
  from the e-mail local part (`jane.doe@…` → Jane Doe; `demouser@…` → Demouser eShopOnWeb).
- Cancellation, plan changes, invoices, metered usage (the seeded `api-call` component) and webhook
  ingestion are not implemented — the scope here is the subscribe flow.
- Subscription pricing is scaled by 100 from Maxio's minor-unit fields (`price_in_cents`). A
  zero-decimal currency would need that generalised.
