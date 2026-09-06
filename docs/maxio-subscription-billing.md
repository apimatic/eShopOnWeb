# Subscription billing with Maxio Advanced Billing

eShopOnWeb is one-time commerce: catalog → basket → order. This adds a **second, parallel**
capability — recurring subscriptions — with **Maxio Advanced Billing** as the system of
record. Nothing in the existing cart/checkout path changes.

## The endpoints

All three live on `src/PublicApi` and require a JWT bearer token from `POST /api/authenticate`.
The shopper is always taken from the token; no endpoint accepts a user name in the request.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | Plans on offer, from the configured Maxio product family. |
| `POST /api/subscriptions` | Subscribe the caller to a plan. Idempotent. |
| `GET /api/my-subscriptions` | The caller's subscriptions, newest first. |

### `POST /api/subscriptions`

```json
{
  "planHandle": "eshop-pro",
  "firstName": "Ada",
  "lastName": "Lovelace",
  "idempotencyKey": "checkout-9f2c"
}
```

Only `planHandle` is required. `firstName`/`lastName` are used only if a Maxio customer has
to be created for this shopper; when they are omitted, non-blank names are derived from the
user name (Maxio rejects a customer with a blank name).

- `201 Created` — the subscription was created.
- `200 OK` with `"alreadySubscribed": true` — the shopper already held this subscription.
- `400` invalid body · `401` no/!valid token · `404` unknown plan ·
  `422` the plan requires a stored payment method · `502`/`503`/`504` billing system problem.

## Configuration

Bound from the `Maxio` configuration section. Nothing is hard-coded, so the same build runs
against any Maxio site and catalog.

| Key | Source env var | Required | Notes |
| --- | --- | --- | --- |
| `Maxio:ApiKey` | `MAXIO_API_KEY` | yes | Sent as HTTP basic auth user name, password `x`. |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | unless `BaseUrl` is set | Derives `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | yes | Only this family's products are offered as plans. |
| `Maxio:BaseUrl` | — | no | When set, used **verbatim** as the API base address. |
| `Maxio:TimeoutSeconds` | — | no | Default 30. |
| `Maxio:MaxRetryAttempts` | — | no | Default 3. |
| `Maxio:RetryBaseDelayMilliseconds` | — | no | Default 200. |

Secrets never belong in the repository. Load them into user-secrets:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

Environment configuration (`Maxio__ApiKey=…`) works too, which is how you would supply these
outside a developer machine.

Configuration is validated the first time the billing gateway is resolved, not at startup:
an eShopOnWeb deployment with no Maxio settings still serves the rest of the API, and the
three subscription endpoints answer `503` with a message naming the missing keys.

## How it is put together

```
PublicApi/SubscriptionEndpoints      HTTP surface: auth, DTOs, status codes
        │
ApplicationCore/Services             SubscriptionService — the subscribe flow and its
        │                            idempotency rules. Knows nothing about HTTP.
        │  IBillingGateway (port)
        ▼
Infrastructure/Billing/Maxio         MaxioBillingGateway — typed HttpClient, retry handler,
                                     wire contracts, options and validation.
```

The **eShop user ↔ Maxio customer mapping lives in Maxio**, as the customer's unique
`reference` (`eshop:{username}`). There is no new table and no new migration, which also
means the mapping survives a restart even though this environment runs on the EF in-memory
provider (which loses everything on restart). Maxio stays the single source of truth for who
is subscribed to what.

### Why plain HTTP instead of the official SDK

Maxio publishes an official .NET SDK (`Maxio.AdvancedBillingSdk`). It was used as the
*contract source* — every path, request body and response field below was read out of
[`maxio-com/ab-dotnet-sdk`](https://github.com/maxio-com/ab-dotnet-sdk) — but not as the
runtime dependency, because the SDK builds its base address from an environment enum plus a
site name and offers no way to point it at an arbitrary URL. `Maxio:BaseUrl` has to be
honoured verbatim, so a typed `HttpClient` is the straightforward implementation.

### Endpoints this integration uses

Confirmed against the official SDK's controllers and then exercised against a live Advanced
Billing sandbox site:

| Call | Used for |
| --- | --- |
| `GET /product_families/{product_family_id}/products.json` | Listing plans. The family is addressed as `handle:{handle}`, since Maxio reassigns numeric ids when a catalog is re-seeded. |
| `GET /site.json` | Site currency — products do not carry one. Best-effort; a failure only drops the currency. |
| `GET /customers/lookup.json?reference=…` | Exact-match customer lookup; `404` when absent. |
| `POST /customers.json` | Creating the customer. Body `{"customer": {...}}`. `first_name`/`last_name` are mandatory. |
| `GET /customers/{customer_id}/subscriptions.json` | The shopper's subscriptions. |
| `POST /subscriptions.json` | Subscribing. Body `{"subscription": {...}}`. |
| `GET /subscriptions/lookup.json?reference=…` | Resolving the winner of an idempotency race. |

Authentication is HTTP basic with the API key as the user name and the literal `x` as the
password.

### Why `payment_collection_method: "remittance"`

The seeded plans do not require a payment method, so no payment profile exists for the
shopper. Maxio still tries to settle the first period at signup, and an `automatic`
subscription with no card on file is rejected with *"No payment method was on file for the
$299.00 balance"*. `remittance` invoices the customer instead, which is the collection method
that matches a plan billed without stored card details.

Plans that *do* require a payment method are refused with `422`: capturing card details needs
Chargify.js and the 3-DS post-authentication flow, which this integration deliberately does
not implement rather than half-implement.

### Idempotency

"A double-click never creates two customers or two subscriptions" is enforced in four layers,
because no single one covers every retry shape:

1. **An in-process lock keyed on the shopper** serialises concurrent requests on one instance.
2. **The customer is looked up by reference before it is created**, and if a concurrent
   request wins the create race (Maxio answers `422 Reference: must be unique`), the winner is
   re-read and reused.
3. **An existing live subscription to the same plan is returned as-is.** "Live" means any
   state other than `canceled`, `expired` or `failed_to_create` — so a state Maxio adds in
   future fails safe, and a shopper whose subscription was cancelled can still re-subscribe.
4. **A caller-supplied `idempotencyKey` becomes the Maxio subscription `reference`**
   (`eshop-sub-{sha256}`), which Maxio enforces as unique site-wide. This is the only layer
   that holds when two *application instances* race, since it is enforced by Maxio itself.

### Reliability

- Transient failures are retried with exponential backoff and jitter, honouring `Retry-After`.
- Only requests that are safe to repeat are retried: reads on connection failures and
  `429`/`5xx`, writes on `429` alone — a `5xx` or dropped connection on a `POST` could mean
  the record *was* created, and re-sending would duplicate it.
- Failures map to honest status codes: `504` when Maxio is unreachable or times out, `503`
  when it throttles us or the integration is misconfigured, `502` for anything else it
  rejects. The API key never appears in a log or a response.

## Tests

| Where | What |
| --- | --- |
| `tests/UnitTests/ApplicationCore/Services/SubscriptionServiceTests` | The subscribe flow: customer reuse, the double-click, 16-way concurrency, both lost-race recoveries, re-subscribe after cancellation, unknown plan, payment-method-required. |
| `tests/UnitTests/Infrastructure/Billing/Maxio` | Pins the wire contract — paths, request bodies, response mapping, error shapes, `BaseUrl` override — against canned sandbox payloads. |
| `tests/PublicApiIntegrationTests/SubscriptionEndpoints` | The HTTP surface end to end against a stub gateway: auth, status codes, response shapes. Hermetic; never calls a real Maxio site. |
