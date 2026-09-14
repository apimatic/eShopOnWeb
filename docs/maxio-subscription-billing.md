# Subscription billing with Maxio Advanced Billing

eShopOnWeb ships as one-time commerce: Catalog → Basket → Order. This adds a **parallel**
capability — recurring subscriptions — with **Maxio Advanced Billing as the system of record**.
Nothing about the cart or checkout flow changes, and no subscription state is persisted locally.

The Maxio OpenAPI specification in [`maxio-spec/`](../maxio-spec) is the contract. Every path,
query parameter, request body, response schema, error model, authentication scheme and server
template used here is taken from it, and a test suite re-checks that against the specification file
on every build (see [Spec conformance](#spec-conformance)).

---

## The endpoints

All three live on `src/PublicApi` and follow that project's `IEndpoint` convention. All three
require a JWT bearer token; the shopper being billed is always taken from the token, never from the
request body.

| Method | Route | Purpose |
|---|---|---|
| `GET`  | `/api/subscription-plans` | Plans available to subscribe to, cheapest first. |
| `POST` | `/api/subscriptions`      | Subscribe the caller to a plan. `201` on enrollment, `200` when already subscribed. |
| `GET`  | `/api/my-subscriptions`   | The caller's subscriptions, newest first. |

### `POST /api/subscriptions`

```jsonc
{
  "planHandle": "eshop-pro",       // required; from GET /api/subscription-plans
  "idempotencyKey": "order-4711",  // optional; may also be sent as the Idempotency-Key header
  "customer": {                    // optional, cosmetic only
    "firstName": "Demo",
    "lastName": "Shopper",
    "organization": "Contoso"
  }
}
```

Responds with the plan, price, state and next billing date:

```jsonc
{
  "created": true,
  "customerCreated": true,
  "customerReference": "eshoponweb-demouser-microsoft-com-03563e80",
  "subscription": {
    "id": 94208359,
    "reference": "eshoponweb-demouser-microsoft-com-03563e80-eshop-pro",
    "state": "active",
    "isActive": true,
    "planHandle": "eshop-pro",
    "planName": "Pro Plan",
    "priceInCents": 29900, "price": 299.00, "currency": "USD",
    "interval": 1, "intervalUnit": "month", "billingPeriod": "month",
    "nextBillingAt": "2026-10-06T10:21:38+05:00",
    "currentPeriodStartedAt": "2026-09-06T10:21:38+05:00",
    "currentPeriodEndsAt": "2026-10-06T10:21:38+05:00",
    "balanceInCents": 29900, "balance": 299.00,
    "paymentCollectionMethod": "remittance"
  }
}
```

---

## Configuration

Bound from the `Maxio` section. Only the first three are required.

| Key | Sandbox source | Meaning |
|---|---|---|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | API key. Sent as the HTTP Basic username with the literal password `x`, per the specification's `BasicAuth` scheme. |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Substituted into the specification server template `https://{site}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Product family whose products are offered as plans. |
| `Maxio:BaseUrl` | — | Optional. When set, used **verbatim** as the base address instead of deriving one from the subdomain. |
| `Maxio:Environment` | `MAXIO_ENVIRONMENT` | `US` (default) or `EU`. Selects between the two server templates the specification declares. Ignored when `BaseUrl` is set. |
| `Maxio:PaymentCollectionMethod` | — | Default `remittance`. See [Why remittance](#why-remittance). |
| `Maxio:ReferencePrefix` | — | Default `eshoponweb`. Give each deployment its own prefix if several share one Maxio site. |
| `Maxio:TimeoutSeconds` | — | Default `30`. Budget for one call including retries. |
| `Maxio:MaxRetryAttempts` | — | Default `3`. |
| `Maxio:CatalogCacheSeconds` | — | Default `60`. How long the plan list and site record are cached. |

**The API key never belongs in the repository.** Load it from the environment into user secrets:

```bash
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"              --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"       --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

`src/PublicApi/appsettings.json` carries the section with **empty** values, as documentation of the
shape only. User secrets and environment variables both override it.

If billing is not configured, the host still starts, logs a warning naming the missing settings, and
answers **503** on the three subscription routes only. Catalog, basket and order endpoints are
unaffected.

---

## How it is put together

```
src/ApplicationCore/Subscriptions/         domain models (plan, subscription, state, commands)
src/ApplicationCore/Interfaces/            ISubscriptionPlanCatalog, ISubscriptionService
src/ApplicationCore/Exceptions/            SubscriptionPlanNotFound / BillingProvider / BillingConfiguration
src/Infrastructure/Maxio/Models/           DTOs transcribed from the specification schemas
src/Infrastructure/Maxio/Http/             typed client, Basic-auth handler, retry handler, error reader
src/Infrastructure/Maxio/Services/         plan catalog + subscription service (the idempotency logic)
src/PublicApi/SubscriptionEndpoints/       the three endpoints, DTOs and the token → subscriber resolver
tests/MaxioBillingTests/                   spec conformance, client, retry, service, endpoint tests
```

The client is hand-written against the specification rather than generated: the specification
describes 175 paths and this integration needs seven of them, so a focused client that names its
source is easier to review than a generated surface that is 96% unused. Fidelity is not left to
trust — the conformance suite checks it mechanically.

### Operations used

| Specification operation | Call |
|---|---|
| `readSite` | `GET /site.json` — site currency for plan pricing |
| `listProductsForProductFamily` | `GET /product_families/{product_family_id}/products.json` |
| `readCustomerByReference` | `GET /customers/lookup.json` |
| `createCustomer` | `POST /customers.json` |
| `listCustomerSubscriptions` | `GET /customers/{customer_id}/subscriptions.json` |
| `findSubscription` | `GET /subscriptions/lookup.json` |
| `createSubscription` | `POST /subscriptions.json` |

The product family is addressed as `handle:<handle>` — the specification defines that path parameter
as "either the product family's id or its handle prefixed with `handle:`". Handles survive a catalog
re-seed; numeric ids do not, so no numeric id appears anywhere in this build.

---

## Idempotency

A double-clicked Subscribe button must not bill a shopper twice. Three independent defences:

1. **A per-shopper in-process lock** serialises concurrent subscribe requests so the
   check-then-create sequence is never interleaved with itself.
2. **An occupancy check.** The shopper's existing subscriptions are read first; one they still hold
   on the same plan is returned as-is (`created: false`, HTTP 200). "Still hold" includes problem
   states such as `past_due` — that subscription exists and must not be duplicated.
3. **Deterministic references.** Maxio enforces uniqueness on the customer reference and on the
   subscription reference. Both are derived deterministically from the shopper and the plan, so a
   request that gets past the first two defences — a second app instance, a retry after a dropped
   response — is refused by Maxio, and the integration turns that refusal back into the record that
   already exists.

References look like `eshoponweb-demouser-microsoft-com-03563e80-eshop-pro`: a readable slug so a
record is recognisable in the Maxio UI, plus a short digest of the exact input so two addresses that
slug alike cannot collide.

The reference is derived from the shopper's **e-mail address**, not their identity user id. With
`UseOnlyInMemoryDatabase=true` the identity store mints fresh user ids on every start, whereas the
seeded e-mail address is stable — so the mapping survives a restart, and re-running the demo does
not create a second customer for the same person.

Cancelling and subscribing again still works: only an *occupied* subscription blocks a new signup,
and a spent reference is superseded (`…-eshop-pro`, then `…-eshop-pro-2`) rather than reused.

A caller-supplied `idempotencyKey` (body field or `Idempotency-Key` header) is a stronger promise:
the same key always resolves to the same subscription, even one that has since been cancelled.

---

## Failure handling

| Situation | Response |
|---|---|
| Unknown plan handle | `404` |
| Missing `planHandle` | `400` |
| Maxio rejected the payload (422) | `422`, with Maxio's messages in `errors` |
| Maxio refused our credentials, or is down | `502` |
| Maxio rate limited us | `503` |
| Maxio did not respond in time | `504` |
| Billing not configured | `503`, naming the missing settings |

Retries are deliberately asymmetric. Reads and rate-limited requests are retried with exponential
backoff, jitter and `Retry-After` support. A **write** that failed with a server error or a dropped
connection is **never** retried, because Maxio may already have applied it; recovery for that case
is the reference lookup described above, which resolves the intended subscription instead of
creating a second one.

The API key is never logged. Query strings are stripped from log lines because they carry customer
references and e-mail addresses, and `401`/`403` responses are logged without their body.

### Why remittance

Both demo plans are configured "payment method not required", and eShopOnWeb captures no card. With
`payment_collection_method: automatic` Maxio refuses the signup — `422 No payment method was on file
for the $299.00 balance` — because it wants to charge immediately. `remittance` invoices the
customer instead, which is what "no card on file" means in Maxio, and the subscription goes straight
to `active`. Deployments that do capture payment profiles should set
`Maxio:PaymentCollectionMethod` to `automatic`.

---

## Spec conformance

`tests/MaxioBillingTests/Spec` parses the real `maxio-spec/openapi.yaml` (and the component files it
`$ref`s) and asserts that:

- every operation the client calls exists at the declared path and method, with the declared
  `operationId`;
- every query and path parameter the client fills in is declared for that operation;
- every `[JsonPropertyName]` on every transcribed model names a property the corresponding schema
  declares;
- the authentication scheme is `http`/`basic`, as `BasicAuth` says;
- the derived base address matches the specification's server template;
- `SubscriptionState` covers the `Subscription-State` enumeration exactly;
- the accepted collection methods match the `Collection-Method` enumeration exactly.

A provider-side contract change therefore surfaces as a failing test, not as a runtime surprise.

`tests/MaxioBillingTests/Client/MaxioSandboxSmokeTests.cs` additionally makes read-only calls to a
real sandbox when `MAXIO_API_KEY` and `MAXIO_SITE_SUBDOMAIN` are present in the environment, and
does nothing when they are not.

---

## Verifying it yourself

See the [step-by-step guide](verify-subscription-billing.md).
