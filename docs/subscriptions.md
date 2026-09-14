# Recurring subscriptions (Maxio Advanced Billing)

eShopOnWeb's one-time flow (Catalog -> Basket -> Order) is untouched. This is a second, parallel
capability: a logged-in shopper browses plans, subscribes to one, and sees it on their account.

**Maxio Advanced Billing is the system of record.** eShopOnWeb stores no plans, no customers and no
subscriptions of its own, and adds no database tables or migrations.

## Endpoints

All three live on `src/PublicApi` and require a JWT bearer token. The caller is taken from the
token only - never from the request body - so no shopper can enroll or inspect another.

| Method | Route | Purpose |
| ------ | ----- | ------- |
| `GET`  | `/api/subscription-plans` | Plans on offer, projected from the configured product family. |
| `POST` | `/api/subscriptions`      | Enroll the caller on a plan. `201` on enrollment, `200` when already enrolled. |
| `GET`  | `/api/my-subscriptions`   | Every subscription the caller holds. |

`POST /api/subscriptions` takes `{ "planHandle": "<handle>" }`, optionally with
`"paymentCollectionMethod": "remittance" | "automatic" | "prepaid"`. It also accepts an
`Idempotency-Key` header - see below.

## Configuration

Bound from the `Maxio` section. Supply them through user secrets, environment variables
(`Maxio__ApiKey`, ...) or your platform's secret store - never through a file in this repository.

| Key | Required | Meaning |
| --- | -------- | ------- |
| `Maxio:ApiKey` | yes | Site API key. Sent as the user name of HTTP Basic auth over TLS. |
| `Maxio:Subdomain` | yes, unless `BaseUrl` is set | Billing site subdomain; the API base address is derived from it. |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address when set, for EU-hosted sites or a gateway host. |
| `Maxio:TimeoutSeconds` | no (30) | Budget for a single API attempt. |
| `Maxio:MaxAttempts` | no (3) | Attempts for a retryable call, including the first. |
| `Maxio:CatalogCacheSeconds` | no (60) | How long the plan list is reused. `0` disables caching. |

With no credentials the application starts normally and the one-time commerce flow is unaffected;
only these three endpoints answer `503`, naming the keys that are missing.

## How identity is mapped

The billing customer's `reference` is a pure function of the eShopOnWeb user name
(`eshoponweb-<username>`, see `BillingReferences`). Nothing local has to be stored or kept in sync,
and the mapping survives an application restart even on the in-memory database. The provider
enforces uniqueness on that value, which is what makes "ensure a customer exists" idempotent.

## How double-clicks are handled

A subscribe request is safe to repeat. In order:

1. Requests for the same shopper are serialised in-process, so a double-click observes the first
   request's outcome rather than racing it.
2. The customer is created only when the lookup by reference finds none; if another caller wins
   that race, the `422 Reference: must be unique` response is resolved by re-reading the record.
3. If the shopper already has a live subscription to the plan, it is returned with
   `alreadySubscribed: true` and nothing is created.
4. The create carries a `uniqueness_token`, so a request that times out on our side can be retried
   without risking a second enrollment. If the provider answers `409`, the subscription that won
   the race is looked up and returned.

The token is derived from the shopper, the plan and one more ingredient: the `Idempotency-Key`
header when the caller sends one, otherwise a five-minute bucket. Send the header with a value that
is stable per user gesture - a form submission id, say - and retries are recognised however far
apart they arrive. Without it, retries are still recognised for five minutes, which covers a
double-click and our own retry budget. The bucket matters: a token that never changed would keep
the provider rejecting a shopper's requests as duplicates long after they cancelled, locking them
out of subscribing again.

Only end-of-life states (`canceled`, `expired`, `failed_to_create`, `trial_ended`) count as "not
subscribed". Recoverable problem states such as `past_due` do not, so an unhealthy subscription is
never silently duplicated.

## Payment collection

Both demo plans have "payment method required" switched off, but the site collects `automatic` by
default, which fails the signup charge when there is no card on file. eShopOnWeb captures no card
details, so subscriptions default to `remittance`: the shopper is invoiced instead of charged.
Callers that do have a payment profile can pass `automatic` explicitly.

## Handles, not ids

Plans are addressed by handle everywhere. Numeric product and price-point ids are reassigned when a
billing site is re-seeded, so they are never persisted, configured or hard-coded.

## Layering

| Layer | What lives there |
| ----- | ---------------- |
| `ApplicationCore/Subscriptions` | Provider-agnostic models, the reference mapping and state rules. |
| `ApplicationCore/Interfaces` | `ISubscriptionService` (the capability) and `ISubscriptionBillingGateway` (the port). |
| `ApplicationCore/Services/SubscriptionService` | The subscribe orchestration and its idempotency. |
| `Infrastructure/Billing/Maxio` | The only code that knows about Maxio: transport, retries, DTOs, error translation. |
| `PublicApi/SubscriptionEndpoints` | HTTP surface and DTOs. |

Nothing that mentions Maxio, HTTP status codes or credentials escapes the infrastructure layer.
