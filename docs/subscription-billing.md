# Subscription billing (Maxio Advanced Billing)

eShopOnWeb's catalog, basket and order flow is one-time commerce. Subscription billing is an
additive, parallel capability: shoppers can subscribe to recurring plans without any change to the
existing cart and checkout. **Maxio Advanced Billing is the system of record** for plans, customers
and subscriptions - nothing about them is stored in the eShopOnWeb databases.

## Endpoints

All three live on the `PublicApi` project and require a JWT bearer token; the subscriber is always
taken from the token, never from the request body.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | Plans available in the configured product family. |
| `POST /api/subscriptions` | Subscribe the caller to a plan. Body: `{ "planHandle": "..." }`. |
| `GET /api/my-subscriptions` | The caller's own subscriptions, newest first. |

`POST /api/subscriptions` answers `201 Created` when it creates a subscription and `200 OK` with
`alreadySubscribed: true` when the caller was already subscribed to that plan, so a repeated request
is safe and distinguishable.

Error responses use the standard `{ "statusCode": ..., "message": ... }` shape:

| Status | Meaning |
| --- | --- |
| `400` | No `planHandle`, or a handle that is not in the product family. |
| `401` | Missing or invalid bearer token. |
| `422` | The billing system refused the signup on business rules, e.g. the plan requires a card. |
| `502` | The billing system was unreachable or failed. Worth retrying. |

## Configuration

Bound from the `Maxio` configuration section. **Never commit these values** - supply them through
user secrets or environment variables.

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Site API key. Sent as the user name of the HTTP Basic credential. |
| `Maxio:Subdomain` | yes, unless `BaseUrl` is set | Site subdomain; the base address is derived as `https://{subdomain}.chargify.com/`. |
| `Maxio:ProductFamilyHandle` | yes | Handle of the product family holding the plans. |
| `Maxio:BaseUrl` | no | Explicit API base address. When set it is used verbatim instead of deriving one from the subdomain. |

Loading them from the environment into user secrets, from the repository root:

```bash
dotnet user-secrets set --project src/PublicApi/PublicApi.csproj "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set --project src/PublicApi/PublicApi.csproj "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set --project src/PublicApi/PublicApi.csproj "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

User secrets are only loaded in the `Development` environment. A missing or invalid section is
logged as a warning at start-up and disables the subscription endpoints; it does not stop the API.

## How it works

`ISubscriptionBillingService` (ApplicationCore) is the port; `MaxioSubscriptionBillingService`
(Infrastructure) is the Maxio adapter, sitting on a typed `MaxioApiClient`.

* **Plans** come from `GET /product_families/handle:{handle}/products.json`. Plans are addressed by
  handle throughout, because numeric ids are reassigned when a billing site is re-seeded.
* **Customers** are keyed on a namespaced reference, `eshoponweb:{account key}`, where the account
  key is the shopper's email address. The eShopOnWeb Identity user id is deliberately not used: it
  is regenerated every time the in-memory identity store is seeded.
* **Subscriptions** are created with `payment_collection_method` set to the site's invoiced mode
  (`remittance` on relationship-invoicing sites, `invoice` on statement-based ones), because this
  integration captures no payment details. A plan that requires a card is rejected up front.

### Idempotency

A double-clicked subscribe button must never produce two customers or two subscriptions. Five layers
cover that, each catching what the one before it cannot:

1. A per-account in-process lock serialises concurrent requests for the same shopper.
2. The billing customer is looked up by its stable reference before it is created; a create that
   loses the race is refused with "reference must be unique" and reconciled to the winner.
3. An existing live subscription to the same plan short circuits the signup and is returned as-is.
4. The create call carries a deterministic subscription reference. References are unique per site,
   so a duplicate that survives the first three layers - a race between two instances, say - is
   refused by the server with `422` and reconciled against the record that already exists.
5. The create call also carries a `uniqueness_token`, fresh per signup attempt. That covers the one
   case a reference cannot: a request the transport replayed after a timeout, where the server may
   already have accepted the first copy. The replay carries the same body and therefore the same
   token, comes back as `409`, and is reconciled the same way. The token is deliberately not derived
   from the reference - the server remembers a token for an hour, so a derived one would block
   signups for the rest of that hour whenever the record it belongs to is purged or re-seeded away.

Because the billing system is the system of record and the join is the shopper's email address, all
of this keeps working across restarts and across instances - including with
`UseOnlyInMemoryDatabase=true`, where the local stores are wiped on every restart.

### Resilience

`MaxioResilienceHandler` caps in-flight calls at the site's concurrency budget and retries `429`,
`408`, `5xx` and transport failures with exponential backoff, jitter and `Retry-After` support.
Retrying writes is safe precisely because of the de-duplication described above.
