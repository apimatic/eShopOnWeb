# Recurring subscriptions (Maxio Advanced Billing)

eShopOnWeb's original flow is one-time commerce: Catalog → Basket → Order. This adds a **parallel,
additive** capability — recurring subscription billing — exposed as three JWT-authenticated
endpoints on `src/PublicApi`. Nothing in the existing cart or checkout path changes.

[Maxio Advanced Billing](https://developers.maxio.com/) is the **system of record**. Plans,
customers and subscriptions live there and are read live on every request; this application stores
none of it. That is deliberate, and it is what lets the integration work correctly on a host running
with `UseOnlyInMemoryDatabase=true`, where local data does not survive a restart.

## Endpoints

| Method | Route | Purpose |
|--------|-------|---------|
| `GET`  | `/api/subscription-plans` | Plans published by the configured product family. |
| `POST` | `/api/subscriptions`      | Subscribe the caller to a plan. Idempotent. |
| `GET`  | `/api/my-subscriptions`   | The caller's own subscriptions, newest first. |

All three require a bearer token from `POST /api/authenticate`. **Who is billed comes from the token
only** — no endpoint accepts a customer identifier from the client, so a caller can never read or
write another shopper's billing records.

### `POST /api/subscriptions`

```jsonc
// request
{ "planHandle": "eshop-pro" }        // required; no default plan, the catalog differs per deployment

// optional: Idempotency-Key: <your key>   (or "idempotencyKey" in the body, for clients that
//                                          cannot set headers; the header wins)
```

Answers **201** with `"created": true` when this call enrolled the shopper, and **200** with
`"created": false` when it did not — a replay, a double click, or a shopper already on the plan.
The body carries the plan, price, state and next billing date confirmed back from Maxio.

## Configuration

Bound from the `Maxio` section. Supply values through user-secrets in development and the platform's
secret store in production — **never** in a file in this repository.

| Key | Required | Meaning |
|-----|----------|---------|
| `Maxio:ApiKey` | yes | API key. Sent as HTTP Basic user name with the literal password `x`. |
| `Maxio:Subdomain` | yes¹ | Site subdomain; the base address becomes `https://{subdomain}.chargify.com/`. |
| `Maxio:ProductFamilyHandle` | yes | Handle of the product family whose products are sold as plans. |
| `Maxio:BaseUrl` | no | Used **verbatim** as the API base address when set, instead of deriving one from the subdomain. |
| `Maxio:PaymentCollectionMethod` | no | Defaults to `remittance`. Set to `automatic` on sites that capture a card up front. |
| `Maxio:Timeout` | no | HTTP timeout. Defaults to 30 seconds. |
| `Maxio:MaxAttempts` | no | Attempts per call, including the first. Defaults to 3. |

¹ `Maxio:BaseUrl` alone is sufficient; the subdomain is only needed to derive an address.

Loading the sandbox credentials from the environment into user-secrets:

```bash
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"              --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"       --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

A deployment with no billing configuration still starts and still serves catalog, basket and order
traffic; only the subscription endpoints answer **503**, naming exactly which keys are missing.

## How idempotency works

Maxio does not deduplicate writes, so a double-clicked Subscribe button would happily create two
subscriptions. Three mechanisms stop that, in order of how often they fire:

1. **Adopt what already exists.** Before creating anything, the caller's live subscriptions are read
   from Maxio and matched against the requested plan. A shopper already on the plan gets that
   subscription back with `created: false`. `past_due`, `on_hold` and `suspended` all count as
   already-held — only `canceled`, `expired`, `trial_ended` and `failed_to_create` do not.
2. **Serialise per shopper.** An in-process gate keyed by the shopper's billing reference collapses
   simultaneous requests on one instance into one Maxio write.
3. **Let Maxio settle the race.** Every record this integration writes carries a deterministic
   `reference`, and Maxio enforces reference uniqueness site-wide. A request that loses a race — to
   another instance, or to a retry of itself — is rejected with `422 Reference: must be unique` and
   adopts the record that won instead of creating a second one.

Only the third mechanism is a guarantee; the first two exist so the common case is cheap and quiet.
It is also what makes retrying a `POST` safe, which is why the transient-fault handler retries
writes at all.

### The references

```
customer      eshop-{login-slug}-{hash8}                e.g. eshop-demouser-microsoft-com-03563e80
subscription  {customer-reference}-{plan-slug}[-{n}]    e.g. eshop-demouser-…-03563e80-eshop-pro
              {customer-reference}-{plan-slug}-{hash8}  when an Idempotency-Key is supplied
```

The customer reference is derived from the login name alone — not from a local primary key — so the
same shopper resolves to the same Maxio customer across restarts and across instances, with no
local mapping table to keep in sync. The slug is only there to make records recognisable in the
Maxio UI; the trailing hash of the full login is what guarantees two shoppers cannot collide.

The `-{n}` ordinal is the count of the shopper's existing subscriptions to that plan, so a shopper
who cancels and resubscribes gets a fresh reference instead of colliding with their retired one.
Because it is computed from live Maxio state, concurrent callers derive the same value and the
uniqueness constraint decides between them.

## Layout

```
src/ApplicationCore/Subscriptions/      plan, subscription and subscriber models; reference derivation
src/ApplicationCore/Interfaces/         ISubscriptionBillingService — provider-agnostic contract
src/Infrastructure/Maxio/               typed Maxio client, wire contracts, retries, orchestration
src/PublicApi/SubscriptionEndpoints/    the three HTTP endpoints and their DTOs
```

`ApplicationCore` knows nothing about Maxio: swapping billing providers means writing one more
implementation of `ISubscriptionBillingService`.

## Maxio API surface used

Every shape below was confirmed against a live Maxio sandbox before being coded against.

| Call | Used for |
|------|----------|
| `GET /site.json` | Site currency for plan prices. |
| `GET /product_families/handle:{handle}/products.json` | Publishing plans. Addressing the family by handle is what keeps this working after a catalog re-seed reassigns numeric ids. |
| `GET /customers/lookup.json?reference=` | Finding the shopper's billing customer. 404 means "none yet". |
| `POST /customers.json` | Creating it on first subscribe. |
| `GET /customers/{id}/subscriptions.json` | Reading the shopper's subscriptions. |
| `POST /subscriptions.json` | Enrolling. |
| `GET /subscriptions/lookup.json?reference=` | Adopting the winner after losing a reference race. |

Authentication is HTTP Basic with the API key as user name and the literal `x` as password.
List endpoints are paged with `page` and `per_page`, and are walked to exhaustion.

`payment_collection_method` is sent as `remittance` by default. Without it, Maxio applies the site's
default collection method — `automatic` on a typical site — and rejects the subscription with
*"No payment method was on file"* even for plans that do not require a credit card. Remittance bills
the customer by invoice instead, which is what lets a shopper enrol with no card capture and no 3-DS.
