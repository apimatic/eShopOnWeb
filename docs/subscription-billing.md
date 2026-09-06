# Subscription billing (Maxio Advanced Billing)

eShopOnWeb's catalog → basket → order flow is one-time commerce. This capability adds a
**parallel** recurring-subscription flow with **Maxio Advanced Billing** as the billing system of
record. Nothing in the existing checkout path changed.

Everything Maxio-facing is built against the OpenAPI specification in [`maxio-spec/`](../maxio-spec):
operations, path and query parameters, request/response schemas, the `BasicAuth` security scheme,
the `https://{site}.chargify.com` server template, and the error schemas.

## Endpoints (`src/PublicApi`)

All three require a JWT bearer token from `POST /api/authenticate`. **The caller's identity comes
from the token only** — no request field names a user, so one shopper can never read or change
another's subscriptions.

| Method | Route | Purpose |
|--------|-------|---------|
| `GET` | `/api/subscription-plans` | Plans available in the configured product family |
| `POST` | `/api/subscriptions` | Subscribe the caller to a plan (idempotent) |
| `GET` | `/api/my-subscriptions` | The caller's subscriptions, newest first |

`POST /api/subscriptions` takes `{ "planHandle": "eshop-pro" }`, plus optional `firstName` /
`lastName` recorded on the billing account. It answers **201** with `created: true` when it enrolled
the shopper, and **200** with `created: false` when the shopper was already subscribed and the
existing subscription was returned.

Status codes: `400` unknown/missing plan handle shape, `401` missing or invalid token, `404` plan
not in the configured family, `502` Maxio rejected the call or is unreachable, `503` billing is not
configured (the response names the missing keys).

## Configuration

Bound from the `Maxio` configuration section. No site or catalog value is baked into the build —
the same binaries run against a different Maxio site and a different catalog by changing
configuration only.

| Key | Required | Meaning |
|-----|----------|---------|
| `Maxio:ApiKey` | yes | API key; sent as the basic-auth user name with password `x` |
| `Maxio:Subdomain` | yes, unless `BaseUrl` is set | Fills the `site` variable of the spec's server template |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are sold as plans |
| `Maxio:BaseUrl` | no | Absolute base address used **verbatim** instead of deriving one from the subdomain (EU-hosted sites, a recording proxy) |
| `Maxio:PaymentCollectionMethod` | no | Overrides the collection method chosen for new subscriptions |
| `Maxio:Timeout` | no | Per-request timeout (default 30s) |
| `Maxio:MaxRetries` | no | Transient-failure retries (default 3) |

**Secrets never go in the repository.** `appsettings.json` carries the key *names* with empty
values; supply the real ones through user-secrets or the environment:

```bash
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"              --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"       --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

Registration never throws. An application without Maxio settings still starts and serves everything
else; only the three subscription endpoints report `503` with the missing key names.

## How idempotency works

eShopOnWeb stores **no** subscription state of its own — deliberately, because the demo runs on the
in-memory provider, which loses everything on restart. Maxio is the only system of record, reached
through deterministic references that Maxio enforces as unique per site:

- customer reference — `eshoponweb--<sanitised user name>` (e.g. `eshoponweb--demouser-microsoft-com`)
- subscription reference — `<customer reference>--<plan handle>`, with `--2`, `--3`… for a
  re-subscribe after an earlier subscription to the same plan ended

Subscribing therefore:

1. resolves the plan handle against the configured product family (an unknown handle is a `404`);
2. takes a per-shopper in-process lock, so the two halves of a double-click cannot interleave;
3. looks the customer up by reference and creates it only if absent — a lost creation race is
   recovered by re-reading the winner's record;
4. returns the shopper's existing live subscription to that plan if there is one;
5. otherwise walks reference slots: a slot held by an ended subscription is skipped, a free slot is
   used to create, and a `422 Reference: must be unique` from a concurrent instance is recovered by
   re-reading that reference.

The lock is a latency optimisation; steps 3–5 are what make this correct across processes,
restarts and multiple application instances.

## Payment collection

The demo never captures card data, and Maxio refuses a signup on `automatic` collection when no
payment profile is on file. New subscriptions are therefore created on an invoice-style collection
method, chosen from the site's own billing architecture: `remittance` on Relationship Invoicing
sites, `invoice` on legacy Statements sites. `Maxio:PaymentCollectionMethod` overrides that.

## Layout

| Path | Contents |
|------|----------|
| `src/ApplicationCore/Subscriptions/` | Provider-agnostic models (`SubscriptionPlan`, `CustomerSubscription`, `Subscriber`, `SubscribeResult`) and `ISubscriptionBillingService` |
| `src/ApplicationCore/Exceptions/` | `SubscriptionPlanNotFoundException`, `BillingProviderException`, `BillingNotConfiguredException` |
| `src/Infrastructure/Billing/Maxio/` | Options, typed API client, wire contracts, transient-fault handler, the orchestration service, DI wiring |
| `src/PublicApi/SubscriptionEndpoints/` | The three endpoints, their request/response contracts and DTO mapping |
| `tests/UnitTests/{ApplicationCore,Infrastructure}/…` | Reference derivation, state classification, options validation, client wire behaviour, enrollment idempotency |
| `tests/PublicApiIntegrationTests/SubscriptionEndpoints/` | Authorization coverage for the three routes |

## Maxio operations used

Every call maps to an operation in the specification:

`readSite` · `listProductsForProductFamily` (family addressed as `handle:<handle>`, so no numeric
ids are baked in) · `readCustomerByReference` · `createCustomer` · `listCustomerSubscriptions` ·
`findSubscription` · `createSubscription`.
