# Recurring subscriptions with Maxio Advanced Billing

eShopOnWeb's original flow is one-time commerce: Catalog → Basket → Order. This adds a **second,
parallel** capability — recurring subscriptions — with **Maxio Advanced Billing** as the system of
record. Nothing about the cart or checkout changes; no subscription state is stored in the
eShopOnWeb database.

The capability is exposed as three JWT-authenticated endpoints on **`src/PublicApi`**. The
subscriber is always taken from the bearer token, never from the request body.

| Endpoint | Purpose |
|---|---|
| `GET /api/subscription-plans` | The plans on offer — the products in the configured Maxio product family. |
| `POST /api/subscriptions` | Subscribe the caller to a plan. Idempotent. |
| `GET /api/my-subscriptions` | The caller's own subscriptions, newest first. |

---

## Configuration

Everything binds from the `Maxio:` configuration section. **No value is committed to this
repository** — in development they live in .NET user-secrets, elsewhere in environment variables
(`Maxio__ApiKey`, …) or a secret store.

| Key | Required | Meaning |
|---|---|---|
| `Maxio:ApiKey` | yes | Site API key. Sent as the HTTP Basic user name with `X` as the password. |
| `Maxio:Subdomain` | yes, unless `BaseUrl` is set | Maxio site subdomain; the API address is derived as `https://{subdomain}.chargify.com/`. |
| `Maxio:ProductFamilyHandle` | yes | Handle of the product family holding the plans eShopOnWeb offers. |
| `Maxio:BaseUrl` | no | Absolute API base address. When set it is used **verbatim** instead of deriving one from the subdomain. |
| `Maxio:PaymentCollectionMethod` | no | Overrides how Maxio collects payment (`automatic`, `remittance`, `invoice`, `prepaid`). See [Payment collection](#payment-collection). |
| `Maxio:TimeoutSeconds` | no (30) | Per-call timeout. |
| `Maxio:MaxRetries` | no (3) | Retries for throttled/transient/transport failures. |
| `Maxio:RetryBaseDelayMilliseconds` | no (500) | First backoff step; later steps grow exponentially with jitter. |
| `Maxio:SiteCacheMinutes` | no (15) | How long the billing site's own settings are cached. |

Loading the sandbox credentials into user-secrets:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

Settings are validated the first time they are read, not at startup: a host with no `Maxio`
section still boots and serves the rest of eShopOnWeb, and only the subscription endpoints fail —
with `503` and a message naming the missing key.

---

## How it fits together

```
PublicApi/SubscriptionEndpoints      HTTP surface, DTOs, status codes
        │
        ▼
ApplicationCore                      ISubscriptionService → SubscriptionService
   Services/SubscriptionService.cs      the subscribe flow and its idempotency rules
   Interfaces/IBillingGateway.cs        the port onto whatever billing system is used
   Subscriptions/*                      vendor-neutral models
        │
        ▼
Infrastructure/Maxio                 MaxioBillingGateway — the only code that knows about Maxio
   MaxioSettings.cs                     configuration + base-address resolution
   Contracts/*                          wire shapes for the Maxio REST API
```

`ApplicationCore` never references Maxio. Swapping billing providers means writing another
`IBillingGateway`.

### Maxio endpoints used

| Call | Maxio endpoint |
|---|---|
| Read site settings (currency, invoicing model) | `GET /site.json` |
| List plans | `GET /product_families/handle:{familyHandle}/products.json` |
| Find a shopper's customer record | `GET /customers/lookup.json?reference={reference}` |
| Create a customer | `POST /customers.json` |
| List a customer's subscriptions | `GET /customers/{customerId}/subscriptions.json` |
| Subscribe | `POST /subscriptions.json` |

---

## The subscribe flow

`POST /api/subscriptions` with `{"planHandle": "<handle>"}`:

1. **Resolve the plan** by handle against the configured product family. Unknown handle → `404`
   listing the handles that do exist. Handles are matched case-insensitively; the catalog's
   canonical handle is what reaches Maxio.
2. **Take a per-shopper lock** so two simultaneous requests from the same account take turns.
3. **Ensure the Maxio customer exists.** The shopper is linked to Maxio by a deterministic
   reference, `eshoponweb-{normalised-email}` — derived from the account name rather than the
   Identity row id, because the in-memory database issues new row ids on every restart while the
   Maxio customer persists. Look it up; create it only if absent. If the create loses a race and
   Maxio reports the reference as taken, re-read and use the winner.
4. **Check for an existing enrollment.** If the shopper already holds a *live* subscription to this
   plan, return it with `200 OK` and `alreadySubscribed: true` — no second subscription.
5. **Create the subscription**, then return `201 Created` with plan, price, state and next billing
   date.

Live states are Maxio's live states plus the recoverable problem states (`past_due`,
`soft_failure`, `unpaid`) — a failed payment does not end a subscription, so it should not let a
duplicate be created. A `canceled` or `expired` subscription does not block subscribing again.

### Idempotency

Three layers, because they cover different failures:

| Failure | Covered by |
|---|---|
| Shopper clicks Subscribe twice in a row | Step 4 finds the live subscription and returns it. |
| Two requests genuinely in flight at once | The per-shopper lock (step 2) serialises them, so the second one reaches step 4 after the first has finished. |
| The caller retries a request whose response it never saw | The caller sends `idempotencyKey`; the same key always produces the same Maxio `uniqueness_token`, so the replay is rejected as a duplicate and the endpoint returns the subscription the first attempt created. |
| The gateway re-sends a create after a timeout | Every create carries a `uniqueness_token`, so Maxio refuses the second copy. |

Without an `idempotencyKey` a **fresh** token is generated per attempt, deliberately. Maxio
remembers a token for an hour whether the create succeeded *or failed*; a token derived from
shopper + plan would lock the shopper out of retrying for an hour after a failure they had already
fixed.

The per-shopper lock is a single-process guard. Across instances, protection is step 4 plus
`idempotencyKey`.

### Payment collection

eShopOnWeb's subscribe flow captures no card, and no 3-D Secure step is involved. Maxio's default
collection method is `automatic`, which tries to charge at signup and fails with *"No payment
method was on file"* even for a plan that does not require one.

So the gateway reads `GET /site.json` and creates subscriptions with the site's invoice-style
collection method — `remittance` on a Relationship Invoicing site, `invoice` on a statement-based
one. The subscription goes active immediately and the shopper is invoiced. Set
`Maxio:PaymentCollectionMethod` to override this if your deployment does capture payment methods.

---

## Talking to the API

Every endpoint needs a bearer token from `POST /api/authenticate` (the storefront cookie will not
work here).

```bash
API=https://localhost:5001   # your PublicApi https address

TOKEN=$(curl -sk -X POST $API/api/authenticate \
  -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  | python -c 'import json,sys; print(json.load(sys.stdin)["token"])')

curl -sk $API/api/subscription-plans -H "Authorization: Bearer $TOKEN"

curl -sk -X POST $API/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"planHandle":"eshop-pro"}'

curl -sk $API/api/my-subscriptions -H "Authorization: Bearer $TOKEN"
```

### `POST /api/subscriptions` body

| Field | Required | Notes |
|---|---|---|
| `planHandle` | yes | From `GET /api/subscription-plans`. |
| `firstName`, `lastName` | no | Recorded on the Maxio customer. Maxio requires both; when omitted they are derived from the email's local part (`ada.lovelace@…` → *Ada Lovelace*). |
| `organization` | no | Recorded on the Maxio customer. |
| `idempotencyKey` | no | Repeat it to retry a request safely. |

### Status codes

| Code | When |
|---|---|
| `201` | Subscribed. |
| `200` | Already subscribed to that plan; the existing subscription is returned with `alreadySubscribed: true`. |
| `400` | `planHandle` missing. |
| `401` | No or invalid bearer token. |
| `404` | Unknown `planHandle`; the message lists the valid ones. |
| `409` | A replayed `idempotencyKey` whose original attempt left no subscription behind. |
| `422` | Maxio rejected the request on its merits, e.g. the plan needs a payment method. |
| `502` | Maxio failed in a way the caller cannot fix, e.g. rejected credentials. |
| `503` | Maxio is unreachable or throttling, or the `Maxio` configuration section is missing. |

Errors use the API's existing shape: `{"StatusCode": 404, "Message": "..."}`.

---

## Operational notes

- **Retries.** Throttling (`429`), `408`, `5xx` and transport failures are retried with exponential
  backoff and jitter, honouring `Retry-After` when Maxio sends one. Maxio limits by *concurrency*
  (4 in-flight calls per site), so the backoff deliberately does not parallelise.
- **Secrets in logs.** The API key is never logged and never appears in an exception message; a
  rejected credential is reported as "check Maxio:ApiKey and Maxio:Subdomain".
- **Caching.** Site settings are cached (`Maxio:SiteCacheMinutes`). Plans and subscriptions are
  read live, so a catalog change in Maxio shows up on the next request.
- **Numeric ids are not stable.** Maxio reassigns product and family ids when a site is re-seeded,
  so everything here addresses the catalog by handle.

## Tests

- `tests/UnitTests/ApplicationCore/Services/SubscriptionServiceTests` — the subscribe flow:
  customer reuse, already-subscribed, resubscribe after cancellation, race recovery, uniqueness
  tokens, and concurrent subscribes producing exactly one create.
- `tests/UnitTests/ApplicationCore/Subscriptions` — customer-reference derivation and the
  per-shopper lock.
- `tests/UnitTests/Infrastructure/Maxio` — the gateway against a stubbed transport: request shapes,
  error mapping, retry behaviour, and base-address resolution.
