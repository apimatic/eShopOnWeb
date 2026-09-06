# Subscription billing (Maxio Advanced Billing)

Recurring-subscription billing, added alongside the existing one-time Catalog → Basket → Order flow.
It does not replace or touch that flow.

**Maxio is the system of record.** eShopOnWeb stores nothing about subscriptions: plans, customers
and enrolments are read from and written to Maxio on every request. That is why the endpoints keep
working across an application restart even when `UseOnlyInMemoryDatabase=true` wipes the local
database.

## Endpoints

All three are JWT-authenticated; the shopper is taken from the token, never from the request body.
Get a token from `POST /api/authenticate` first.

| Method | Route | Purpose |
|--------|-------|---------|
| `GET`  | `/api/subscription-plans` | Plans on offer in the configured product family, cheapest first. |
| `POST` | `/api/subscriptions` | Subscribe the caller to a plan. `201` when created, `200` when the request resolved to a subscription that already existed. |
| `GET`  | `/api/my-subscriptions` | Every subscription the caller holds, newest first, with a `liveCount`. |

`POST /api/subscriptions` body:

```json
{ "planHandle": "eshop-pro", "idempotencyKey": "optional", "firstName": "optional", "lastName": "optional" }
```

`idempotencyKey` may also be sent as the `Idempotency-Key` header.

## Idempotency

Maxio enforces that a `reference` is unique per site, so a deterministic reference makes "create" safe
to repeat without any application-side bookkeeping that a restart would lose.

* Customer reference — `eshop:{login}`, e.g. `eshop:demouser@microsoft.com`. Derived from the login
  name rather than the Identity primary key, because the login survives a reseed of the in-memory
  database. A create that loses a race fails with *"Reference: must be unique"* and is resolved by
  reading the winner back, so concurrent first-time subscribes still produce exactly one customer.
* Subscription reference — `{customer reference}:{scope}`, where the scope is the plan handle, or
  `key:{idempotencyKey}` when the caller supplies one. A double-clicked Subscribe button therefore
  resolves to a single enrolment; subscribing to a *different* plan is a genuinely different request.
* Re-subscribing after cancellation walks to the next slot (`…:eshop-pro#2`), because an ended
  subscription keeps its reference forever. With an explicit idempotency key there is only ever one
  slot — that is the point of the key.

An unrecognised subscription state is treated as *live*, so a state Maxio adds later can never cause a
second enrolment.

## Configuration

Bound from the `Maxio:` section. Never commit the values.

| Key | Source | Notes |
|-----|--------|-------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | HTTP Basic username; the password is the literal `x`, per the spec's `BasicAuth` scheme. |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Fills `{site}` in the spec's `https://{site}.chargify.com` server template. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Product family whose products are offered as plans. |
| `Maxio:BaseUrl` | – | Optional. When set it is used verbatim instead of deriving an address from the subdomain. |

Non-secret behaviour knobs, with their defaults, are in `appsettings.json`:
`PaymentCollectionMethod` (`remittance`), `ReferencePrefix`, `TimeoutSeconds`, `MaxRetryAttempts`,
`CatalogCacheSeconds`, `MaxReferenceAttempts`.

`PaymentCollectionMethod` defaults to `remittance` (invoice billing) so a shopper can subscribe
without a stored payment profile. On a site that captures cards at signup, set it to `automatic`.

Load the credentials with user-secrets (from a shell that already has the environment variables):

```powershell
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY
```

When the section is incomplete the host still starts, and the subscription endpoints answer `503`
naming the missing keys.

## Where the code lives

| Layer | Path | Contents |
|-------|------|----------|
| Domain | `src/ApplicationCore/Subscriptions/` | `SubscriptionPlan`, `Subscription`, `Subscriber`, `SubscriptionState`. |
| Port | `src/ApplicationCore/Interfaces/ISubscriptionService.cs` | The capability, free of any Maxio detail. |
| Adapter | `src/Infrastructure/Maxio/` | Typed client, DTOs, retry handler, reference scheme, service. |
| API | `src/PublicApi/SubscriptionEndpoints/` | Routes, wire DTOs, mapping. |

Every Maxio call maps to one operation in `maxio-spec/openapi.yaml`, which is the contract:

| Purpose | `operationId` | Route |
|---------|---------------|-------|
| Site currency | `readSite` | `GET /site.json` |
| Plan catalogue | `listProductsForProductFamily` | `GET /product_families/{product_family_id}/products.json` |
| Find shopper | `readCustomerByReference` | `GET /customers/lookup.json` |
| Create shopper | `createCustomer` | `POST /customers.json` |
| Find enrolment | `findSubscription` | `GET /subscriptions/lookup.json` |
| Create enrolment | `createSubscription` | `POST /subscriptions.json` |
| List enrolments | `listCustomerSubscriptions` | `GET /customers/{customer_id}/subscriptions.json` |

The product family is addressed as `handle:{ProductFamilyHandle}` — the path parameter accepts
"either the product family's id or its handle prefixed with `handle:`". Handles are stable; numeric
ids are reassigned when the catalogue is reseeded, so no numeric id is ever persisted or configured.

## Error handling

| Situation | Status |
|-----------|--------|
| Missing/invalid `planHandle` | `400` |
| Plan handle not in the product family | `404` |
| No or invalid bearer token | `401` |
| Maxio rejected the request (4xx) | `400`, with the provider's messages |
| Maxio unreachable, throttled, or rejecting our API key | `502` |
| `Maxio:` section incomplete | `503`, naming the missing keys |

Transient failures are retried with exponential backoff and jitter, honouring `Retry-After`. Reads
retry on any 5xx; writes only on `429` and gateway-level `502/503/504`, since a `500` on a write may
have created the record — and even then the unique reference makes the retry safe.
