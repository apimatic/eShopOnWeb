# Subscription billing (Maxio Advanced Billing)

eShopOnWeb's original flow is one-time commerce: Catalog → Basket → Order. This adds a second,
parallel capability — recurring subscriptions — without touching that flow. A shopper can browse
plans, subscribe to one, and see the result on their account.

**Maxio Advanced Billing is the system of record.** No plan, customer or subscription data is
stored in the eShopOnWeb databases. Every answer is read from Maxio at request time, so the API
stays correct across restarts and reflects changes made directly in the Maxio UI.

## Endpoints

All three live on `src/PublicApi` and require a JWT bearer token. The shopper is identified from
the token alone; the request body never says who the caller is.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/subscription-plans` | Plans offered by the configured product family |
| `POST` | `/api/subscriptions` | Subscribe the caller to a plan |
| `GET` | `/api/my-subscriptions` | The caller's subscriptions, newest first |

### `POST /api/subscriptions`

```jsonc
{
  "planHandle": "eshop-pro",          // required; from GET /api/subscription-plans
  "idempotencyKey": "a-uuid"          // optional; see "Idempotency" below
}
```

Responds `201 Created` when a subscription was created and `200 OK` with
`"alreadySubscribed": true` when the caller was already subscribed to that plan. Both carry the
same body, confirming plan, price, state and next billing date:

```jsonc
{
  "subscription": {
    "id": 94208771,
    "reference": "eshoponweb:demouser@microsoft.com:eshop-pro",
    "state": "active",
    "isLive": true,
    "planHandle": "eshop-pro",
    "planName": "Pro Plan",
    "price": 299,
    "priceInCents": 29900,
    "currency": "USD",
    "interval": 1,
    "intervalUnit": "month",
    "nextBillingAt": "2026-10-06T12:08:30+05:00",
    "currentPeriodStartedAt": "2026-09-06T12:08:30+05:00",
    "currentPeriodEndsAt": "2026-10-06T12:08:30+05:00",
    "balance": 299,
    "paymentCollectionMethod": "remittance",
    "customerId": 98837496,
    "customerReference": "eshoponweb:demouser@microsoft.com"
  },
  "alreadySubscribed": false
}
```

Failures come back as RFC 9457 problem documents with a `failure` extension naming the cause:

| Status | `failure` | Meaning |
| --- | --- | --- |
| 400 | `InvalidRequest` | `planHandle` missing or blank |
| 401 | — | No token, or the token's user no longer exists |
| 404 | `PlanNotFound` | No such plan in the configured product family |
| 409 | `Conflict` | A subscribe for this account is already in flight, or an idempotency key was replayed and produced nothing |
| 422 | `UpstreamRejected` | Maxio understood the request and refused it; `errors` carries Maxio's own messages |
| 502 | `UpstreamUnavailable` | Maxio could not be reached, or failed transiently |
| 503 | `NotConfigured` | The deployment has no Maxio configuration |

## Configuration

Bound from the `Maxio` configuration section. **No value belongs in the repository** — supply
them through user-secrets in development, and environment variables or a vault elsewhere.

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | API key; sent as the HTTP Basic username |
| `Maxio:Subdomain` | yes* | Maxio site subdomain; the API base address is derived from it |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are offered as plans |
| `Maxio:BaseUrl` | no | Override. When set it is used verbatim as the API base address instead of deriving one from the subdomain — needed for sites that are not on Maxio's default US host, such as EU sites. |

\* Not required when `Maxio:BaseUrl` is set.

Load them into user-secrets from the environment (values never touch a file in the repo):

```bash
dotnet user-secrets --project src/PublicApi set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets --project src/PublicApi set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets --project src/PublicApi set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

Nothing is hard-coded to a particular site or catalog: plans are discovered from whatever product
family is configured, and the currency comes from the site itself. A deployment with no Maxio
configuration still starts and serves the rest of the API — it logs a warning at startup and the
three subscription endpoints answer `503`.

## Idempotency

Subscribing twice must not bill twice. Four mechanisms, from cheapest to strongest:

1. **A per-subscriber lock inside the process.** Concurrent subscribe requests for one shopper are
   serialized, so a double-clicked button cannot race two enrollments past each other's checks.
2. **An authoritative pre-check.** Inside the lock, Maxio is asked what the customer already has.
   A subscription to the same plan that has not reached a terminal state is returned as-is, with
   `alreadySubscribed: true`.
3. **Unique references.** The shopper's user name is stored as the Maxio *customer reference*, and
   `customer-reference:plan-handle` as the *subscription reference*. Maxio enforces uniqueness on
   both per site, so even a request from another instance — or a retry of one that timed out on
   the way back — is rejected rather than duplicated, and is then resolved to the existing record.
   A collision with an *ended* subscription means the shopper is legitimately signing up again, so
   the retry gets a disambiguated reference.
4. **Optional caller idempotency key.** `idempotencyKey` in the request body is forwarded as
   Maxio's `uniqueness_token`, which rejects a replay within the hour. It is opt-in precisely
   because that window also blocks retrying a request that *failed* — so no token is sent when the
   caller does not ask for one, and a failed subscribe stays immediately retryable.

Customer creation is idempotent by the same reference rule: a losing racer gets `422 Reference:
must be unique` and reads the customer back instead of creating a second one.

## Payment collection

The demo plans do not require a stored payment method, and this integration deliberately captures
no card details (no PCI surface, no 3-D Secure redirect). Sites collect payment automatically by
default, which fails at signup when there is no card on file, so the integration reads the site's
configuration and asks for invoicing instead — `remittance` on Relationship Invoicing sites,
`invoice` otherwise. A plan that *does* require a payment method is refused up front with a
`422` explaining why, rather than being sent to Maxio to fail.

## Resilience

- Reads retry on `429`, on `5xx` and on transport failures, with exponential backoff, jitter, and
  `Retry-After` honoured. Maxio limits by *concurrency*, so backing off is the documented remedy.
- Writes retry only on `429` — the one response that guarantees Maxio did not process the request.
  A `5xx` or a dropped connection may have created something; reissuing blindly is how customers
  get billed twice. Recovery is handled a level up, by looking for the record the failed attempt
  may have created.
- Plans are cached for a minute and site settings for thirty, to keep plan browsing off the wire.

## Where the code lives

| Path | Role |
| --- | --- |
| `src/ApplicationCore/Subscriptions/` | Plans, subscriptions, subscriber, result type — no billing vendor in sight |
| `src/ApplicationCore/Interfaces/ISubscriptionService.cs` | The capability the API depends on |
| `src/Infrastructure/Maxio/` | Maxio settings, typed API client, retry handler, and the service that implements the interface |
| `src/PublicApi/SubscriptionEndpoints/` | The three HTTP endpoints, DTOs, and failure-to-status mapping |
| `tests/UnitTests/Infrastructure/Maxio/` | Client, retry and orchestration tests against a stubbed Maxio |
| `tests/PublicApiIntegrationTests/SubscriptionEndpoints/` | Endpoint authorization tests |
