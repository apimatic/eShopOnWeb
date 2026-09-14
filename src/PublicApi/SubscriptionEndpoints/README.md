# Subscription billing (Maxio Advanced Billing)

Recurring-subscription billing for eShopOnWeb. This capability is **additive** — it runs alongside
the existing Catalog → Basket → Order flow and does not change it.

**Maxio Advanced Billing is the system of record.** eShopOnWeb stores no copy of the plan catalog,
the billing customer, or the subscription; every read goes straight to Maxio. That is deliberate:
a local mirror would be one more thing to reconcile, and it would not survive a host that runs on
the in-memory database anyway.

## Endpoints

All three require a JWT bearer token from `POST /api/authenticate`. The account being billed is
always taken from the token — nothing in a request body can change whose subscription is affected.

| Endpoint | Purpose |
|---|---|
| `GET /api/subscription-plans` | Plans on offer, taken from the configured Maxio product family. |
| `POST /api/subscriptions` | Subscribe the caller to a plan. Idempotent. |
| `GET /api/my-subscriptions` | The caller's subscriptions, read live from Maxio. |

### `POST /api/subscriptions`

```jsonc
{
  "planHandle": "eshop-pro",     // required unless Maxio:DefaultPlanHandle is configured
  "idempotencyKey": "optional"   // optional; see "Idempotency" below
}
```

Responses:

| Status | Meaning |
|---|---|
| `201 Created` | The subscription was created by this call. |
| `200 OK` | The caller was already subscribed to the plan; the existing subscription is returned unchanged (`created: false`). |
| `400 Bad Request` | No plan was given and no default is configured, or Maxio rejected the request as invalid. |
| `401 Unauthorized` | Missing/invalid token, or the token names an account that no longer exists. |
| `404 Not Found` | The named plan is not in the configured product family. |
| `409 Conflict` | A concurrent subscribe is in flight and its outcome could not be resolved yet. Re-read `GET /api/my-subscriptions`. |
| `502 Bad Gateway` | Maxio was unreachable, throttled us, or errored. |
| `503 Service Unavailable` | The `Maxio:` configuration section is missing or incomplete. |

## Idempotency

Subscribing is idempotent at three independent levels, so a double-clicked Subscribe button can
never produce two customers or two subscriptions:

1. **Per-shopper serialisation.** Concurrent subscribe calls for one account are serialised in
   process (striped semaphores, so the lock set stays bounded).
2. **Existing-subscription check.** If the shopper already holds a *live* subscription to that plan,
   it is returned as-is and `201` becomes `200`. Ended subscriptions (canceled, expired, …) do not
   block resubscribing.
3. **Maxio `uniqueness_token`.** The create carries a duplicate-prevention token, so a request that
   crossed process or instance boundaries is rejected upstream rather than duplicated. When Maxio
   answers `409 DuplicateSubmissionError` the service re-reads the subscription that the winning
   request created and returns that — which is exactly the "my response got lost" recovery Maxio's
   duplicate-prevention guidance describes.

The token identifies **one logical attempt**, not the (shopper, plan) pair forever: a fixed token
would let a single rejected attempt lock the shopper out of that plan for the full 60-minute
duplicate-prevention window. Supply `idempotencyKey` to define the attempt yourself; otherwise
attempts are bucketed by time (`Maxio:IdempotencyWindowSeconds`, default 60s).

Customer creation is idempotent the same way: the Maxio customer `reference` is derived from the
eShopOnWeb user name (`eshoponweb:<username>`) and Maxio enforces uniqueness on it, so a racing
create is rejected and the winner is adopted. The user name is used rather than the Identity primary
key because it is stable across database resets — including the in-memory provider, which would
otherwise orphan the customer Maxio already holds on every restart.

## Payment collection

eShopOnWeb never captures a card, so a subscription set to collect automatically cannot settle its
signup charge and Maxio refuses the signup. New subscriptions are therefore created with
invoice-based collection, spelled the way the site expects: `remittance` on Relationship Invoicing
sites, `invoice` on statement-based ones. The site's architecture is read once from `GET /site.json`
and cached. Set `Maxio:PaymentCollectionMethod` explicitly (for example to `automatic`) on a
deployment that does capture payment methods.

## Configuration

Bound from the `Maxio:` section.

| Key | Required | Notes |
|---|---|---|
| `Maxio:ApiKey` | yes | **Secret.** user-secrets in development, key vault/environment in production. Never in `appsettings*.json`. |
| `Maxio:Subdomain` | yes¹ | Maxio site subdomain; the base address is derived from it. |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are offered as plans. Also the guard that stops a caller subscribing to an unrelated product on the site. |
| `Maxio:BaseUrl` | no | Verbatim override of the API base address (EU hosting, a gateway, a test double). Overrides `Subdomain` when set. |
| `Maxio:DefaultPlanHandle` | no | Plan used when a request omits `planHandle`. Unset by default, so the build assumes nothing about any particular catalog. |
| `Maxio:PaymentCollectionMethod` | no | Overrides the derived collection method. |
| `Maxio:TimeoutSeconds` | no | Default 30. |
| `Maxio:MaxRetryAttempts` | no | Default 3 retries after the first attempt. |
| `Maxio:RetryBaseDelayMilliseconds` | no | Default 500; exponential backoff with jitter. |
| `Maxio:MaxConcurrentRequests` | no | Default 4, matching Maxio's per-site concurrency budget. |
| `Maxio:PlanCacheSeconds` | no | Default 60. Zero disables plan caching. |
| `Maxio:SiteCacheSeconds` | no | Default 3600. |
| `Maxio:IdempotencyWindowSeconds` | no | Default 60. |

¹ Not required if `Maxio:BaseUrl` is set.

Development setup:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

Misconfiguration is reported when the capability is *used* (`503`), not at startup — subscription
billing is additive, and a host without Maxio credentials should still serve the rest of the API.

## Talking to Maxio

`Infrastructure/Maxio` holds the integration:

| Type | Role |
|---|---|
| `MaxioApiClient` | One method per upstream endpoint. HTTP Basic (`apiKey:X`), snake_case JSON, retries on 429/408/5xx and transport failures with exponential backoff + jitter honouring `Retry-After`. The request message is rebuilt per attempt, so retried writes never resend disposed content. |
| `MaxioConcurrencyHandler` | Caps in-flight calls at `MaxConcurrentRequests`. Maxio queues excess concurrency rather than serving it faster, so we shape the load instead of getting throttled. |
| `MaxioSubscriptionBillingService` | Orchestration: plan resolution, customer ensure, idempotent subscribe, duplicate recovery. |
| `MaxioErrorTranslator` | Maps upstream status codes onto the `BillingException` family that `ExceptionMiddleware` turns into HTTP statuses. Upstream detail is summarised, never echoed wholesale, and credentials never appear in a message. |

Retrying POSTs is safe by construction: both writes this integration issues are guarded upstream —
customer creation by the unique `reference`, subscription creation by the `uniqueness_token` — so a
replayed write is rejected rather than duplicated.

Upstream endpoints used: `GET /site.json`, `GET /product_families/handle:{handle}/products.json`,
`GET /customers/lookup.json`, `POST /customers.json`,
`GET /customers/{id}/subscriptions.json`, `POST /subscriptions.json`.
