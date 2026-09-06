# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

eShopOnWeb's one-time flow is Catalog → Basket → Order. Recurring subscriptions run **alongside** it
as a parallel capability and share none of its state: **Maxio Advanced Billing is the system of
record**, and eShopOnWeb persists no subscription rows at all. Every read goes to Maxio, so nothing
can drift out of sync — and the in-memory database's habit of forgetting everything on restart
cannot lose a subscription.

### Endpoints

All three require a JWT bearer token from `POST /api/authenticate`. The caller's identity is taken
from the token; a request body can never assert one.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | Plans on offer, read live from the configured Maxio product family, cheapest first. |
| `POST /api/subscriptions` | Subscribes the caller to a plan. `201` on a new enrollment, `200` when already subscribed. |
| `GET /api/my-subscriptions` | The caller's own subscriptions, most recent first, including ended ones. |

`POST /api/subscriptions` body:

```json
{ "planHandle": "eshop-pro", "firstName": "Ada", "lastName": "Lovelace", "idempotencyKey": "checkout-42" }
```

Only `planHandle` is required. `firstName` / `lastName` populate the billing record — Maxio rejects a
customer whose name is blank, and eShopOnWeb identities carry no name, so when they are omitted a
name is derived from the caller's e-mail address. `idempotencyKey` is optional; see below.

Error mapping: `400` for a request Maxio refused (e.g. no payment method on file), `404` for an
unknown plan handle, `409` when a concurrent subscribe is still in flight, `502` for a Maxio outage.

### Configuration

Bound from the `Maxio` section. **No credential value belongs in a file inside this repository.**

| Key | Required | Meaning |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Site API key. Sent as the HTTP Basic user name (the password is the literal `x`). |
| `Maxio:Subdomain` | yes | Site subdomain — `acme` addresses `https://acme.chargify.com`. |
| `Maxio:ProductFamilyHandle` | yes | Handle of the product family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | Absolute override for the API address. When set it is used verbatim instead of deriving one from the subdomain. |
| `Maxio:PaymentCollectionMethod` | no | `remittance` (default) or `automatic`. |
| `Maxio:CustomerReferencePrefix` | no | Defaults to `eshoponweb:`. |
| `Maxio:Timeout`, `Maxio:MaxRetries`, `Maxio:SiteCacheDuration` | no | Transport tuning. |

Settings are validated at startup, so a misconfigured site fails to boot rather than failing one
endpoint at a time.

In development, load the credentials into user-secrets (they are stored in your user profile, not in
the repository):

```bash
dotnet user-secrets --project src/PublicApi set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets --project src/PublicApi set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets --project src/PublicApi set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

Outside Development, supply them as `Maxio__ApiKey`, `Maxio__Subdomain` and
`Maxio__ProductFamilyHandle` environment variables (or any other configuration provider).

Handles — not numeric ids — identify everything, because Maxio reassigns ids when a catalog is
re-seeded. The product family is addressed as `product_families/handle:<handle>/products.json`.

`Maxio:PaymentCollectionMethod` defaults to `remittance` because eShopOnWeb captures no card:
under `automatic` collection Maxio refuses signup for a non-zero balance with *"No payment method was
on file"*. Remittance issues an invoice instead, which is how a card-free signup is meant to work.

### Idempotency

Subscribing is safe to repeat — a double-click, a retry, or two instances racing all resolve to one
customer and one subscription. Three layers, each covering what the one before it cannot:

1. **A per-user in-process lock**, so concurrent requests on one instance queue instead of racing.
2. **A read-before-write check** for a subscription to the same plan the shopper is still enrolled
   in. This holds across instances, restarts and any length of time. Ended subscriptions (canceled,
   expired, failed) are ignored, so re-subscribing after a cancellation works.
3. **An application-chosen `reference` on every write.** Maxio enforces references as unique per
   site, so the loser of a genuine race is refused with `422` and reads the winner back instead of
   creating a duplicate. The customer's reference is derived from the user name; the subscription's
   is `<customer reference>|<plan handle>|<scope>`, where the scope is the caller's
   `idempotencyKey` when supplied and otherwise a generation number that only advances once a
   previous subscription to that plan has ended.

Maxio also offers a `uniqueness_token` replay guard. This integration deliberately **does not** use
it: the token is consumed even when the request it accompanied was *rejected*, so a single failed
attempt would lock the shopper out of retrying the same subscribe for the length of the replay
window. The unique reference gives stronger protection with no such trap.

### Layout

| Path | Contents |
| --- | --- |
| `src/ApplicationCore/Subscriptions/` | Provider-agnostic plan, subscription, request and result models. |
| `src/ApplicationCore/Interfaces/ISubscriptionBillingService.cs` | The capability the endpoints depend on. |
| `src/ApplicationCore/Exceptions/Billing*.cs` | Failure types the API maps to status codes. |
| `src/Infrastructure/Billing/Maxio/` | Settings, the typed HTTP client and its wire contracts, and the Maxio implementation. |
| `src/PublicApi/SubscriptionEndpoints/` | The three endpoints and their DTOs. |
| `tests/UnitTests/Infrastructure/Billing/Maxio/` | Idempotency, mapping, retry and wire-contract tests. |

The Maxio client is hand-written rather than taken from the published `Maxio.AdvancedBillingSdk`
package. The integration touches six endpoints, and writing them directly keeps the transport under
our control — `Maxio:BaseUrl` honoured verbatim, `IHttpClientFactory` pooling, a per-attempt timeout,
retries that respect `Retry-After`, and query strings kept out of the logs — without taking a large
generated dependency into a reference application. Every request shape and response field it maps was
confirmed against a live Maxio sandbox before being written.
