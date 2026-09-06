# Subscription billing (Maxio Advanced Billing)

eShopOnWeb's original flow is one-time commerce: Catalog → Basket → Order. This adds a second,
**parallel** capability — recurring subscriptions — with
[Maxio Advanced Billing](https://developers.maxio.com/http/advanced-billing-api) as the billing
system of record. Nothing in the cart or checkout path changed.

## The endpoints

All three live on **`src/PublicApi`**, are JWT-authenticated, and take the shopper's identity from
the token (never from the request body).

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/subscription-plans` | Plans available in the configured product family. |
| `POST` | `/api/subscriptions` | Subscribe the caller to a plan. Idempotent. |
| `GET` | `/api/my-subscriptions` | The caller's subscriptions, live ones first. |

`POST /api/subscriptions` body:

```json
{ "planHandle": "eshop-pro", "idempotencyKey": "optional" }
```

`idempotencyKey` may also be sent as an `Idempotency-Key` header. It answers `201` when the call
created the subscription and `200` when it resolved to one that already existed; either way the
body carries the confirmed plan, price, state and next billing date, plus an `outcome` of
`created`, `alreadySubscribed` or `idempotentReplay`.

## Configuration

Bound from the `Maxio:` section. **Never commit the values** — use user-secrets locally or
`Maxio__*` environment variables / a secret store elsewhere.

| Key | Required | Meaning |
|---|---|---|
| `Maxio:ApiKey` | yes | Advanced Billing API key. Sent as the HTTP Basic user name with the literal password `x`, which is the scheme the API defines. |
| `Maxio:Subdomain` | yes¹ | Site subdomain. The US base address `https://{subdomain}.chargify.com` is derived from it. |
| `Maxio:ProductFamilyHandle` | yes | Handle of the product family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | Used **verbatim** as the API base address when set, instead of deriving one. This is how you reach the EU host (`https://{subdomain}.ebilling.maxio.com`) or a proxy. |
| `Maxio:PaymentCollectionMethod` | no | Overrides the collection method used at signup. See below. |
| `Maxio:TimeoutSeconds` | no | Per-request timeout. Default 30. |
| `Maxio:MaxRetryAttempts` | no | Retries for throttled/transient calls. Default 3. |
| `Maxio:RetryBaseDelayMilliseconds` | no | Backoff base. Default 500. |
| `Maxio:PlanCacheSeconds` | no | In-memory plan catalog TTL. Default 60. |

¹ Either `Maxio:Subdomain` or `Maxio:BaseUrl` must be set.

Load the sandbox credentials into user-secrets (values come from the environment, so nothing lands
in a file in the repo):

```bash
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"              --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"       --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

Nothing is hard-coded to one site or catalog: rebinding the section is all it takes to point the
same build at a different Maxio site with a different product family.

**If the section is unbound the host still starts.** It logs a warning naming the missing keys and
the three endpoints answer `503` with the same list, rather than the process refusing to boot or
the endpoints failing as `500`s.

## Design

### Maxio is the only store

There is no local table mapping eShopOnWeb users to subscriptions. The link is the **customer
reference**, derived deterministically from the user name:

```
eshop-{slug of user name}-{first 8 hex of sha256(user name)}
```

The slug keeps it legible in the Maxio UI, the hash keeps it unambiguous (slugging alone maps
`a@b.com` and `a-b.com` onto the same string). Because the reference is a pure function of the user
name, restarting the API — even on the in-memory database, which loses everything — still finds
the same Maxio customer and the same subscriptions.

### Idempotency

A shopper double-clicking Subscribe must not end up with two customers or two subscriptions. Three
mechanisms stack up, weakest and cheapest first:

1. **A per-shopper in-process lock** serialises concurrent subscribes so the second one waits
   rather than racing.
2. **A pre-check** reads the shopper's subscriptions and returns the existing one — `200`,
   `alreadySubscribed` — if they already hold a live subscription to that plan. "Live" is
   everything that is not `canceled`, `expired`, `failed_to_create`, `unpaid` or `trial_ended`; a
   `past_due` shopper is still enrolled and must not be signed up twice.
3. **A deterministic subscription reference**, which Maxio enforces as unique per site, is the
   backstop that also holds across processes. On the `422` that a duplicate reference produces, the
   subscription that owns the reference is read back and returned as `idempotentReplay`.

The same `422`-then-read-back handles a raced customer create.

The reference is `sub-{customer reference}-{key}`, where the key is the caller's `idempotencyKey`
or, when they gave none, the plan handle. Using the plan handle is what makes a plain double-click
safe with no client cooperation.

**Re-subscribing after cancelling** is a real action, not a duplicate. When the derived reference
turns out to belong to a subscription that has *ended*, the call moves to
`sub-{customer}-{plan}-r{id of the ended subscription}` and creates a genuinely new subscription —
still deterministic, so a double-click on *that* also collapses to one. A caller who pinned an
explicit `idempotencyKey` gets strict replay instead: the same key always returns the same
subscription.

### Payment collection

Both demo plans are configured with no payment method required, but on a Relationship Invoicing
site the *site's* default collection method is `automatic`, which makes Maxio try to charge at
signup and reject it with *"No payment method was on file"*. So the integration reads
`GET /site.json` once (cached) and enrols with `remittance` on Relationship Invoicing sites and
`invoice` on legacy ones — the methods that let a signup complete with no card capture and no 3-DS.
Set `Maxio:PaymentCollectionMethod` to take that decision yourself.

### Resilience

`MaxioRetryHandler` retries deliberately asymmetrically. Reads are replayed on `429`, 5xx and
connection failures. **Writes are replayed only on `429`**, where Maxio has told us it did not
process the request — a 5xx or a dropped connection on `POST /subscriptions.json` may well have
created the subscription, and re-sending it blindly is how a shopper gets billed twice. Recovery
from those goes through the reference instead. Backoff is exponential with jitter and honours
`Retry-After` when present.

### Errors

| Situation | Status |
|---|---|
| `planHandle` missing | `400` |
| No/invalid bearer token | `401` |
| Plan not in the configured family | `404` |
| Maxio rejected the request | `422`, with Maxio's own message |
| Maxio unreachable, timed out, or answered unusably | `502` |
| `Maxio:` section unbound | `503`, naming the missing keys |

## Where the code lives

| Path | What |
|---|---|
| `src/ApplicationCore/Subscriptions/` | Domain models: plan, subscription, command, result, state classification. |
| `src/ApplicationCore/Interfaces/ISubscriptionBillingService.cs` | The capability, stated without Maxio in it. |
| `src/ApplicationCore/Exceptions/Billing*.cs` | Billing failure taxonomy. |
| `src/Infrastructure/Billing/Maxio/` | Settings, reference derivation, typed HTTP client, retry handler, the service. |
| `src/Infrastructure/Billing/Maxio/Contracts/` | Maxio wire contracts. |
| `src/PublicApi/SubscriptionEndpoints/` | The three endpoints and their DTOs. |
| `tests/UnitTests/Infrastructure/Billing/Maxio/` | Idempotency, plan resolution, collection method, mapping. |
| `tests/PublicApiIntegrationTests/SubscriptionEndpoints/` | Auth and unconfigured-host behaviour. |

## Maxio endpoints used

Every one was confirmed against Maxio's published API surface and then against a live sandbox
before being coded against.

| Call | Used for |
|---|---|
| `GET /product_families/handle:{handle}/products.json` | The plan catalog. The `handle:` prefix is what keeps this working across sites, where numeric ids differ but handles do not. |
| `GET /customers/lookup.json?reference=` | Find the shopper's customer (`404` when absent). |
| `POST /customers.json` | Create it. |
| `GET /customers/{id}/subscriptions.json` | The shopper's subscriptions. |
| `POST /subscriptions.json` | Enrol. |
| `GET /subscriptions/lookup.json?reference=` | Resolve a duplicate reference back to its owner. |
| `GET /site.json` | Decide the collection method. |

Authentication is HTTP Basic with the API key as user name and the literal `x` as password.
