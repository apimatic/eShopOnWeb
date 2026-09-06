# Subscription billing (Maxio Advanced Billing)

Recurring-plan billing for eShopOnWeb shoppers, exposed on PublicApi. This runs **alongside** the
existing Catalog → Basket → Order flow and shares no state with it.

**Maxio is the system of record.** eShopOnWeb persists nothing about plans, customers or
subscriptions: every read goes back to Maxio, so what a shopper sees is what Maxio will bill.

## Endpoints

All three require a JWT bearer token from `POST /api/authenticate`. The subscriber is always the
bearer of the token — there is no way to subscribe somebody else.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/subscription-plans` | The plans on offer (the non-archived products of the configured product family). |
| `POST` | `/api/subscriptions` | Subscribe the caller to a plan. `201` when it enrolls them, `200` when they were already enrolled. |
| `GET` | `/api/my-subscriptions` | Every subscription the caller holds, in any state. |

`POST /api/subscriptions` body — both fields optional:

```json
{ "planHandle": "eshop-pro", "idempotencyKey": "b6c1…" }
```

Omit `planHandle` to use `Maxio:DefaultPlanHandle`, or the only plan on offer when there is exactly
one. The idempotency key may also be sent as an `Idempotency-Key` header.

Failures map to: `400` unknown plan, `401` no/unknown identity, `409` a concurrent subscribe that
could not be resolved, `502` Maxio unreachable or refusing the request.

## How subscribing stays idempotent

A double click must not create two customers or two subscriptions. Maxio will not prevent that on
its own — it will happily create a second subscription to the same product for the same customer —
so the guarantee is built here, in three layers, because no single layer covers every case:

1. **A per-shopper lock** (`SubscriberLock`) serialises concurrent attempts inside this process.
   Cheap and exact, but only within one instance.
2. **An existing-enrollment check** re-reads the shopper's subscriptions from Maxio and returns the
   one they already hold. This catches attempts the lock did not serialise — a second instance, a
   retry minutes later — and is what makes the operation idempotent rather than merely serialised.
   A subscription in a recoverable problem state (`past_due`, `on_hold`, `suspended`) still counts
   as an enrollment; only the end-of-life states (`canceled`, `expired`, `failed_to_create`,
   `trial_ended`) free the shopper to subscribe again.
3. **A `uniqueness_token`** on the create call. If a request reached Maxio but the response was
   lost, the retry is rejected with `409` instead of creating a second subscription. Maxio will not
   say whether the first request succeeded, so the handler re-reads the shopper's subscriptions and
   returns what it finds; only if it finds nothing does it report a conflict.

The customer record gets the same treatment: look up by reference, create if absent, and if the
create loses a race (Maxio reports the reference as taken) read back the winner's customer.

Two deliberate choices behind that:

- **The uniqueness token is per attempt, not per (shopper, plan).** A deterministic token would
  lock a shopper out of a genuine retry for the whole dedupe window after a validation failure.
  Pass an `Idempotency-Key` when you want a specific retry deduplicated.
- **The subscription reference is per attempt too.** Maxio requires subscription references to be
  unique site-wide, and a *failed* attempt still consumes the reference it was given.

## Identity mapping

The Maxio customer `reference` is `eshoponweb-{buyerId}`, where `buyerId` is the shopper's user
name — the same value `Basket.BuyerId` and `Order.BuyerId` use. One shopper, one identity across
one-time commerce and recurring billing, and legible in the Maxio UI.

## Configuration

Bound from the `Maxio` section. `ApiKey` is a secret and must come from user secrets or the
platform's secret store — never from a file in this repository.

| Key | Required | Notes |
|---|---|---|
| `Maxio:ApiKey` | yes | Site API key. Sent as HTTP Basic username, password is a literal `X`. |
| `Maxio:Subdomain` | yes* | Derives the API host `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | yes | Selects which products are offered as plans. |
| `Maxio:BaseUrl` | no | Overrides the API base address verbatim. Makes `Subdomain` optional. |
| `Maxio:DefaultPlanHandle` | no | Plan used when a subscribe request names none. |
| `Maxio:PaymentCollectionMethod` | no | Defaults to `remittance`. |
| `Maxio:TimeoutSeconds` | no | Per-call timeout, default 30. Maxio itself cuts off at 120s. |
| `Maxio:MaxRetryAttempts` | no | Retries after a throttled or transient failure, default 3. |
| `Maxio:PlanCacheSeconds` | no | Plan catalog cache lifetime, default 60. |

\* Required unless `Maxio:BaseUrl` is set.

Startup fails with a message naming every missing key if the section is incomplete. That is
deliberate: a half-configured billing integration should not reach the point of taking a shopper's
subscribe request.

### Why `remittance`

The demo plans are configured "payment method not required", but the sandbox site collects
automatically by default, so a subscribe with no stored card fails with *"No payment method was on
file for the $299.00 balance"*. `payment_collection_method: remittance` invoices the customer
instead of charging a card, which is what lets a shopper subscribe without card capture or 3-DS.
Set `Maxio:PaymentCollectionMethod` to `automatic` on a site where a stored card is expected.

## Talking to Maxio

`MaxioApiClient` is a typed `HttpClient`; auth, timeout and retries are configured on the client in
`MaxioServiceCollectionExtensions`. `MaxioRetryHandler` backs off exponentially with jitter rather
than retrying harder — Maxio limits *concurrency*, not request rate, and asks callers not to answer
throttling with more parallelism. It replays reads freely, but replays a write only on `429`, where
the response proves the request was rejected outright rather than possibly acted on.

## Layering

- `ApplicationCore/Entities/SubscriptionAggregate` — plans, subscriptions, the subscriber identity.
- `ApplicationCore/Interfaces/IBillingGateway` — the port onto the billing system.
- `ApplicationCore/Services/SubscriptionService` — the enrollment rules, with no knowledge of HTTP.
- `Infrastructure/Maxio` — the adapter: settings, wire models, retry handler, DI wiring.
- `PublicApi/SubscriptionEndpoints` — routing, auth, DTOs, status codes.

Swapping billing providers means writing another `IBillingGateway`; nothing above it changes.
