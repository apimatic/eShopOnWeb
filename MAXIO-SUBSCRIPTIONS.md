# Recurring subscriptions with Maxio Advanced Billing

eShopOnWeb's one-time commerce flow (Catalog → Basket → Order) is untouched. This is an
**additive** capability that runs beside it: a signed-in shopper browses plans, subscribes to one,
and sees it on their account. **Maxio Advanced Billing is the system of record** — this application
stores no subscription state of its own.

Everything the integration sends and expects comes from the Maxio OpenAPI specification in
[`maxio-spec/`](maxio-spec/openapi.yaml).

---

## Endpoints

All three live on **`src/PublicApi`** and require a JWT bearer token. The shopper's identity is
taken from the token's name claim and is never read from the request body.

| Method | Route | Purpose |
|--------|-------|---------|
| `GET`  | `/api/subscription-plans` | Plans offered by the configured product family. |
| `POST` | `/api/subscriptions` | Subscribe the caller to a plan. Idempotent. |
| `GET`  | `/api/my-subscriptions` | The caller's subscriptions, newest first. |

### `POST /api/subscriptions`

```json
{ "planHandle": "eshop-pro", "firstName": "Ada", "lastName": "Lovelace", "organization": "Acme" }
```

Only `planHandle` is required; the name fields are optional and, when omitted, are derived from the
shopper's email so the Maxio customer is still recognisable in the merchant UI.

* `201 Created` — the caller was enrolled.
* `200 OK` with `"alreadySubscribed": true` — the caller already held a live subscription to that
  plan, and nothing new was created.

The response carries the plan, price, state and next billing date:

```json
{
  "subscription": {
    "id": 94211532,
    "reference": "eshoponweb-demouser-microsoft-com-eshop-pro",
    "state": "active",
    "isLive": true,
    "planHandle": "eshop-pro",
    "planName": "Pro Plan",
    "priceInCents": 29900,
    "price": 299,
    "currency": "USD",
    "billingPeriod": "every month",
    "currentPeriodEndsAt": "2026-10-06T20:10:59+05:00",
    "nextBillingAt": "2026-10-06T20:10:59+05:00",
    "customerId": 98839783,
    "customerReference": "eshoponweb-demouser-microsoft-com"
  },
  "alreadySubscribed": false
}
```

---

## Configuration

Bound from the `Maxio` configuration section. **The API key is a secret and must never be committed.**
In development, load it into user-secrets from your environment:

```bash
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"              --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"       --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
dotnet user-secrets set "Maxio:Environment"         "$MAXIO_ENVIRONMENT"          --project src/PublicApi
```

| Key | Required | Default | Notes |
|-----|----------|---------|-------|
| `Maxio:ApiKey` | yes | — | Sent as the HTTP Basic user name with password `x`, per the spec's `BasicAuth` scheme. |
| `Maxio:Subdomain` | unless `BaseUrl` is set | — | Substituted into the environment's server template. |
| `Maxio:ProductFamilyHandle` | yes | — | Only products in this family are offered and subscribable. |
| `Maxio:BaseUrl` | no | — | When set, used **verbatim** as the API base address; `Subdomain`/`Environment` are ignored. |
| `Maxio:Environment` | no | `US` | `US` → `https://{site}.chargify.com`, `EU` → `https://{site}.ebilling.maxio.com` (spec `x-server-configuration`). |
| `Maxio:PaymentCollectionMethod` | no | `remittance` | From the spec schema `Collection-Method`. `remittance` lets a shopper subscribe without a stored card; a site that captures cards up front should use `automatic`. |
| `Maxio:TimeoutSeconds` | no | `30` | Per-request timeout. |
| `Maxio:MaxRetryAttempts` | no | `3` | Retries for transient failures. |
| `Maxio:SiteCacheMinutes` | no | `60` | How long the site currency read is cached. |
| `Maxio:ReferencePrefix` | no | `eshoponweb` | Prefix on every reference this store creates in Maxio. |

Nothing is validated at start-up: eShopOnWeb keeps running without billing credentials, and a
misconfigured integration answers `503 Service Unavailable` on the subscription endpoints only.

---

## How it maps to the specification

The client in `src/Infrastructure/Maxio` is hand-written against the spec. Each method names the
`operationId` it implements:

| `operationId` | Path | Used for |
|---------------|------|----------|
| `readSite` | `GET /site.json` | Site currency (cached, non-fatal on failure). |
| `listProductsForProductFamily` | `GET /product_families/{product_family_id}/products.json` | Plan catalog, addressed as `handle:{ProductFamilyHandle}`. |
| `readProductByHandle` | `GET /products/handle/{api_handle}.json` | Validating the requested plan. |
| `readCustomerByReference` | `GET /customers/lookup.json` | Finding the shopper's billing customer. |
| `createCustomer` | `POST /customers.json` | Creating it the first time. |
| `listCustomerSubscriptions` | `GET /customers/{customer_id}/subscriptions.json` | Idempotency check and `my-subscriptions`. |
| `findSubscription` | `GET /subscriptions/lookup.json` | Checking whether a reference is free. |
| `createSubscription` | `POST /subscriptions.json` | Enrolling the shopper. |

Request and response types in `src/Infrastructure/Maxio/Models` mirror the spec schemas
(`Product`, `Customer`, `Subscription`, `Site` and their `*-Request`/`*-Response` envelopes), and
`MaxioSubscriptionStates` / `MaxioCollectionMethods` enumerate the spec's `Subscription-State` and
`Collection-Method` values.

---

## Idempotency

A double-clicked subscribe must not produce two customers or two subscriptions. Three layers
guarantee that, without any local database — which matters here, because the in-memory provider
loses everything on restart.

1. **Deterministic references.** The Maxio customer reference is derived from the eShopOnWeb user
   name (`eshoponweb-demouser-microsoft-com`), and the subscription reference from that plus the
   plan handle. The same shopper always resolves to the same Maxio records, across restarts and
   across instances. Maxio holds the mapping, so nothing needs to be persisted locally.
2. **Look up before create.** Subscribe reads the customer's subscriptions and returns any live one
   for the requested plan instead of creating a second. A `422 Reference: must be unique` from a
   concurrent writer is recovered by re-reading rather than surfaced as an error.
3. **A per-shopper in-process lock**, which closes the window where two simultaneous requests both
   observe "nothing exists yet".

Re-subscribing after a cancellation is still possible: when the base reference belongs to a
canceled or expired subscription, the next variant (`…-eshop-pro-2`) is used.

## Reliability

* Retries with exponential backoff and jitter, honouring `Retry-After`. Reads retry on any transient
  condition; **writes retry only when the response proves nothing was processed** (`429`, `502`,
  `503`, `504`), so a create is never silently duplicated.
* Failures are translated by `ExceptionMiddleware`: unknown plan → `404`; billing rejected the
  request → `422` with Maxio's own messages; our credentials rejected → `502` with a generic message
  (never echoed back); provider unreachable → `502`, timed out → `504`, rate limited → `429`;
  misconfiguration → `503`.
* Callers can only subscribe to plans inside the configured product family, so a handle cannot be
  used to reach an arbitrary product on the billing site.

---

## Running and verifying it

```bash
export DOTNET_ROLL_FORWARD=Major       # global.json rolls forward to the installed SDK
export ASPNETCORE_ENVIRONMENT=Development
export UseOnlyInMemoryDatabase=true    # no LocalDB required

dotnet run --project src/PublicApi --no-launch-profile \
  --urls "https://localhost:26903;http://localhost:26904"
```

Then get a token and call the endpoints — Swagger UI is at `https://localhost:26903/swagger`:

```bash
TOKEN=$(curl -sk -X POST https://localhost:26903/api/authenticate \
  -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | sed -E 's/.*"token":"([^"]+)".*/\1/')

curl -sk https://localhost:26903/api/subscription-plans -H "Authorization: Bearer $TOKEN"

curl -sk -X POST https://localhost:26903/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"planHandle":"eshop-pro"}'

curl -sk https://localhost:26903/api/my-subscriptions -H "Authorization: Bearer $TOKEN"
```

Automated coverage lives in `tests/UnitTests/Infrastructure` (client contract, options, references,
idempotency and plan-gating rules) and `tests/PublicApiIntegrationTests/SubscriptionEndpoints`
(authorization boundary). None of them call the live sandbox.
