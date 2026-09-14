# Subscription billing with Maxio Advanced Billing

eShopOnWeb's original flow is one-time commerce: Catalog → Basket → Order. This adds a **parallel**
capability for recurring subscriptions, with **Maxio Advanced Billing** as the billing system of
record. Nothing in the cart or checkout flow changes.

Maxio owns the subscription data. eShopOnWeb stores no plan catalog, no customer mirror and no
subscription rows, so there is nothing that can drift out of sync with billing. The eShopOnWeb user
is bound to a Maxio customer purely through a derived, unique reference.

## Endpoints

All three live on `src/PublicApi`, are JWT-authenticated, and take the caller's identity from the
token — never from the request body.

| Method | Route | Purpose |
|---|---|---|
| `GET`  | `/api/subscription-plans` | Plans published by the billing system, cheapest first. |
| `POST` | `/api/subscriptions`      | Subscribe the caller to a plan. Idempotent. |
| `GET`  | `/api/my-subscriptions`   | The caller's own subscriptions, newest first. |

### `POST /api/subscriptions`

```json
{ "planHandle": "eshop-pro", "idempotencyKey": "optional-caller-key" }
```

- `planHandle` — required unless `Maxio:DefaultPlanHandle` is configured. An unknown handle answers
  `400` and lists the handles that do exist.
- `idempotencyKey` — optional. Two requests carrying the same key always resolve to the same
  subscription, even across a cancel-and-resubscribe cycle. It is **not** needed for duplicate
  protection (see below).

Responses:

- `201 Created` with `"created": true` — a new subscription was made.
- `200 OK` with `"created": false` — the caller was already enrolled; nothing changed. The
  subscription returned is the existing one.

Both carry the plan, price, state and next billing date.

## Idempotency

A double-click must never produce two customers or two subscriptions. Three mechanisms combine, and
only the middle one is load-bearing:

1. **Per-subscriber in-process lock** — collapses a burst into one round trip. An optimisation, not
   a guarantee: it does nothing across processes.
2. **Unique references enforced by Maxio** — the real guarantee. Advanced Billing rejects a second
   customer or subscription carrying a reference that is already taken, so the loser of a race gets
   a `422` instead of creating a duplicate. The integration recognises that specific `422`, re-reads
   the record that won, and returns it. This holds across processes, instances and restarts.
   - Customer reference: `eshop:cust:{subscriber-key}`
   - Subscription reference: `eshop:sub:{subscriber-key}:k:{idempotency-key}`, or when no key is
     supplied, `eshop:sub:{subscriber-key}:{plan-handle}:{n}` where `n` is one more than the number
     of subscriptions the customer already has on that plan. Deriving `n` from state both racers can
     see is what makes them collide — while a shopper legitimately re-subscribing after a
     cancellation lands on the next number and gets a genuinely new subscription.
   - The `eshop` prefix is `Maxio:ReferencePrefix`, so records written by this app are recognisable
     in the Maxio UI and cannot collide with another system's on the same site.
3. **Live-subscription pre-check** — one shopper never holds the same plan twice. A live
   subscription to the requested plan is returned as-is, keyed or not.

The subscriber key is derived from the **normalised user name**, not from the Identity row's primary
key: the key becomes the Maxio customer reference and has to outlive the account, but eShopOnWeb
assigns Identity ids at seed time, so they are regenerated whenever the store is rebuilt — every
restart when running on the in-memory provider.

## Configuration

Bound from the `Maxio` section. Only the first three are required.

| Key | Required | Notes |
|---|---|---|
| `Maxio:ApiKey` | yes | Site API key. Sent as the HTTP Basic username with password `x`. **Secret.** |
| `Maxio:Subdomain` | yes* | Site subdomain. \*Not needed when `Maxio:BaseUrl` is set. |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are published as plans. |
| `Maxio:BaseUrl` | no | Absolute base address, used **verbatim** when set. Otherwise derived: `https://{subdomain}.chargify.com` (US) or `https://{subdomain}.ebilling.maxio.com` (EU). |
| `Maxio:Environment` | no | `US` (default) or `EU`. Ignored when `BaseUrl` is set. |
| `Maxio:DefaultPlanHandle` | no | Plan used when a request omits `planHandle`. Unset by default, so an omission is reported rather than guessed. |
| `Maxio:PaymentCollectionMethod` | no | Override. Unset means the correct value is read from the site (see below). |
| `Maxio:CatalogCacheDuration` | no | Default 60s. `00:00:00` disables the plan cache. |
| `Maxio:RequestTimeout` | no | Default 30s. Total budget for one operation, retries included. |
| `Maxio:MaxRetryAttempts` / `Maxio:RetryBaseDelay` | no | Default 3 / 250ms. |
| `Maxio:ReferencePrefix` | no | Default `eshop`. |

Configuration is validated at startup, so a missing key is a boot failure with a message naming the
key, not a `500` for the first shopper who tries to subscribe.

**Secrets never go in the repository.** Load them from your environment into user-secrets:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

The `Maxio` section in `appsettings.json` documents the shape with empty values only.

## Design notes

**Product family is resolved by handle, never by id.** Maxio reassigns numeric ids when a catalog is
re-seeded. `/product_families/{handle}/products.json` is not supported (it answers 404), so the
family is located by listing `/product_families.json` and matching the handle, then its id is used
for the products call.

**Signups are invoiced, not charged.** The default collection method (`automatic`) attempts to
capture the first invoice immediately, which fails with *"No payment method was on file"* for any
priced plan — this integration deliberately stores no payment profile and does no card capture or
3-DS. So the collection method is set explicitly, and the valid value depends on the site's
invoicing architecture: `remittance` on Relationship Invoicing sites, `invoice` on legacy Statements
sites. Rather than assume, the integration reads `relationship_invoicing_enabled` from
`GET /site.json` and picks accordingly. A plan whose `require_credit_card` is true is refused up
front with `422` and an explanation, instead of forwarding a confusing billing error.

**Retries are asymmetric.** A `GET` is replayed on `429`/`5xx`/network errors with exponential
backoff and full jitter, honouring `Retry-After`. A `POST` is only replayed on `429`, where Maxio
has said it did not process the request — replaying a `POST` after a `5xx` would risk a duplicate.
Retry-safety for `POST` comes from the unique references instead.

**Errors map to honest status codes.** An unknown plan is `400`. A plan this integration cannot
subscribe to is `422`. An upstream billing problem is `503` when retrying may help and `502` when
Maxio gave a definitive refusal — never `500`, which would blame eShopOnWeb for an upstream fault.

## Layout

| Path | Contents |
|---|---|
| `src/ApplicationCore/Subscriptions/` | Domain view: `SubscriptionPlan`, `CustomerSubscription`, `SubscribeRequest`, state classification. No HTTP, no provider types. |
| `src/ApplicationCore/Interfaces/ISubscriptionBillingService.cs` | The capability the rest of the app depends on. |
| `src/Infrastructure/Billing/Maxio/` | The Maxio implementation: typed HTTP client, wire models, retry handler, orchestration, DI. All internal except settings, the exception types and `AddMaxioSubscriptionBilling`. |
| `src/PublicApi/SubscriptionEndpoints/` | The three endpoints, their DTOs, and subscriber resolution from the token. |
| `tests/UnitTests/Infrastructure/Billing/Maxio/` | Subscribe/idempotency/catalog/retry coverage, driving the real client over a scripted transport. |

The endpoints follow the `Ardalis.ApiEndpoints` convention already used by `AuthEndpoints`, rather
than the minimal-API convention used by the catalog endpoints: this flow needs per-request services
(the billing client, the Identity user manager) plus the request's cancellation token, which
constructor injection on a per-request endpoint provides directly.

## Contract sourcing

Every endpoint, envelope key and field name was taken from the official Maxio .NET SDK
([`maxio-com/ab-dotnet-sdk`](https://github.com/maxio-com/ab-dotnet-sdk) — `doc/controllers/*.md`
and `Controllers/*.cs`) and then confirmed against a live sandbox site before any code was written
against it. The endpoints used:

| Call | Purpose |
|---|---|
| `GET /site.json` | Site currency and invoicing architecture. |
| `GET /product_families.json` | Resolve the product family by handle. |
| `GET /product_families/{id}/products.json` | Publish the family's products as plans. |
| `GET /customers/lookup.json?reference=…` | Find the billing customer for a user (`404` when absent). |
| `POST /customers.json` | Create it on first subscribe. |
| `GET /customers/{id}/subscriptions.json` | The caller's subscriptions. |
| `GET /subscriptions/lookup.json?reference=…` | Recover the winner of a reference race. |
| `POST /subscriptions.json` | Subscribe. |

Advanced Billing publishes no idempotency-key header; reference uniqueness — verified live on both
customers and subscriptions — is the mechanism it does offer, and is what this integration relies on.
