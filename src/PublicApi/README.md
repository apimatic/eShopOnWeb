# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

Alongside the one-time Catalog → Basket → Order flow, the API exposes recurring-subscription
billing. It is an additive capability: nothing in the existing cart or checkout path changes.

**Maxio Advanced Billing is the system of record.** No customer or subscription data is stored
in the eShopOnWeb database. The two systems are linked by a customer `reference` derived from the
authenticated user (`eshop-<username>`), so the mapping survives restarts even when the app runs
against the in-memory database.

### Endpoints

All three require a JWT bearer token from `POST /api/authenticate`. The caller's identity comes
from the token; there is no user identifier anywhere in a request body or route.

| Endpoint | Purpose |
|---|---|
| `GET /api/subscription-plans` | Plans a shopper can subscribe to, taken from the configured Maxio product family. |
| `POST /api/subscriptions` | Subscribes the caller to a plan. Body: `{ "planHandle": "..." }` using a handle from the plans endpoint; `pricePointHandle` and `idempotencyKey` are optional. |
| `GET /api/my-subscriptions` | The caller's subscriptions, newest first, with plan, price, state and next billing date. |

`POST /api/subscriptions` is **safe to repeat**. A signup that would duplicate an existing one
returns `200 OK` with `"created": false` and the subscription that already exists; a genuinely new
signup returns `201 Created` with `"created": true`. Three mechanisms back that up:

1. Signups for one user are serialised in-process, so a double-click cannot race the check below.
2. Before creating, the customer's existing subscriptions are read; one already on the plan in a
   non-terminal state is returned as-is. Ended subscriptions (`canceled`, `expired`, `trial_ended`,
   `failed_to_create`) do not block signing up again.
3. Every create carries a Maxio `uniqueness_token`, so a replayed HTTP request is rejected by Maxio
   with `409` rather than creating a second subscription. Supply your own key with an
   `Idempotency-Key` header (or an `idempotencyKey` body field) to extend that across processes.

Customer creation is idempotent the same way: the customer is looked up by reference first, and a
lost race (Maxio enforces reference uniqueness) is resolved by re-reading rather than failing.

### Configuration

Bound from the `Maxio` configuration section. **Never commit these values.** Use user-secrets in
development and a secret store in production; the `Maxio__ApiKey` environment-variable form works too.

| Key | Required | Meaning |
|---|---|---|
| `Maxio:ApiKey` | yes | API key, sent as the HTTP Basic user name. |
| `Maxio:Subdomain` | yes, unless `BaseUrl` is set | Maxio site subdomain; the base address is derived as `https://<subdomain>.chargify.com/`. |
| `Maxio:ProductFamilyHandle` | yes | Handle of the product family whose products are published as plans. Only plans in this family can be subscribed to. |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address when set, instead of deriving one from the subdomain. |

```bash
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"              --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"       --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

Plans and prices are always read from Maxio by handle. Numeric Maxio ids are never hard-coded,
because they are reassigned when a site is re-seeded.

The derived base address targets Maxio's US domain. For a site hosted elsewhere, set
`Maxio:BaseUrl` to that site's API address instead of relying on the subdomain.

### Behaviour worth knowing

- **No card is collected.** Subscriptions are created with `payment_collection_method: remittance`,
  so Maxio invoices the customer instead of charging a stored card. Left to the site default, Maxio
  would try to capture the first period at signup and reject it with *"No payment method was on
  file"*, even for plans that do not require a payment profile. Taking a card would mean a
  Billing.js token exchange and a 3-DS flow, which this capability deliberately avoids.
- **Failures are separated by who can act on them.** A missing or wrong credential answers `503`
  with the setting to fix; an upstream Maxio failure answers `502`; a request Maxio rejects as
  invalid answers `422` with Maxio's own messages; an unknown plan answers `404`.
- **Throttling is respected.** Maxio limits by concurrency and answers overload with `429`. Those
  responses (and transient 5xx on reads) are retried with exponential backoff and jitter, honouring
  `Retry-After`. Writes are only replayed when a uniqueness token makes a duplicate detectable.
- **Startup is never blocked.** If billing is unconfigured, the app still boots and logs a warning;
  only the subscription endpoints fail.
- The plan list is cached in memory for 60 seconds, so catalog edits in Maxio take up to a minute
  to appear.

### Where the code lives

| Path | Role |
|---|---|
| `src/ApplicationCore/Subscriptions` | Provider-agnostic model: `ISubscriptionService`, plans, subscriptions, billing exceptions. |
| `src/Infrastructure/Maxio` | The Maxio implementation: HTTP client, retry handler, settings, signup workflow. |
| `src/PublicApi/SubscriptionEndpoints` | The three HTTP endpoints and their DTOs. |
