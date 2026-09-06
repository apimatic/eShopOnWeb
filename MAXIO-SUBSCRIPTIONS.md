# Recurring subscriptions with Maxio Advanced Billing

eShopOnWeb is one-time commerce: Catalog → Basket → Order. This adds a second, **parallel**
capability — recurring subscriptions — with **Maxio Advanced Billing** as the billing system of
record. Nothing in the existing cart/checkout flow changes.

The contract is `maxio-spec/openapi.yaml`. Every request this integration makes is one of the
operations in that specification; the client is hand-written against it, and each member names the
`operationId` it implements.

---

## The hero flow

A signed-in shopper lists plans, subscribes to one, and sees it on their account.

```
GET  /api/subscription-plans   →  the plans on offer, live from Maxio
POST /api/subscriptions        →  ensure a Maxio customer exists, then enroll
GET  /api/my-subscriptions     →  what the shopper currently holds
```

All three live on **`src/PublicApi`** and are **JWT-authenticated**. The shopper identity comes from
the token — the request body cannot name a different user.

---

## Architecture

```
src/PublicApi/SubscriptionEndpoints/        HTTP surface (MinimalApi.Endpoint, per project convention)
        │                                   DTOs, status codes, explicit mapping
        ▼
src/ApplicationCore/Services/               SubscriptionService: orchestration + idempotency
src/ApplicationCore/Subscriptions/          Provider-neutral domain models
src/ApplicationCore/Interfaces/             IBillingGateway (port), ISubscriptionService, ISubscriberDirectory
        ▲
        │ implemented by
src/Infrastructure/Maxio/                   MaxioBillingGateway  → maps wire ⇄ domain, translates errors
                                            MaxioApiClient       → one method per spec operation
                                            Models/MaxioModels   → wire models, each named after its schema
                                            handlers, settings, site cache
src/Infrastructure/Identity/                IdentitySubscriberDirectory → ASP.NET Identity lookup
```

`ApplicationCore` never references Maxio. Swapping billing providers means writing one
`IBillingGateway`.

### Which spec operations are used

| Purpose | Operation | Path |
|---|---|---|
| Site currency + invoicing architecture | `readSite` | `GET /site.json` |
| Plans on offer | `listProductsForProductFamily` | `GET /product_families/handle:{handle}/products.json` |
| Find the shopper's billing customer | `readCustomerByReference` | `GET /customers/lookup.json` |
| Create the billing customer | `createCustomer` | `POST /customers.json` |
| What the shopper holds | `listCustomerSubscriptions` | `GET /customers/{customer_id}/subscriptions.json` |
| Resolve an idempotency replay | `findSubscription` | `GET /subscriptions/lookup.json` |
| Enroll | `createSubscription` | `POST /subscriptions.json` |

Auth is the spec's `BasicAuth` scheme: the API key as the username, `x` as the password. The base
address is the spec's server template `https://{site}.chargify.com` with `Maxio:Subdomain`
substituted for `{site}`, unless `Maxio:BaseUrl` overrides it.

---

## No local subscription state

Maxio is the system of record. eShopOnWeb stores no subscription rows, so nothing can drift out of
sync and nothing is lost when the in-memory database is reset between runs.

The link between the two systems is the **customer reference**:

```
{Maxio:CustomerReferencePrefix}-{shopper email, lowercased}
e.g.  eshoponweb-demouser@microsoft.com
```

The email is used rather than the ASP.NET Identity primary key because the key is not stable across
runs when `UseOnlyInMemoryDatabase=true`, and the reference has to be reproducible for
"ensure a customer exists" to be idempotent.

---

## Idempotency — a double-click never bills twice

Four layers, from cheapest to most authoritative:

1. **Deterministic customer reference.** Look up, then create. A create that loses a race is
   rejected by Maxio for a duplicate reference; the loser re-reads and uses the winner's customer.
2. **Per-shopper in-process lock.** Collapses the common double-click before it reaches Maxio.
3. **"Already holds this plan" check.** A shopper with a current subscription for the plan gets that
   subscription back (HTTP **200**, `alreadySubscribed: true`) instead of a second one. States that
   end the lifecycle — `canceled`, `expired`, `trial_ended`, `failed_to_create` — do not count, so a
   genuine re-subscribe still works. A state this build does not recognise counts as current, on the
   principle that a blocked re-subscribe is cheaper than a double bill.
4. **Unique subscription reference.** Every create carries a reference Maxio enforces as unique.
   Two racing creates cannot both succeed; the loser re-reads by reference and returns the winner.
   An `Idempotency-Key` (header or body field) feeds that reference, which makes a retried request
   safe **across processes**, not just within one.

A new subscription answers **201 Created**; a duplicate or replay answers **200 OK**.

---

## Payment collection

Both demo plans have `require_credit_card: false`, and the sandbox site's default collection method
is `automatic` — which Maxio rejects without a card on file. So when a plan does not require a
payment profile, the integration reads the site's invoicing architecture and picks the collection
method that works there: `remittance` on Relationship Invoicing sites, `invoice` on legacy
Statements sites. When a plan *does* require a payment profile, the site default is left alone.

Nothing about this is hard-coded to the demo site.

---

## Configuration

Bound from the `Maxio` section. **No value is baked into the build** — the same binary runs against a
different site and a different catalog by changing configuration only.

| Key | Required | Environment variable | Meaning |
|---|---|---|---|
| `Maxio:ApiKey` | yes | `MAXIO_API_KEY` | API key; sent as the Basic-auth username |
| `Maxio:Subdomain` | yes | `MAXIO_SITE_SUBDOMAIN` | Site subdomain for the server template |
| `Maxio:ProductFamilyHandle` | yes | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Product family whose products are the plans |
| `Maxio:BaseUrl` | no | `MAXIO_BASE_URL` | Absolute base URL used **verbatim** instead of deriving one from the subdomain. This is how an EU-hosted site (`https://{site}.ebilling.maxio.com`) or a gateway is targeted. |
| `Maxio:DefaultPlanHandle` | no | — | Plan used when a subscribe request names none. Unset by default. |
| `Maxio:CustomerReferencePrefix` | no | — | Defaults to `eshoponweb`. Changing it orphans existing references. |
| `Maxio:TimeoutSeconds` | no | — | Per-request timeout. Default 30. |
| `Maxio:MaxRetryAttempts` | no | — | Retries for throttled/transient responses on repeatable requests. Default 3. |
| `Maxio:SiteCacheMinutes` | no | — | How long `GET /site.json` is cached. Default 15. |

**Secrets never enter the repository.** Load them from the environment into user-secrets:

```bash
dotnet user-secrets --project src/PublicApi set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets --project src/PublicApi set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets --project src/PublicApi set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

The `MAXIO_*` environment variables are also read directly and mapped onto these keys, so a
container or CI job needs no extra setup.

Configuration is **not** validated at host start: subscriptions are additive, and a deployment that
does not use them must still be able to boot the catalog and order endpoints. A misconfiguration is
logged loudly at start-up and returns **502** from the subscription endpoints when they are called.

---

## Errors

| Situation | Status | Notes |
|---|---|---|
| No/invalid bearer token | 401 | |
| Token names a user the identity store does not know | 401 | |
| Plan handle not offered by the configured family | 404 | Message lists the handles that are available |
| Maxio rejected the request (422/400) | 400 | Maxio's own messages are passed through |
| Credentials rejected, or site/family missing | 502 | Operator fault, not caller fault |
| Maxio unreachable, throttled past the retry budget, or 5xx | 503 | |

Throttling (429) and transient 5xx responses are retried with exponential backoff and jitter, and a
`Retry-After` header is honoured. **POSTs are never retried** — a create that timed out may still
have succeeded, so duplicate-safety comes from the unique reference and the re-read, not from
repetition.

---

## Verifying it

See "Verify the Maxio subscription integration" in the repository README.
