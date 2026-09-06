# Subscription billing (Maxio Advanced Billing)

eShopOnWeb's catalog/basket/order flow is one-time commerce. This capability sits **alongside** it and
adds recurring subscriptions, with **Maxio Advanced Billing as the system of record** — nothing about
who is subscribed to what is stored locally.

## Endpoints

All three live on `src/PublicApi` and are JWT-authenticated. The shopper is taken from the token's
name claim, never from the request body.

| Method | Route | Purpose |
|--------|-------|---------|
| `GET`  | `/api/subscription-plans` | The plans on offer, read from the configured Maxio product family. |
| `POST` | `/api/subscriptions`      | Subscribe the caller to a plan. Idempotent. |
| `GET`  | `/api/my-subscriptions`   | The caller's subscriptions as Maxio currently reports them. |

`POST /api/subscriptions` takes `{ "planHandle": "eshop-pro" }`. `planHandle` is optional; when it is
omitted the plan configured as `Maxio:DefaultPlanHandle` is used, and if that is unset the request is
rejected with `400` listing the handles that are valid.

It answers **`201 Created`** for a new enrollment and **`200 OK` with `alreadySubscribed: true`** when
the shopper was already on the plan — so a double-clicked Subscribe button is a no-op, not a second
subscription.

## The subscribe flow

```
POST /api/subscriptions
  └─ resolve the shopper from the JWT  (SubscriberResolver → identity store)
  └─ resolve the plan by handle        (GET /product_families/handle:{family}/products.json)
  └─ ensure a Maxio customer exists    (GET /customers/lookup.json → POST /customers.json)
  └─ already subscribed to this plan?  (GET /customers/{id}/subscriptions.json)
  │    yes → return it, 200
  └─ enroll                            (POST /subscriptions.json)  → 201
```

### Why it cannot double-subscribe

Three independent guards, because any one of them can be defeated on its own:

1. **A stable customer reference.** The Maxio customer is keyed by `eshoponweb:{account email}`, and
   Maxio permits only one customer per reference per site. A create that loses the race comes back as
   `422 reference: must be unique` and is resolved by re-reading the customer that won.
2. **An authoritative pre-check.** Before enrolling, the shopper's existing subscriptions are read
   from Maxio and matched on plan handle. Problem states (`past_due`, `unpaid`, `on_hold`, …) still
   count as enrolled; only genuinely ended ones (`canceled`, `expired`, `failed_to_create`,
   `trial_ended`) let a shopper subscribe to that plan again.
3. **Maxio's duplicate prevention.** Every write carries a deterministic `uniqueness_token`, so a
   retried or racing submission is rejected with `409` instead of being performed twice; the `409` is
   then resolved by re-reading rather than assumed either way. The token is bucketed over
   `Maxio:IdempotencyWindowSeconds` (5 minutes) so it absorbs double-clicks and client retries without
   blocking a deliberate re-subscribe later.

Within a single process a keyed async lock additionally serialises same-shopper, same-plan attempts,
so guard 3 is only needed across instances.

### Payment collection

The seeded plans do not require a payment method, but Maxio still tries to *collect* the first
invoice, and the default `automatic` collection fails with *"No payment method was on file"*.
eShopOnWeb captures no card at signup, so subscriptions are created with invoiced collection —
`remittance` on Relationship Invoicing sites, `invoice` on legacy Statements sites, chosen from
`GET /site.json`. Set `Maxio:PaymentCollectionMethod` to `automatic` if you add card capture
(via Billing.js) before subscribing.

## Configuration

Bound from the `Maxio` configuration section.

| Key | Required | Notes |
|-----|----------|-------|
| `Maxio:ApiKey` | yes | Sent as HTTP Basic username with password `X`. **Secret — use user-secrets or the environment.** |
| `Maxio:Subdomain` | yes, unless `BaseUrl` is set | The API address is derived as `https://{subdomain}.chargify.com/`. |
| `Maxio:ProductFamilyHandle` | yes | The product family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address when set, instead of deriving one from the subdomain (EU hosting, an API Gateway connector, a local stub). |
| `Maxio:DefaultPlanHandle` | no | Plan used when a subscribe request names none. |
| `Maxio:PaymentCollectionMethod` | no | Override for the collection method described above. |
| `Maxio:CatalogCacheSeconds` | no (60) | How long the plan catalog and site metadata are cached. |
| `Maxio:RequestTimeoutSeconds` | no (30) | Budget for a whole call **including retries**. |
| `Maxio:MaxRetryAttempts` | no (3) | Retries after the first attempt. |
| `Maxio:MaxConcurrentRequests` | no (4) | Maxio throttles per site on concurrency; this queues locally instead. |
| `Maxio:IdempotencyWindowSeconds` | no (300) | Window over which a repeated subscribe reuses the same uniqueness token. |

Nothing in this section has a default that points at a particular Maxio site, plan or catalog — the
same build runs against any site.

Load the sandbox credentials into user-secrets (they never belong in a file in this repository):

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
dotnet user-secrets set "Maxio:DefaultPlanHandle"   "eshop-pro"
```

When Maxio is not configured the host still starts and the rest of the API keeps working; the three
subscription endpoints answer `503` naming the missing configuration keys.

## Failure handling

| Situation | Status | Where |
|-----------|--------|-------|
| Unknown or unspecified plan handle | `400` (lists valid handles) | `SubscriptionPlanNotFoundException` |
| Duplicate submission that cannot be resolved to a subscription | `409` | `SubscriptionConflictException` |
| Maxio rejected or failed the request | `502` (carries Maxio's own errors) | `BillingGatewayException` / `MaxioApiException` |
| Maxio unreachable, timed out, or still throttling after every retry | `503` | `BillingUnavailableException` |
| Missing or malformed configuration | `503` (names the keys) | `BillingConfigurationException` |

`MaxioResilienceHandler` retries `429`, `408` and `5xx` plus transport failures with exponential
backoff and jitter, honouring `Retry-After`. Retrying a `POST` is safe precisely because of the
uniqueness token. `MaxioConcurrencyHandler` caps in-flight calls at Maxio's documented ceiling of four
per site.

## Where the code lives

| Path | Contents |
|------|----------|
| `src/ApplicationCore/Subscriptions/` | Domain models: plan, subscriber identity, subscription summary, states. |
| `src/ApplicationCore/Interfaces/ISubscriptionBillingService.cs` | The billing abstraction the API depends on. |
| `src/ApplicationCore/Exceptions/Billing*.cs`, `Subscription*.cs` | Billing failure taxonomy. |
| `src/Infrastructure/Maxio/` | Maxio implementation: settings, typed client, wire contracts, resilience, orchestration. |
| `src/PublicApi/SubscriptionEndpoints/` | The three endpoints, their DTOs, and the token → subscriber resolver. |
| `tests/UnitTests/Infrastructure/Maxio/` | Idempotency, wire-format, resilience and locking tests. |
| `tests/PublicApiIntegrationTests/SubscriptionEndpoints/` | Authorization and unconfigured-host behaviour. |
