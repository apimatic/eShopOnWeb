# Subscription billing with Maxio Advanced Billing

eShopOnWeb's catalog/basket/order flow is one-time commerce. This capability adds **recurring
subscriptions** alongside it, with **Maxio Advanced Billing** as the system of record. Nothing
in the existing checkout path changes: a shopper can subscribe without ever touching a basket,
and eShopOnWeb stores no billing state of its own.

The [Maxio OpenAPI specification](../maxio-spec/openapi.yaml) is the contract. Every endpoint,
path parameter, query parameter, request body, response shape, error model and the auth scheme
come from it, and [conformance tests](../tests/UnitTests/Maxio/MaxioSpecificationConformanceTests.cs)
read the spec at test time and fail if the client drifts from it.

## Endpoints (`src/PublicApi`)

All three require a JWT bearer token from `POST /api/authenticate`; the shopper's identity is
taken from the token, never from the request body.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/subscription-plans` | Plans in the configured product family |
| `POST` | `/api/subscriptions` | Subscribe the caller to a plan (idempotent) |
| `GET` | `/api/my-subscriptions` | The caller's own subscriptions |

`POST /api/subscriptions` body (both fields optional):

```json
{ "planHandle": "eshop-pro", "idempotencyKey": "checkout-42" }
```

* `planHandle` — omit it to use `Maxio:DefaultPlanHandle`; if neither is present the call is a
  `400`.
* `idempotencyKey` — replaying the same key for the same shopper always returns the same
  subscription.

Responses: `201 Created` when the subscription was created, `200 OK` with
`"alreadySubscribed": true` when the shopper was already enrolled.

```json
{
  "subscription": {
    "id": 94213441,
    "reference": "eshoponweb:sub:demouser@microsoft.com:eshop-pro",
    "state": "active",
    "isLive": true,
    "planHandle": "eshop-pro",
    "planName": "Pro Plan",
    "price": 299.00,
    "priceInCents": 29900,
    "currency": "USD",
    "interval": 1,
    "intervalUnit": "month",
    "currentPeriodEndsAt": "2026-10-06T23:50:48+05:00",
    "nextBillingAt": "2026-10-06T23:50:48+05:00",
    "paymentCollectionMethod": "remittance"
  },
  "alreadySubscribed": false
}
```

Failures are `application/problem+json` and carry the provider's own messages:

| Status | Meaning |
| --- | --- |
| `400` | Malformed request (unknown/oversized field, no plan and no default) |
| `401` | No/invalid token, or the token names a user that no longer exists |
| `404` | The plan handle is not in the configured product family |
| `422` | Maxio rejected the request (e.g. *No payment method was on file…*) |
| `502` | Maxio could not be reached or returned an unusable response |
| `503` | Subscription billing is not configured on this server |

## Configuration

Bound from the `Maxio` section. Only the first four matter for a normal deployment; the rest
have working defaults in `src/PublicApi/appsettings.json`.

| Key | Source | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | `MAXIO_API_KEY` | **Secret.** Sent as the HTTP Basic user name, password `x`, per the spec's `BasicAuth` scheme |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Templated into the spec's server URL |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Plans are read from this family, addressed as `handle:<value>` |
| `Maxio:BaseUrl` | — | Optional override; when set it is used **verbatim** instead of the templated URL |
| `Maxio:Environment` | `MAXIO_ENVIRONMENT` | `US` → `https://{site}.chargify.com`, `EU` → `https://{site}.ebilling.maxio.com` (from the spec's server configuration) |
| `Maxio:DefaultPlanHandle` | — | Plan used when a subscribe request names none |
| `Maxio:PaymentCollectionMethod` | — | Default `remittance`; see below |
| `Maxio:TimeoutSeconds` | — | Per-attempt timeout, default `30` |
| `Maxio:MaxRetryAttempts` / `Maxio:RetryBaseDelayMilliseconds` | — | Retry budget, default `3` / `250` |
| `Maxio:PlanCacheSeconds` | — | Plan catalog cache, default `60`; `0` disables |
| `Maxio:ReferencePrefix` | — | Prefix of the reference values written to Maxio, default `eshoponweb`; give each deployment that shares one Maxio site its own |

Nothing is hard-coded: point the same build at a different site and a different catalog by
changing configuration only.

**Secrets never enter the repository.** Load them into user-secrets from the environment:

```powershell
./scripts/Set-MaxioUserSecrets.ps1
```

(Equivalently `dotnet user-secrets --project src/PublicApi set "Maxio:ApiKey" $env:MAXIO_API_KEY`,
or supply `Maxio__ApiKey` as an environment variable in production.)

### Why `remittance` is the default collection method

The seeded plans do not require a stored payment method, but Maxio still needs one to *collect*
the signup charge under `automatic` collection - a subscribe call without a card fails with
*"No payment method was on file for the $299.00 balance"*. `remittance` (invoice billing) is a
value the spec's `Collection-Method` schema allows and lets a shopper subscribe without card
capture or 3-DS. A deployment that captures cards should set
`Maxio:PaymentCollectionMethod` to `automatic`.

## How it works

```
PublicApi endpoint ──> SubscriptionApiService ──> ISubscriptionService ──> IMaxioApiClient ──> Maxio
 (route, auth,          (ClaimsPrincipal ->        (MaxioSubscription-      (typed HTTP client
  validation,            Subscriber via             Service: plans,          written to the spec,
  problem responses)     UserManager)               idempotent subscribe)    Basic auth, retries)
```

| Layer | Location |
| --- | --- |
| Contracts and domain types | `src/ApplicationCore/Interfaces/ISubscriptionService.cs`, `src/ApplicationCore/Entities/SubscriptionAggregate` |
| Maxio client, options, retry policy, orchestration | `src/Infrastructure/Maxio` |
| HTTP surface | `src/PublicApi/SubscriptionEndpoints` |

Operations used, all from the spec: `listProductsForProductFamily`, `readCustomerByReference`,
`createCustomer`, `listCustomerSubscriptions`, `findSubscription`, `createSubscription`
(see `src/Infrastructure/Maxio/MaxioOperations.cs`).

### Idempotency

There is no local `userId → subscription` table (and the in-memory database used in this
environment would lose one on restart anyway). The link is a **deterministic reference** stored
on the Maxio records:

* customer → `eshoponweb:user:<username>`
* subscription → `eshoponweb:sub:<username>:<plan-handle>`, or
  `eshoponweb:sub:<username>:key:<idempotency-key>` when the caller supplies a key

Subscribing therefore:

1. serialises a shopper's concurrent attempts in-process (`KeyedAsyncLock`) so a double click
   does not produce two round trips;
2. looks the customer up by reference and creates it only if missing - and if a concurrent
   request won the race (Maxio replies *"Reference: must be unique"*), adopts the winner;
3. looks the subscription up by reference; a **live** one (or any one matching a caller-supplied
   idempotency key) is returned as `alreadySubscribed`;
4. as a safety net, checks the customer's subscriptions for a live one on the same plan, which
   catches subscriptions created outside this reference scheme (for example in the Maxio UI);
5. creates the subscription - and if that loses a race on the reference, re-reads and returns the
   winner instead of failing.

A subscription that has ended (`canceled`, `expired`, …) does not block a new one: it is created
under a freshly suffixed reference, because Maxio enforces reference uniqueness site-wide.

### Resilience

`MaxioRetryHandler` applies a per-attempt timeout and exponential backoff with jitter. `429` is
retried for every verb (Maxio's `Retry-After` is honoured); `5xx` and network faults are retried
only for reads, because a `POST` that failed *after* reaching Maxio may already have created a
record - those are reconciled by the reference lookups above instead.

If the `Maxio` section is missing or invalid the host still starts and logs a warning at startup;
only the three subscription endpoints fail, with `503`.

## Tests

| Project | What it covers |
| --- | --- |
| `tests/UnitTests/Maxio` | Options/URL templating, the typed client (URLs, Basic auth, request bodies, error models), the retry policy, the idempotency logic, and spec conformance |
| `tests/PublicApiIntegrationTests/SubscriptionEndpoints` | The three endpoints end to end (auth, status codes, idempotency including 5 concurrent subscribes, per-shopper isolation, unconfigured → `503`) against an in-memory Maxio stand-in |

```powershell
dotnet test tests/UnitTests/UnitTests.csproj --filter FullyQualifiedName~Maxio
dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj --filter FullyQualifiedName~Subscription
```

## Verifying against the sandbox

See the "Verify the Maxio subscription flow" section of the [root README](../README.md#verify-the-maxio-subscription-flow).
