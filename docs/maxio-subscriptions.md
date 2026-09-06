# Recurring subscription billing with Maxio Advanced Billing

eShopOnWeb's original flow (Catalog → Basket → Order) sells one-time purchases. This capability adds
**recurring subscriptions** alongside it, with **Maxio Advanced Billing** as the billing system of
record. Nothing in the existing cart/checkout path changes.

The contract for every Maxio interaction is the OpenAPI specification in [`maxio-spec/`](../maxio-spec).
Endpoints, parameters, payload shapes, the auth scheme, server templating and the error models are
all taken from it; each client method names the `operationId` it implements.

## The hero flow

`POST /api/subscriptions` with a bearer token:

1. Resolve the shopper from the JWT (`ISubscriberFactory`) — callers never pass an identity.
2. Resolve the plan by **handle** from the billing catalog (numeric ids are not stable across
   catalog re-seeds, handles are).
3. Ensure a Maxio customer exists for the shopper (`readCustomerByReference`, then `createCustomer`),
   keyed by a stable reference: `eshoponweb:<email>`.
4. Refuse to enroll twice: if the shopper already has a live subscription to that plan, return it.
5. Otherwise `createSubscription` and return plan, price, state and next billing date.

## Layout

| Where | What |
|---|---|
| `src/ApplicationCore/Subscriptions/` | Provider-agnostic model: `Subscriber`, `SubscriptionPlan`, `CustomerSubscription`, `SubscribeRequest`/`SubscribeResult`, `SubscriptionStates` |
| `src/ApplicationCore/Interfaces/ISubscriptionService.cs` | The capability's contract |
| `src/ApplicationCore/Exceptions/BillingException.cs` | `BillingConfigurationException`, `BillingRequestInvalidException`, `BillingProviderException`, `SubscriptionPlanNotFoundException` |
| `src/Infrastructure/Maxio/` | Maxio implementation: settings, typed HTTP client, DTOs mirroring the spec's schemas, retry handler, `MaxioSubscriptionService` |
| `src/PublicApi/SubscriptionEndpoints/` | The three HTTP endpoints, DTOs and the token → shopper factory |

eShopOnWeb stores **no** subscription state of its own. Maxio is the system of record, so answers
stay correct across restarts and across instances — which matters because the app can run on an
in-memory database that is rebuilt on every start.

## Endpoints

All three are on **`src/PublicApi`** and require a JWT bearer token
(`POST /api/authenticate` issues one). The caller's identity always comes from the token.

### `GET /api/subscription-plans`

Lists the plans in the configured product family
(`listProductsForProductFamily`, or `listProducts` when no family is configured), archived products
excluded, cheapest first.

```json
{
  "subscriptionPlans": [
    {
      "handle": "eshop-pro", "name": "Pro Plan", "price": 299.00, "priceInCents": 29900,
      "interval": 1, "intervalUnit": "month", "billingPeriod": "month",
      "requiresPaymentMethod": false, "taxable": false,
      "productFamilyHandle": "eshop-subscribe"
    }
  ]
}
```

### `POST /api/subscriptions`

```jsonc
{
  "planHandle": "eshop-pro",        // optional when Maxio:DefaultPlanHandle is configured
  "pricePointHandle": null,          // optional, non-default price point
  "idempotencyKey": "checkout-42"    // optional; the Idempotency-Key header is also accepted
}
```

* `201 Created` with `"created": true` — the shopper was enrolled.
* `200 OK` with `"created": false` — an equivalent subscription already existed.

```json
{
  "created": true,
  "subscription": {
    "id": 94211971, "state": "active", "isLive": true,
    "planHandle": "eshop-pro", "planName": "Pro Plan", "price": 299.00,
    "interval": 1, "intervalUnit": "month",
    "nextBillingAt": "2026-10-06T21:00:28+05:00",
    "currentPeriodStartedAt": "2026-09-06T21:00:28+05:00",
    "currentPeriodEndsAt": "2026-10-06T21:00:28+05:00",
    "paymentCollectionMethod": "remittance",
    "customerId": 98840128, "customerReference": "eshoponweb:demouser@microsoft.com"
  }
}
```

### `GET /api/my-subscriptions`

Every subscription the caller holds, newest first, plus an `activeSubscriptions` view containing
only those that still entitle the shopper to a plan.

## Idempotency — a double-click never enrolls twice

Three independent guards, strongest first:

1. **Caller supplied key.** `Idempotency-Key` header (or `idempotencyKey` in the body) is hashed
   together with the shopper's reference into a deterministic subscription `reference`. A replay is
   resolved by `findSubscription` before anything is created, so the same key always yields the same
   subscription.
2. **State in Maxio.** Before creating, the customer's subscriptions are read; a **live** subscription
   to the same plan is returned instead of enrolling again. "Live" follows the spec's
   `Subscription-State` documentation — ended states (`canceled`, `expired`, `trial_ended`,
   `failed_to_create`) do not block re-subscribing.
3. **In-process serialisation.** Concurrent attempts by the same shopper are serialised
   (`StripedAsyncLock`) so the second attempt observes what the first one did rather than racing it.

Customer creation is idempotent by the same logic: the reference is unique in Maxio, so a lost race
on `createCustomer` is resolved by reading the winner back.

## Payment collection

Both demo plans are configured with *payment method not required*, but a Maxio site whose default
collection method is `automatic` still attempts to capture the first charge — which necessarily fails
for a shopper with no payment profile. So when a plan does **not** require a payment method, the
subscription is created with invoice-style collection
(`Collection-Method`: `remittance` on Relationship Invoicing sites, `invoice` on legacy Statements
sites; the architecture is read once from `readSite` and cached). Plans that *do* require a payment
method keep the site default, so Maxio enforces its own payment-profile rules.

`Maxio:PaymentCollectionMethod` overrides this decision outright.

## Configuration

Bound from the `Maxio` section. **No secret is ever stored in the repository** — load the API key
into user-secrets (or the environment / a vault).

| Key | Required | Meaning |
|---|---|---|
| `Maxio:ApiKey` | yes | API key, sent as the HTTP Basic user name (password `x`) per the spec's `BasicAuth` scheme |
| `Maxio:Subdomain` | yes* | Advanced Billing site subdomain |
| `Maxio:BaseUrl` | no | Absolute base URL; when set it is used **verbatim** instead of deriving one from the subdomain |
| `Maxio:ProductFamilyHandle` | no | Product family whose products are offered as plans; empty lists the whole site |
| `Maxio:Environment` | no | `US` (default) or `EU`, per the spec's `x-server-configuration` servers |
| `Maxio:DefaultPlanHandle` | no | Plan used when a subscribe request does not name one |
| `Maxio:PaymentCollectionMethod` | no | Overrides the collection method chosen for new subscriptions |
| `Maxio:TimeoutSeconds` | no | Per-request timeout (default 30) |
| `Maxio:MaxRetryAttempts` / `Maxio:RetryBaseDelayMilliseconds` | no | Transient-fault retry budget (default 3 / 250ms) |
| `Maxio:PlanCacheSeconds` | no | Plan catalog cache lifetime (default 60, `0` disables) |

\* either `Maxio:Subdomain` or `Maxio:BaseUrl`.

Load the sandbox credentials from the environment into user-secrets (values never touch the repo):

```bash
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"              --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"       --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
dotnet user-secrets set "Maxio:Environment"         "$MAXIO_ENVIRONMENT"          --project src/PublicApi
dotnet user-secrets set "Maxio:DefaultPlanHandle"   "eshop-pro"                   --project src/PublicApi
```

Missing configuration never prevents the host from starting: a warning is logged at startup and the
subscription endpoints answer `503` with a message naming the keys to set. The rest of the API is
unaffected.

## Resilience and failure mapping

* Reads are retried on network faults, timeouts, `429` and `5xx` with exponential backoff, jitter and
  `Retry-After` support. Writes are retried **only** on `429`, where Maxio has explicitly rejected the
  call without processing it — anything else could have been applied and must not be replayed blindly.
* Error payloads are parsed from every shape the spec declares (`Error-List-Response`,
  `Error-Array-Map-Response`, `Customer-Error-Response`, `Single-String-Error-Response`,
  `Single-Error-Response`, and bare JSON strings).

| Failure | HTTP |
|---|---|
| Billing not configured, or Maxio rejected the credentials (`401`/`403`) | `503` |
| Unknown plan handle | `404` |
| Maxio rejected the request (`422`/`400`) | `400`, with Maxio's messages |
| Maxio unreachable, timed out, or `5xx` | `502` |

The API key is never logged; requests are logged as method, path, status and elapsed time.

## Tests

* `tests/UnitTests/Infrastructure/Maxio/` — base URL derivation, error-payload parsing, transport
  (paths, query strings, Basic auth, envelopes, `404` → null, `422` → typed exception), retry policy,
  and the service's idempotency, mapping, collection-method and failure-translation behaviour.
* `tests/PublicApiIntegrationTests/SubscriptionEndpoints/` — the three endpoints over real HTTP with
  billing stubbed: authentication, status codes, payloads, per-shopper isolation.

No test calls Maxio, so the suite runs offline.
