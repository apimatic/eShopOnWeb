# Recurring subscriptions with Maxio Advanced Billing

eShopOnWeb's cart/checkout flow is one-time commerce. This document describes the **parallel**
recurring-subscription capability: a logged-in shopper browses plans, subscribes to one, and sees it
reflected in their account. **Maxio Advanced Billing is the system of record** - no subscription,
plan or customer state is persisted in the eShopOnWeb databases.

The Maxio OpenAPI specification in [`maxio-spec/`](../maxio-spec/openapi.yaml) is the contract. Every
path, query parameter, request envelope, response envelope, auth detail and error model used here
comes from that file.

---

## Endpoints

All three are on `src/PublicApi` and require a JWT bearer token from `POST /api/authenticate`. The
shopper is always identified by the token; there is no way to act on somebody else's behalf.

| Route | Purpose |
|---|---|
| `GET /api/subscription-plans` | Plans available for signup, cheapest first. |
| `POST /api/subscriptions` | The hero flow: ensure a billing customer exists, then enroll the shopper. |
| `GET /api/my-subscriptions` | The shopper's billing customer and all of their subscriptions. |

### `POST /api/subscriptions`

```jsonc
{
  "planHandle": "eshop-pro",        // optional if Maxio:DefaultPlanHandle is configured
  "idempotencyKey": "checkout-123", // optional; see "Idempotency" below
  "firstName": "Ada",               // optional; defaults are derived from the shopper's email
  "lastName": "Lovelace",           // optional
  "organization": "Analytical Ltd"  // optional
}
```

Answers **`201 Created`** with `created: true` when it enrolled the shopper, and **`200 OK`** with
`created: false` when an equivalent subscription already existed. The body carries the plan, price,
state and next billing date:

```jsonc
{
  "created": true,
  "subscription": {
    "id": 94213499,
    "state": "active",
    "isLive": true,
    "reference": "eshop:sub:demouser@microsoft.com:eshop-pro",
    "planHandle": "eshop-pro",
    "planName": "Pro Plan",
    "priceInCents": 29900,
    "price": 299,
    "currency": "USD",
    "nextBillingAt": "2026-10-06T23:56:50+05:00",
    "currentPeriodStartedAt": "2026-09-06T23:56:50+05:00",
    "currentPeriodEndsAt": "2026-10-06T23:56:50+05:00",
    "paymentCollectionMethod": "remittance",
    "customer": { "id": 98841382, "reference": "eshop:demouser@microsoft.com", "email": "demouser@microsoft.com" }
  }
}
```

---

## Configuration

Bound from the `Maxio` section into
[`MaxioOptions`](../src/Infrastructure/Maxio/MaxioOptions.cs). **No value is hard-coded**: the same
build runs against any Maxio site and catalog.

| Key | Required | Source in this environment | Notes |
|---|---|---|---|
| `Maxio:ApiKey` | yes | `MAXIO_API_KEY` | HTTP Basic username; the password is the literal `x`, per the spec's `BasicAuth` scheme. |
| `Maxio:Subdomain` | yes* | `MAXIO_SITE_SUBDOMAIN` | Substituted into the server template. |
| `Maxio:ProductFamilyHandle` | yes | `MAXIO_DEFAULT_PRODUCT_FAMILY` | The family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | - | Override. When set it is used **verbatim** and neither `Subdomain` nor `Environment` participates. |
| `Maxio:Environment` | no | `MAXIO_ENVIRONMENT` | `US` (default) or `EU`. Selects the server template. |
| `Maxio:DefaultPlanHandle` | no | - | Plan used when a subscribe request names none. |
| `Maxio:PaymentCollectionMethod` | no | - | Pins the collection method; see below. |
| `Maxio:ReferencePrefix` | no | - | Namespace for the references written to Maxio. Default `eshop`. |
| `Maxio:TimeoutSeconds`, `Maxio:MaxRetryAttempts`, `Maxio:RetryBaseDelayMilliseconds`, `Maxio:SiteCacheSeconds` | no | - | Transport tuning. |

\* Required unless `Maxio:BaseUrl` is set.

**Secrets never enter the repository.** Load the credentials into .NET user-secrets from the
environment:

```bash
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"              --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"       --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
dotnet user-secrets set "Maxio:Environment"         "$MAXIO_ENVIRONMENT"          --project src/PublicApi
```

Environment variables (`Maxio__ApiKey`, ...) work equally well.

If the section is incomplete the app still starts - it logs a warning naming the missing keys, the
catalog endpoints keep working, and the three subscription routes answer `503 Service Unavailable`
with the same detail.

---

## Where the code lives

Clean Architecture, matching the rest of the solution:

```
src/ApplicationCore/Entities/SubscriptionAggregate/   domain models (not EF-mapped; Maxio owns the state)
src/ApplicationCore/Interfaces/IBillingGateway.cs     port onto the billing provider
src/ApplicationCore/Interfaces/ISubscriptionService.cs
src/ApplicationCore/Services/SubscriptionService.cs   orchestration + all duplicate-suppression policy
src/ApplicationCore/Services/KeyedAsyncLock.cs        per-shopper serialization
src/ApplicationCore/Exceptions/Billing*.cs            failure taxonomy

src/Infrastructure/Maxio/Contracts/                   wire contracts, one per spec schema
src/Infrastructure/Maxio/MaxioApiClient.cs            typed HttpClient, one member per spec operation
src/Infrastructure/Maxio/MaxioRetryHandler.cs         retry/backoff policy
src/Infrastructure/Maxio/MaxioBillingGateway.cs       adapter: wire contracts <-> domain
src/Infrastructure/Maxio/MaxioOptions.cs              settings, server templating, validation
src/Infrastructure/Maxio/MaxioBillingServiceCollectionExtensions.cs

src/PublicApi/SubscriptionEndpoints/                  the three HTTP endpoints and their DTOs
```

`ApplicationCore` knows nothing about Maxio; swapping the billing provider means writing another
`IBillingGateway`.

### Spec provenance

Every outbound call maps to exactly one operation in the specification, and the client members are
named after the `operationId`:

| Used for | `operationId` | Path |
|---|---|---|
| Listing plans | `listProductsForProductFamily` | `GET /product_families/{product_family_id}/products.json` |
| Site currency and architecture | `readSite` | `GET /site.json` |
| Finding the shopper's customer | `readCustomerByReference` | `GET /customers/lookup.json` |
| Creating the shopper's customer | `createCustomer` | `POST /customers.json` |
| Listing the shopper's subscriptions | `listCustomerSubscriptions` | `GET /customers/{customer_id}/subscriptions.json` |
| Recognising a replayed signup | `findSubscription` | `GET /subscriptions/lookup.json` |
| Enrolling the shopper | `createSubscription` | `POST /subscriptions.json` |

Base address follows the spec's `x-server-configuration`: `https://{site}.chargify.com` for `US`,
`https://{site}.ebilling.maxio.com` for `EU`. Auth is the spec's single `BasicAuth` scheme.

---

## Design decisions

### Identity and references, with no local mapping table

Maxio is the system of record, so there is no `userId -> subscriptionId` table to keep in sync - and
none to lose when the app runs on the in-memory provider. The link is a **reference stored on Maxio**:

* customer: `eshop:{userName}`
* subscription: `eshop:sub:{userName}:{idempotencyKey}`

The user name (the shopper's sign-in email) is used rather than the identity row's id, precisely
because the in-memory identity store mints fresh ids on every restart. Both reference formats are
owned by the adapter; the domain never sees them.

### Idempotency - a double-click cannot enroll a shopper twice

Four layers, in order:

1. **Per-shopper lock.** Concurrent subscribes for the same shopper and key are serialized in-process
   (`KeyedAsyncLock`), so they cannot all observe "not subscribed yet" and race.
2. **Customer ensure is idempotent.** Look up by reference first; if the create is rejected anyway
   (Maxio enforces one customer per reference), re-read - a lost race yields the same customer.
3. **Reference lookup.** `findSubscription` on the derived reference. With an explicit
   `idempotencyKey` this is an exact replay and returns whatever that call produced. Without one,
   only a still-live subscription counts - a shopper whose subscription was canceled may subscribe
   again, and the new signup gets a fresh reference so it does not collide with their history.
4. **"Already subscribed?" check.** The shopper's live subscriptions are scanned for the same plan.
   This catches duplicates that slipped past the reference (for example a different idempotency key)
   and is the semantically meaningful guard.

Verified end to end: eight simultaneous subscribe calls produce one `201` and seven `200`s, all
naming the same subscription id.

The lock is a single-process guard. Behind a load balancer, steps 2-4 still hold - they are checks
against the system of record - but two instances racing inside the same few hundred milliseconds
could each create a subscription. The spec offers no idempotency key on `createSubscription`, so
closing that window fully would need a distributed lock, which this deployment has no infrastructure
for.

### Collection method: signups are invoiced, not charged

eShopOnWeb captures no card details - there is no Chargify.js integration and no PCI flow. With
`automatic` collection Maxio rejects the signup outright ("No payment method was on file for the
$299.00 balance"), even for a product with `require_credit_card: false`, because the first period's
balance falls due immediately.

So the adapter sets `payment_collection_method` to an invoicing method, choosing the one valid for
the site's architecture (read from `readSite`): `remittance` under Relationship Invoicing,
`invoice` on legacy Statements sites. Both values are in the spec's `Collection-Method` enum. Set
`Maxio:PaymentCollectionMethod` to override - for instance `automatic` on a deployment that does
capture payment methods.

### Retries

`MaxioRetryHandler` retries with exponential backoff and jitter, honouring `Retry-After`:

* **Reads** are retried on transport faults and `5xx`.
* **Writes are not** - a `POST /subscriptions.json` that timed out may well have succeeded.
* **`429`** is retried for every method, since a throttled request was by definition not processed.

Request bodies are serialized once into rewindable content so a retried write resends byte-for-byte.

### Error mapping

| Situation | Status | Body |
|---|---|---|
| Maxio not configured, or it rejected the credentials | `503` | which settings are missing |
| Unknown plan handle, or none given and no default | `400` | the handle and product family |
| Maxio rejected the request (`4xx`, e.g. `422`) | `400` | Maxio's own `errors` messages, verbatim |
| Maxio unavailable or erroring (`5xx`, timeouts) | `502` | what failed |

Maxio's two documented error shapes are both parsed: the `errors` array
(`Error-List-Response.yaml`) and the keyed object (`Customer-Error-Response.yaml`).

The API key is never logged. Request logging records the `operationId`, method, path and status only.

---

## Verifying it works

Prerequisites: the .NET SDK, a trusted HTTPS dev certificate (`dotnet dev-certs https --check`) and
the four user-secrets above. No database, Docker or broker is needed.

```bash
# 1. Build (global.json rolls the pinned 8.0.x SDK forward to whatever major is installed)
DOTNET_ROLL_FORWARD=Major dotnet build eShopOnWeb.sln

# 2. Tests
DOTNET_ROLL_FORWARD=Major dotnet test eShopOnWeb.sln

# 3. Run PublicApi against the in-memory stores
DOTNET_ROLL_FORWARD=Major UseOnlyInMemoryDatabase=true ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/PublicApi --no-launch-profile --urls "https://localhost:27243;http://localhost:27244"
```

Startup logs `Maxio subscription billing enabled: base address ..., product family '...'` once the
section is complete.

```bash
# 4. Get a bearer token (the storefront cookie will not work here)
TOKEN=$(curl -sk -X POST https://localhost:27243/api/authenticate -H 'Content-Type: application/json' -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | grep -o '"token":"[^"]*' | cut -d'"' -f4)

# 5. Browse plans -> 200, both seeded plans, cheapest first
curl -sk -H "Authorization: Bearer $TOKEN" https://localhost:27243/api/subscription-plans

# 6. Before subscribing -> 200 with {"customer":null,"subscriptions":[],"activeCount":0}
curl -sk -H "Authorization: Bearer $TOKEN" https://localhost:27243/api/my-subscriptions

# 7. Subscribe -> 201, created:true, state "active", plus price and next billing date
curl -sk -i -X POST https://localhost:27243/api/subscriptions -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"planHandle":"eshop-pro"}'

# 8. Subscribe again -> 200, created:false, the same subscription id
curl -sk -i -X POST https://localhost:27243/api/subscriptions -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"planHandle":"eshop-pro"}'

# 9. Reflected in the account -> the subscription, activeCount 1
curl -sk -H "Authorization: Bearer $TOKEN" https://localhost:27243/api/my-subscriptions
```

Worth trying as well:

```bash
# No token -> 401
curl -sk -o /dev/null -w 'status %{http_code}\n' https://localhost:27243/api/subscription-plans

# Unknown plan -> 400 naming the handle and the product family
curl -sk -X POST https://localhost:27243/api/subscriptions -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"planHandle":"does-not-exist"}'

# Double-click: one 201, the rest 200, all naming the same subscription id
for i in 1 2 3 4 5 6; do
  curl -sk -o /dev/null -w '%{http_code} ' -X POST https://localhost:27243/api/subscriptions -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"planHandle":"basic-plan"}' &
done; wait; echo
```

Everything is also browsable at `https://localhost:27243/swagger` under the
**SubscriptionEndpoints** tag, and each subscription shows up in the Maxio UI for the configured
site.

---

## Not included

* **Cancel, upgrade/downgrade, and payment-method capture.** The brief is the subscribe flow; the
  spec covers these operations if they are wanted next.
* **Usage reporting for the metered `api-call` component.** Seeded on the sandbox family, but not
  part of the subscribe flow.
* **Webhooks.** State is read from Maxio on demand rather than mirrored locally, so there is nothing
  to keep in sync.
* **Storefront UI.** The capability is exposed as JWT-authenticated `PublicApi` endpoints, as
  specified. The cookie-authenticated `Web` storefront is untouched.
