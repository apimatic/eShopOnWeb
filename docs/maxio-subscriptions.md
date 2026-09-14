# Recurring subscriptions with Maxio Advanced Billing

eShopOnWeb's one-time flow (Catalog → Basket → Order) is untouched. This is a second, parallel
capability: a logged-in shopper can browse recurring plans, subscribe to one, and see the result in
their account. **Maxio Advanced Billing is the system of record** — eShopOnWeb persists no plans, no
billing customers and no subscriptions of its own.

The Maxio OpenAPI specification in [`maxio-spec/`](../maxio-spec) is the contract. Every request this
integration makes corresponds to an operation declared there; each client method names the operation id
it implements.

---

## HTTP API (`src/PublicApi`, JWT)

All three endpoints require a bearer token from `POST /api/authenticate`. The shopper is taken from the
token's name claim — never from the request body — so a caller can only ever act on their own billing
data.

| Method | Route | Purpose |
|--------|-------|---------|
| `GET`  | `/api/subscription-plans` | Plans available in the configured product family |
| `POST` | `/api/subscriptions`      | Subscribe the caller to a plan (idempotent) |
| `GET`  | `/api/my-subscriptions`   | The caller's subscriptions, read from Maxio |

### `POST /api/subscriptions`

```json
{ "planHandle": "eshop-pro", "firstName": "Ada", "lastName": "Lovelace" }
```

`planHandle` is required — pass one of the handles from `GET /api/subscription-plans`. Omitting it
returns `400` along with the handles that *are* valid, so the caller never has to guess. `firstName` and
`lastName` are optional and are used only when the shopper's Maxio customer record is created for the
first time; when they are absent a name is derived from the e-mail address.

Responses:

* `201 Created` with `"created": true` — the shopper was enrolled by this call.
* `200 OK` with `"created": false` — an equivalent live subscription already existed and was returned.
  This is what a double-click, a retry or a concurrent duplicate produces.

The body confirms plan, price, state and next billing date:

```json
{
  "created": true,
  "subscription": {
    "id": 94212761,
    "state": "active",
    "isLive": true,
    "planHandle": "eshop-pro",
    "planName": "Pro Plan",
    "priceInCents": 29900,
    "price": 299,
    "currency": "USD",
    "interval": 1,
    "intervalUnit": "month",
    "displayPrice": "299.00 USD / month",
    "currentPeriodStartedAt": "2026-09-06T22:33:27+05:00",
    "currentPeriodEndsAt": "2026-10-06T22:33:27+05:00",
    "nextBillingAt": "2026-10-06T22:33:27+05:00",
    "activatedAt": "2026-09-06T22:33:28+05:00",
    "paymentCollectionMethod": "remittance",
    "balanceInCents": 29900,
    "billingCustomerId": 98840775,
    "billingCustomerReference": "eshoponweb:demouser@microsoft.com"
  }
}
```

### Status codes

| Status | When |
|--------|------|
| `400` | `planHandle` missing (the response lists the valid handles) |
| `401` | No / invalid bearer token |
| `404` | `planHandle` is not a plan in the configured product family |
| `422` | Maxio rejected the request (its messages are passed through) |
| `502` | Maxio was unreachable, timed out, or returned an unexpected status |
| `503` | The integration is not configured, or Maxio rejected the credentials |

A deployment with no Maxio settings still boots and serves the rest of the API; only the three
subscription routes report `503`.

---

## Configuration

Bound from the `Maxio` configuration section. **Values never live in the repository** — load them from
user-secrets or the environment.

| Key | Sandbox source | Meaning |
|-----|----------------|---------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | API key. Sent as the HTTP Basic *username* with the password `x`, per the spec's `BasicAuth` scheme. |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Substituted into the spec's server template `https://{site}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Product family whose products are offered as plans. Resolved by handle at runtime, because Maxio reassigns numeric ids on re-seed. |
| `Maxio:BaseUrl` | – | Optional. When set it is used **verbatim** as the API base address instead of deriving one from the subdomain — this is how you target a non-US environment (e.g. `https://{site}.ebilling.maxio.com`). |

Optional tuning keys, all with working defaults: `Maxio:PaymentCollectionMethod`,
`Maxio:RequestTimeout`, `Maxio:MaxRetryAttempts`, `Maxio:RetryBaseDelay`, `Maxio:CatalogCacheDuration`.

Load the sandbox credentials into user-secrets (from a shell that has the env vars):

```bash
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"              --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"       --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

---

## How it fits together

```
src/PublicApi/SubscriptionEndpoints/     HTTP surface, DTOs, identity from the JWT
src/ApplicationCore/Interfaces/          ISubscriptionBillingService  (provider-agnostic)
src/ApplicationCore/Subscriptions/       SubscriptionPlan, CustomerSubscription, SubscriberIdentity
src/ApplicationCore/Exceptions/          SubscriptionBillingException hierarchy → HTTP status codes
src/Infrastructure/Maxio/                MaxioSubscriptionBillingService (orchestration)
src/Infrastructure/Maxio/Contracts/      Spec-derived request/response schemas
src/Infrastructure/Maxio/Http/           Retry / backoff delegating handler
```

Nothing outside `src/Infrastructure/Maxio` knows that the provider is Maxio: the adapter translates every
provider failure into one of the `SubscriptionBillingException` types, which the API layer maps to status
codes.

### Spec operations used

| Operation id | Request | Used for |
|--------------|---------|----------|
| `readSite` | `GET /site.json` | Site currency and invoicing architecture |
| `listProductFamilies` | `GET /product_families.json` | Resolve `Maxio:ProductFamilyHandle` → family id |
| `listProductsForProductFamily` | `GET /product_families/{product_family_id}/products.json` | The plan catalog (paginated, archived excluded) |
| `readCustomerByReference` | `GET /customers/lookup.json?reference=` | Find the shopper's billing customer |
| `createCustomer` | `POST /customers.json` | Create it on first use |
| `createSubscription` | `POST /subscriptions.json` | Enrol the shopper |
| `listCustomerSubscriptions` | `GET /customers/{customer_id}/subscriptions.json` | Idempotency check and `my-subscriptions` |

---

## Idempotency — why a double-click cannot double-charge

There is no local `userId → subscription` table to lose (and with the in-memory database there could not
be one), so idempotency is anchored on values Maxio itself enforces as unique.

1. **Stable customer reference.** The shopper's billing customer is keyed by
   `eshoponweb:{lower-cased e-mail}`. Maxio rejects a duplicate `reference` with `422`, so the
   "look up, else create" pair is safe: if two requests race, the loser re-reads and reuses the winner's
   customer. The reference is derived from the e-mail rather than the ASP.NET Identity row id precisely so
   that it survives a restart of the in-memory identity store.
2. **In-process serialisation.** Concurrent subscribe attempts by the same shopper are funnelled through a
   per-reference async lock, so the "already subscribed?" check cannot be overtaken by a sibling request.
3. **Existence check.** Before creating anything the shopper's subscriptions are listed; a live
   subscription to the same plan is returned as-is with `created: false`.
4. **Deterministic subscription reference.** The create call carries
   `{customerReference}|{planHandle}|{n}`, where `n` counts previous subscriptions to that plan. Maxio
   enforces subscription `reference` uniqueness, so a duplicate that slips past steps 2–3 (a second
   instance, say) is rejected server-side — and the caller then gets the winning subscription instead of an
   error. Including `n` keeps re-subscribing after a cancellation possible.
5. **Lost-response recovery.** If the create call fails at the transport level, the subscription list is
   re-read and matched on that deterministic reference, so a subscription that *was* created is adopted
   rather than duplicated.

Retries follow the same reasoning: throttling (`429`) is always replayed because the request was rejected
before it was processed, but a `5xx` or dropped connection is only replayed for idempotent methods —
never for `POST /subscriptions.json`.

## Payment collection

The seeded plans do not require a stored payment method, but Maxio's default `automatic` collection still
needs a payment profile to settle the first invoice (`"No payment method was on file for the $299.00
balance"`). For plans whose `require_credit_card` is false the integration therefore signs the
subscription up with an invoice-style collection method — `remittance` on sites with relationship
invoicing enabled, `invoice` otherwise — so enrolment completes without card capture or 3-D Secure. Plans
that *do* require a card keep the site default, so Maxio's own error reaches the caller unmodified. Set
`Maxio:PaymentCollectionMethod` to override.

## Not in scope

Cancellation, plan changes and usage reporting for the seeded metered component (`api-call`) are not part
of this capability. The spec covers all three, so they are additive rather than blocked.
