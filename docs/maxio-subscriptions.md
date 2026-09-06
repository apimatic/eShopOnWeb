# Subscription billing with Maxio Advanced Billing

eShopOnWeb's catalog → basket → order flow is one-time commerce. This capability adds recurring
subscriptions **alongside** it: a logged-in shopper browses plans, subscribes, and sees the
subscription on their account. **Maxio Advanced Billing is the system of record** — eShopOnWeb stores
no subscription state of its own.

Every Maxio interaction is built to the OpenAPI specification in [`maxio-spec/`](../maxio-spec).
Paths, parameters, payload shapes, the `BasicAuth` scheme and the server template all come from it.

## Endpoints

All three live on **`src/PublicApi`** and require a JWT bearer token. The caller's identity always
comes from the token; request bodies never say who is subscribing.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/subscription-plans` | Plans on offer — the products of the configured Maxio product family. |
| `POST` | `/api/subscriptions` | Subscribe the caller to a plan. Idempotent. |
| `GET` | `/api/my-subscriptions` | The caller's subscriptions, newest first. |

### `POST /api/subscriptions`

```jsonc
{
  "planHandle": "eshop-pro",       // required; from GET /api/subscription-plans
  "idempotencyKey": "optional"     // may also be sent as the Idempotency-Key header
}
```

| Status | Meaning |
| --- | --- |
| `201` | A new subscription was created (`alreadySubscribed: false`). |
| `200` | The shopper was already enrolled; their existing subscription is returned (`alreadySubscribed: true`). |
| `400` | `planHandle` missing. |
| `401` | No or invalid bearer token. |
| `402` | The plan requires a stored payment method, which eShopOnWeb does not collect. |
| `404` | No such plan in the configured product family. |
| `502` | Maxio rejected the request or was unreachable; the provider's own message is included. |

The response carries the subscription (`id`, `state`, `nextBillingAt`, period bounds, balance) and the
plan it is on (`name`, `price`, `currency`, `interval`).

## Configuration

Bound from the **`Maxio:`** configuration section:

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Sent as the basic-auth user name with password `x`, per the spec's `BasicAuth` scheme. |
| `Maxio:Subdomain` | yes, unless `BaseUrl` is set | Substituted into the spec's `https://{site}.chargify.com` server template. |
| `Maxio:ProductFamilyHandle` | yes | The family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | Absolute base address used **verbatim** when set, instead of deriving one from the subdomain (e.g. the EU host). |
| `Maxio:PaymentCollectionMethod` | no | Pins the collection method; otherwise derived from the site (see below). |
| `Maxio:TimeoutSeconds` | no | Default `30`. |
| `Maxio:MaxRetryAttempts` | no | Retries after the first attempt on a transient fault. Default `3`. |
| `Maxio:CatalogCacheSeconds` | no | Plan/site cache lifetime. Default `60`; `0` disables caching. |

**No credential value is ever committed.** Load them into user secrets:

```bash
dotnet user-secrets --project src/PublicApi set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets --project src/PublicApi set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets --project src/PublicApi set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

As a convenience for containers and CI, `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`,
`MAXIO_DEFAULT_PRODUCT_FAMILY` and `MAXIO_BASE_URL` are also read from the environment at start-up and
mapped onto the matching `Maxio:` keys at the **lowest** precedence, so user secrets and `appsettings`
always win. The host boots fine without any of this; only the subscription endpoints fail, with a
message naming the missing setting.

## How the flow works

1. **Resolve the plan.** `GET /product_families/handle:{family}/products.json` (`listProductsForProductFamily`),
   cached briefly. Only plans in the configured family can be subscribed to, so a caller cannot
   enroll in some other product that happens to live on the same Maxio site.
2. **Ensure the customer.** `GET /customers/lookup.json?reference=…` (`readCustomerByReference`), then
   `POST /customers.json` (`createCustomer`) if absent. The reference is `eshop:{userName}`.
3. **Enroll.** `POST /subscriptions.json` (`createSubscription`) with `customer_id`, `product_handle`
   and a reference eShopOnWeb owns.
4. **Read back.** `GET /customers/{customer_id}/subscriptions.json` (`listCustomerSubscriptions`)
   powers `/api/my-subscriptions`; `GET /subscriptions/lookup.json?reference=…` (`findSubscription`)
   resolves races.

### Idempotency

A double-click can never produce a second customer or a second subscription. Three layers:

- **Deterministic references.** The customer reference is `eshop:{userName}`; the subscription
  reference is `eshop:{userName}:{planHandle}`, or `eshop:{userName}:key:{idempotencyKey}` when a key
  is supplied. Maxio enforces uniqueness on both, so the billing system itself is the arbiter — this
  holds across processes, restarts and instances.
- **Read before write.** A shopper already on the requested plan gets their existing subscription back
  with `200`, no create attempted.
- **Conflict resolution.** If a concurrent request wins the race, Maxio answers
  `422 "Reference: must be unique"`; the loser reads the winner's record back and returns it rather
  than failing or duplicating. A per-shopper in-process lock keeps that path rare.

Re-subscribing to a plan the shopper previously cancelled is allowed: the end-of-life states from the
spec (`canceled`, `expired`, `failed_to_create`, `trial_ended`) free the plan, and the new signup takes
a suffixed reference (`…:eshop-pro:2`).

### Payment collection

Both demo plans have `require_credit_card: false`, and eShopOnWeb captures no card details — so signup
uses an invoice-based collection method from the spec's `Collection-Method` enum: `remittance` on sites
with Relationship Invoicing enabled, `invoice` otherwise, decided from `GET /site.json` (`readSite`).
`Maxio:PaymentCollectionMethod` overrides this. A plan with `require_credit_card: true` is rejected up
front with `402` instead of being sent to Maxio to fail.

### Resilience

The typed client retries transient faults (`408`, `429`, `500`, `502`, `503`, `504`, connection
failures) with exponential backoff, jitter and `Retry-After` support. Retrying a create is safe
precisely because of the unique reference: a retry of a request that already succeeded comes back as a
`422` conflict, which resolves to a read rather than a duplicate. Query strings are stripped from log
messages so shopper references never reach the logs, and the API key is never logged at all.

## Where the code lives

| Path | Role |
| --- | --- |
| `src/ApplicationCore/Subscriptions/` | Domain model: plans, subscriptions, subscriber identity, states. |
| `src/ApplicationCore/Interfaces/ISubscriptionService.cs` | The capability the API calls. |
| `src/ApplicationCore/Interfaces/ISubscriptionBillingGateway.cs` | The billing system, in eShopOnWeb's vocabulary. |
| `src/ApplicationCore/Services/SubscriptionService.cs` | Subscribe orchestration and idempotency. |
| `src/Infrastructure/Maxio/` | Maxio adapter: options, typed client, wire contracts, retry, gateway. |
| `src/PublicApi/SubscriptionEndpoints/` | The three HTTP endpoints and their DTOs. |
| `tests/UnitTests/.../SubscriptionServiceTests/` | Idempotency, conflict resolution, plan validation. |
| `tests/UnitTests/Infrastructure/Maxio/` | Request shapes, error decoding, base-URL resolution. |
| `tests/PublicApiIntegrationTests/SubscriptionEndpoints/` | Authorization and request validation. |

## Verifying it end to end

```bash
# 1. Run PublicApi (in-memory stores, no LocalDB needed)
DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true \
  dotnet run --project src/PublicApi

API=https://localhost:27223

# 2. Get a bearer token
TOKEN=$(curl -sk -X POST "$API/api/authenticate" -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)

# 3. Browse plans
curl -sk "$API/api/subscription-plans" -H "Authorization: Bearer $TOKEN" | jq

# 4. Subscribe -> 201
curl -sk -i -X POST "$API/api/subscriptions" -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"planHandle":"eshop-pro"}'

# 5. Subscribe again -> 200, same id, alreadySubscribed: true
curl -sk -i -X POST "$API/api/subscriptions" -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"planHandle":"eshop-pro"}'

# 6. See it on the account
curl -sk "$API/api/my-subscriptions" -H "Authorization: Bearer $TOKEN" | jq
```

Swagger UI at `https://localhost:27223/swagger` lists the endpoints under **SubscriptionEndpoints**.
