# Subscription billing (Maxio Advanced Billing)

Recurring-subscription billing for eShopOnWeb, with **Maxio Advanced Billing** as the billing system
of record. It runs **alongside** the existing catalog → basket → order flow and changes none of it.

## Endpoints

All three are JWT authenticated; the subscriber is taken from the bearer token, never from the
request body. Get a token from `POST /api/authenticate` first.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET`  | `/api/subscription-plans` | Plans on offer, cheapest first. |
| `POST` | `/api/subscriptions` | Subscribe the caller to a plan. Body: `{ "planHandle": "eshop-pro" }`. |
| `GET`  | `/api/my-subscriptions` | The caller's subscriptions, most recent first. |

`POST /api/subscriptions` answers `201 Created` when it enrolled the caller and `200 OK` with
`alreadySubscribed: true` when the caller already held a live subscription to that plan.

Failures map to: `400` malformed request · `401` no or unusable token · `404` unknown plan ·
`422` the provider refused the enrollment · `502` the provider is unreachable ·
`503` this host has no Maxio configuration.

## Configuration

Bound from the `Maxio` section. The API key is a secret: supply it through user-secrets in
development, and environment variables or a vault elsewhere. It must never be written into a file in
this repository.

| Key | Required | Meaning |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Advanced Billing API key (`MAXIO_API_KEY`). |
| `Maxio:Subdomain` | unless `BaseUrl` is set | Site subdomain (`MAXIO_SITE_SUBDOMAIN`). |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are the plans (`MAXIO_DEFAULT_PRODUCT_FAMILY`). |
| `Maxio:BaseUrl` | no | API base address, used **verbatim** when set; overrides `Subdomain` and `Environment`. |
| `Maxio:Environment` | no | `US` (default) or `EU`. Selects the regional host (`MAXIO_ENVIRONMENT`). |
| `Maxio:PaymentCollectionMethod` | no | `remittance` (default) or `automatic`. |
| `Maxio:ReferencePrefix` | no | Prefix on generated references. Default `eshoponweb`. |
| `Maxio:TimeoutSeconds` | no | Per-call HTTP timeout. Default `30`. |
| `Maxio:MaxRetryAttempts` | no | Retries after a transient provider failure. Default `3`. |
| `Maxio:RetryBaseDelayMilliseconds` | no | Base of the exponential backoff. Default `250`. |
| `Maxio:PlanCacheSeconds` | no | Plan catalog cache lifetime; `0` disables. Default `60`. |

Loading the sandbox credentials into user-secrets:

```bash
dotnet user-secrets --project src/PublicApi set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets --project src/PublicApi set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets --project src/PublicApi set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
dotnet user-secrets --project src/PublicApi set "Maxio:Environment"         "$MAXIO_ENVIRONMENT"
```

When `Maxio:ApiKey` is present the settings are validated at startup, so a wrong subdomain or product
family is caught before the first shopper. When it is absent the host still starts — the rest of
eShopOnWeb does not depend on billing — and the three endpoints answer `503`.

## How it is put together

```
ApplicationCore/Interfaces/ISubscriptionBillingService.cs   the port
ApplicationCore/Subscriptions/*                            Subscriber, SubscriptionPlan, Subscription
ApplicationCore/Exceptions/Subscription*Exception.cs       failure vocabulary
Infrastructure/Billing/Maxio/*                             the Advanced Billing adapter
PublicApi/SubscriptionEndpoints/*                          HTTP surface
```

eShopOnWeb stores **no** subscription state of its own. Plans, customers and subscriptions are read
from and written to Advanced Billing on every call, keyed by references derived from the eShopOnWeb
user. Nothing to migrate, nothing to reconcile, and the flow survives a restart even on the in-memory
database.

### Idempotency

Advanced Billing enforces that a customer `reference` and a subscription `reference` are unique per
site. This integration derives both deterministically, and that constraint — not a local lock — is
what makes subscribing safe to repeat:

- customer: `eshoponweb-<slug of user name>-<digest>`
- subscription: `<customer reference>--<plan handle>`, with `--2`, `--3` … used only when an earlier,
  now finished subscription already holds the slot (a shopper re-subscribing after cancelling).

Subscribing therefore:

1. resolves the plan from the configured product family, by handle;
2. looks the customer up by reference and creates it only if absent — a `422` "reference must be
   unique" from a competing caller is resolved by reading the customer back;
3. returns the existing subscription, unchanged, if the shopper already holds a live one on that plan;
4. otherwise creates the subscription with the derived reference. A `422` "reference must be unique"
   means either a concurrent caller won the race — its subscription is returned — or the slot holds a
   finished subscription, in which case the next slot is tried.

An in-process lock per customer reference collapses the common double click into one round trip; the
unique reference is what keeps it correct across processes and instances.

### Resilience

`MaxioRetryHandler` retries 429 and 5xx responses and transport failures with exponential backoff and
jitter (Advanced Billing rate limits per site and sends no `Retry-After`). Writes are retried too,
because the unique reference turns a duplicated write into a duplicate-reference error the caller
already knows how to resolve.

### Payment methods

The seeded plans do not require a stored payment method, and subscriptions are created with
`payment_collection_method: remittance`, so a shopper can subscribe without card capture. A plan that
does require a card is rejected up front with `422` rather than sent to the provider — capturing card
details would mean Maxio.js and 3-D Secure, which this integration does not do.

## Provenance of the API contract

Every endpoint, field and error shape used here was confirmed against the official Maxio .NET SDK
([maxio-com/ab-dotnet-sdk](https://github.com/maxio-com/ab-dotnet-sdk), release 10.0.0) and then
exercised against a live Advanced Billing sandbox site:

| Operation | Endpoint |
| --- | --- |
| Site (currency) | `GET /site.json` |
| Plans in a family | `GET /product_families/handle:{handle}/products.json` |
| Find customer | `GET /customers/lookup.json?reference=` |
| Create customer | `POST /customers.json` |
| Find subscription | `GET /subscriptions/lookup.json?reference=` |
| Create subscription | `POST /subscriptions.json` |
| Customer's subscriptions | `GET /subscriptions.json?customer_id=` |

Authentication is HTTP basic with the API key as the user name and the literal `x` as the password.
Product families and products are addressed by handle (`handle:<handle>`) because Advanced Billing
reassigns numeric ids when a site is re-seeded.

The SDK itself is not referenced: it resolves its base address from a fixed `{site}.chargify.com` /
`{site}.ebilling.maxio.com` template and cannot be pointed at an arbitrary `Maxio:BaseUrl`, which this
integration has to support. The adapter is a thin typed `HttpClient` over the same REST API instead.
