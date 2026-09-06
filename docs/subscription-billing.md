# Subscription billing (Maxio Advanced Billing)

eShopOnWeb ships with one-time commerce: Catalog → Basket → Order. This adds a **parallel,
additive** capability — recurring subscriptions — with **Maxio Advanced Billing as the billing
system of record**. Nothing about the cart or checkout flow changes.

The hero flow is *Subscribe*: a logged-in shopper browses plans, subscribes to one, and sees it
reflected in their account.

---

## Endpoints

All three live on `src/PublicApi`, follow that project's `IEndpoint` conventions, and require a
JWT bearer token. **The shopper is taken from the token only** — no request names a subscriber, so
one caller can never read or alter another's subscriptions.

### `GET /api/subscription-plans`

Plans on offer, read live from the configured Maxio product family.

```json
{
  "subscriptionPlans": [
    {
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "price": 299.00,
      "currency": "USD",
      "interval": 1,
      "intervalUnit": "month",
      "billingPeriod": "299.00 USD per month",
      "requiresPaymentMethod": false,
      "productFamilyHandle": "eshop-subscribe",
      "trial": null
    }
  ]
}
```

### `POST /api/subscriptions`

```json
{ "planHandle": "eshop-pro" }
```

Optional: `firstName` / `lastName` (used only when the Maxio customer is first created) and
`idempotencyKey` (or the `Idempotency-Key` header).

Answers **201** when it enrolled the shopper and **200** when they were already on that plan. The
body is the same either way, plus `created` and a confirmation `message`:

```json
{
  "subscription": {
    "id": "94208944",
    "state": "Active",
    "providerState": "active",
    "planHandle": "eshop-pro",
    "planName": "Pro Plan",
    "price": 299.00,
    "currency": "USD",
    "billingPeriod": "299.00 USD per month",
    "nextBillingAt": "2026-10-06T13:00:28+05:00",
    "customerId": "98837622",
    "customerReference": "eshoponweb:demouser@microsoft.com",
    "reference": "eshoponweb:demouser@microsoft.com:eshop-pro:1"
  },
  "created": true,
  "message": "Subscribed to Pro Plan at 299.00 USD per month. Status: active. Next billing date: 2026-10-06."
}
```

### `GET /api/my-subscriptions`

The caller's subscriptions, newest first, plus an `activeCount`.

### Status codes

| Code | When |
|------|------|
| `200` | Read succeeded, or subscribe was an idempotent no-op |
| `201` | Subscribe enrolled the shopper |
| `400` | `planHandle` missing |
| `401` | No/invalid bearer token, or the token names a user that no longer exists |
| `404` | The plan handle is not offered by the configured product family |
| `409` | Maxio recognised a duplicate request that could not be reconciled — re-read, do not retry |
| `422` | Maxio rejected the request (e.g. the plan needs a stored payment method) |
| `502` | Maxio was unreachable or returned an unusable response |
| `503` | The `Maxio:` configuration section is missing or incomplete on this deployment |

---

## Configuration

Bound from the `Maxio:` section. **No value belongs in the repository.**

| Key | Required | Notes |
|-----|----------|-------|
| `Maxio:ApiKey` | yes | Site API key. Sent as the HTTP Basic username with a literal `X` password. |
| `Maxio:Subdomain` | yes* | Site subdomain; the API base becomes `https://{Subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | Optional override, used **verbatim** as the API base address instead of deriving one from the subdomain. |

\* Either `Maxio:Subdomain` or `Maxio:BaseUrl` must be present.

Optional tuning keys, all with working defaults: `PaymentCollectionMethod`, `TimeoutSeconds`,
`MaxRetryAttempts`, `RetryBaseDelayMilliseconds`, `MaxConcurrentRequests`, `CatalogCacheSeconds`,
`CustomerReferencePrefix`, `ApiHostSuffix`.

### Supplying the values

Development — .NET user-secrets, which live outside the working tree:

```bash
dotnet user-secrets --project src/PublicApi set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets --project src/PublicApi set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets --project src/PublicApi set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

Elsewhere — environment variables or a secret store: `Maxio__ApiKey`, `Maxio__Subdomain`,
`Maxio__ProductFamilyHandle`, `Maxio__BaseUrl`.

Nothing is hard-coded: the same build runs against a different Maxio site and a different catalog
by changing configuration alone. Startup logs whether billing is configured — the base address and
family handle, never the key.

---

## Design

```
PublicApi/SubscriptionEndpoints      HTTP contract, JWT identity, DTOs
        │  ISubscriptionService
ApplicationCore/Subscriptions        Provider-neutral models + billing exceptions
        │
Infrastructure/Billing/Maxio         Maxio implementation: typed client, mappers, resilience
```

`ISubscriptionService` and its models name no vendor, matching how eShopOnWeb already separates
`IEmailSender` from `EmailSender`. Swapping providers means one new implementation in
Infrastructure and one DI line.

### Handles, never ids

Maxio reassigns numeric product ids whenever a catalog is re-seeded, so plans are addressed by
handle end to end — in the API contract, in the plan lookup, and in the create-subscription call.

### Joining an eShopOnWeb user to a Maxio customer

The Maxio customer `reference` is `eshoponweb:{lowercased username}`. Maxio permits at most one
customer per reference, so "ensure a customer exists" is idempotent for free, across instances.

The ASP.NET Identity primary key is deliberately **not** used: under the in-memory database
provider it is regenerated on every restart, which would orphan the billing customer. The username
is stable, and because Maxio holds the mapping, restarts lose nothing.

### Idempotent subscribe

Three independent layers, so a double-clicked button never bills a shopper twice:

1. **Per-shopper in-process lock** — concurrent requests for the same shopper cannot interleave
   the reconcile-then-create sequence.
2. **Reconciliation against Maxio** — if a subscription for that plan is in any state other than
   ended, it is returned as-is with `created: false`. This is the guarantee that holds across
   instances and across restarts, because it is a read of the system of record.
3. **`uniqueness_token` on the create** — Maxio rejects a repeat of the same token inside 60
   minutes with `409`. The token is derived from the customer reference, the plan handle, the
   *generation* (how many subscriptions the shopper has already had on this plan), and any
   caller-supplied idempotency key. Including the generation means a genuine re-subscribe after a
   cancellation is not mistaken for a replay of the original signup.

On a `409` the flow re-reads the shopper's subscriptions and returns the live one. Only if nothing
is found does it surface `409` to the caller — with a message saying to re-read rather than retry,
because as Maxio's duplicate-prevention guidance notes, the outcome of the original request cannot
be inferred.

A `422` "reference has already been taken" on customer creation is handled the same way: re-look-up
and reuse the winner.

### Payment collection

Neither seeded plan requires a payment method, but Maxio sites default to *automatic* collection,
which charges a card at signup — and this integration deliberately captures no card details, so an
automatic signup fails with *"No payment method was on file"*. Subscriptions are therefore created
with an invoice-style collection method matched to the site's architecture: `remittance` on
Relationship Invoicing sites, `invoice` on statement-based ones. Set
`Maxio:PaymentCollectionMethod` to `automatic` for a deployment that does capture cards (via
Billing.js and a payment profile).

Plans whose Maxio configuration *does* require a stored card are rejected up front with a `422`
naming the problem, rather than forwarding a generic provider error.

### Talking to Maxio well

`MaxioResilienceHandler` implements what Maxio's guidance asks for:

- **Bounded concurrency** (default 4). Maxio limits a subdomain to a small number of concurrent
  API workers and queues the overflow, so more parallelism only makes every request slower.
- **Retry with exponential backoff + jitter** on `429`, `5xx` and transport faults, honouring
  `Retry-After`.
- **Per-attempt timeouts**, so a retry gets a full budget rather than the remains of the first
  attempt's.

Retrying `POST`s is safe here precisely because both writes are guarded — subscription creates
carry a `uniqueness_token`, customer creates are protected by reference uniqueness.

Plans and site metadata are cached briefly (60s / 30min) to keep plan browsing off the wire.

### Error translation

Maxio-specific faults stay in Infrastructure. `MaxioApiException` is translated at the service
boundary into the provider-neutral exceptions in `ApplicationCore/Exceptions/BillingException.cs`,
which `ExceptionMiddleware` maps to status codes. A misconfigured deployment (`503`) and a provider
outage (`502`) are deliberately distinguishable from a genuine bug (`500`).

---

## Verifying it

### 0. Environment

`global.json` pins the SDK to 8.0.x; only the .NET 10 SDK is installed here, so `rollForward` is
set to `latestMajor`. Also run with `DOTNET_ROLL_FORWARD=Major`. There is no LocalDB on this
machine, so run with `UseOnlyInMemoryDatabase=true`. Make sure the HTTPS dev cert is trusted:

```bash
dotnet dev-certs https --check
```

### 1. Load the credentials into user-secrets

```bash
dotnet user-secrets --project src/PublicApi set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets --project src/PublicApi set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets --project src/PublicApi set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

### 2. Build and run the tests

```bash
export DOTNET_ROLL_FORWARD=Major
dotnet build eShopOnWeb.sln
dotnet test tests/UnitTests/UnitTests.csproj
dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj
```

The billing tests run against a fake Maxio that reproduces the provider's real constraints (one
customer per reference, single-use uniqueness tokens), so they need no network and no credentials.

### 3. Start the API

```bash
export DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true
export ASPNETCORE_URLS="https://localhost:26323;http://localhost:26324"
dotnet run --project src/PublicApi/PublicApi.csproj
```

The log should say `Maxio billing configured: base address https://<subdomain>.chargify.com/,
product family '<handle>'.` Swagger is at <https://localhost:26323/swagger>.

### 4. Get a bearer token

The storefront cookie does not work here — PublicApi is JWT-only.

```bash
API=https://localhost:26323
TOKEN=$(curl -sk -X POST "$API/api/authenticate" -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)
```

### 5. Browse the plans

```bash
curl -sk "$API/api/subscription-plans" -H "Authorization: Bearer $TOKEN" | jq
```

### 6. Subscribe — and double-click it

Two concurrent requests: exactly one `201`, one `200`, both returning the same subscription id.

```bash
for i in 1 2; do
  curl -sk -X POST "$API/api/subscriptions" -H "Authorization: Bearer $TOKEN" \
    -H 'Content-Type: application/json' -d '{"planHandle":"eshop-pro"}' \
    -o "sub$i.json" -w "req$i HTTP:%{http_code}\n" &
done; wait
jq '{created, id: .subscription.id, message}' sub1.json sub2.json
```

### 7. See it in the account

```bash
curl -sk "$API/api/my-subscriptions" -H "Authorization: Bearer $TOKEN" | jq
```

### 8. Confirm against Maxio directly

One customer, one subscription — no duplicates:

```bash
REF=$(printf 'eshoponweb:demouser@microsoft.com' | jq -sRr @uri)
CUSTOMER=$(curl -s -u "$MAXIO_API_KEY:X" \
  "https://$MAXIO_SITE_SUBDOMAIN.chargify.com/customers/lookup.json?reference=$REF" | jq .customer.id)
curl -s -u "$MAXIO_API_KEY:X" \
  "https://$MAXIO_SITE_SUBDOMAIN.chargify.com/customers/$CUSTOMER/subscriptions.json" \
  | jq '[.[].subscription | {id, state, plan: .product.handle, reference, payment_collection_method}]'
```

### 9. Failure paths

```bash
curl -sk "$API/api/subscription-plans" -w '\n%{http_code} (expect 401)\n'                       # no token
curl -sk -X POST "$API/api/subscriptions" -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{}' -w '\n%{http_code} (expect 400)\n'                # no plan
curl -sk -X POST "$API/api/subscriptions" -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"planHandle":"nope"}' -w '\n%{http_code} (expect 404)\n'
```

### Resetting between runs

The sandbox is a test site, so a subscription can be purged (this also deletes the customer, which
is what makes the "first-ever subscribe" path repeatable):

```bash
curl -s -u "$MAXIO_API_KEY:X" -X POST \
  "https://$MAXIO_SITE_SUBDOMAIN.chargify.com/subscriptions/<id>/purge.json?ack=<customer_id>&cascade%5B%5D=customer"
```

---

## Not included

- **Card capture.** Signup uses invoice-style collection; wiring Billing.js and payment profiles
  is the natural next step, and `Maxio:PaymentCollectionMethod` is the switch that turns it on.
- **Cancel / upgrade / downgrade.** The scope here is the subscribe flow.
- **Webhooks.** Maxio state changes (dunning, cancellation) are picked up on the next read rather
  than pushed. Because eShopOnWeb stores nothing, reads are always current — but they are also
  always a round trip.
- **A cross-instance lock.** Layer 1 above is per-process. Layers 2 and 3 are not, and they are
  the ones that make duplicates impossible.
