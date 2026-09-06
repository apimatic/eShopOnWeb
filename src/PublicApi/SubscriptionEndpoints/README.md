# Subscription billing endpoints

Recurring-subscription billing backed by **Maxio Advanced Billing**. This capability is *additive* — it runs
alongside the existing catalog → basket → order flow and replaces none of it.

All three routes are JWT-authenticated; the shopper is taken from the token and never from the request body.

| Route | Purpose |
|---|---|
| `GET /api/subscription-plans` | The plans offered by the configured product family |
| `POST /api/subscriptions` | Subscribe the caller to a plan (idempotent) |
| `GET /api/my-subscriptions` | The caller's subscriptions: plan, price, state, next billing date |

## Configuration

Bound from the `Maxio` section. Nothing has a hard-coded default, so the same build runs against a different
Maxio site and a different catalog.

| Key | Meaning |
|---|---|
| `Maxio:ApiKey` | Site API key (sent as the basic-auth user name) |
| `Maxio:Subdomain` | Maxio site subdomain, substituted into the default base URL |
| `Maxio:ProductFamilyHandle` | Handle of the product family whose products are the sellable plans |
| `Maxio:BaseUrl` | *Optional.* When set, used verbatim as the API base address instead of deriving one from the subdomain |

In development these come from .NET user-secrets, so no credential is ever committed:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

A deployment with no Maxio configuration still serves the rest of the API: the settings are validated when the
client is first built, and only these three routes report `503`.

## How it is put together

```
ApplicationCore   ISubscriptionBillingService + SubscriptionPlan / CustomerSubscription / SubscriberIdentity
                  BillingException — the one failure type the rest of the app sees
Infrastructure    Billing/Maxio/* — the SDK adapter, the only place that knows Maxio exists
PublicApi         SubscriptionEndpoints/* — routes, DTOs, and the identity-from-token rule
```

### Idempotency

A double-click must never produce two customers or two subscriptions.

* **Customer** — keyed on `SubscriberIdentity.Reference`, a deterministic value derived from the eShopOnWeb
  user name (`eshoponweb-<username>`). Maxio permits only one customer per reference, so lookup-then-create is
  safe; a rejected create is settled by re-reading rather than by parsing an error body. Nothing is stored
  locally, so the mapping survives a restart even on the in-memory database.
* **Subscription** — before creating, the caller's existing subscriptions are read and one to the same plan in
  a non-terminal state is returned instead (`alreadySubscribed: true`, HTTP `200` rather than `201`).
* **Concurrency** — same-shopper requests are serialised in-process, so parallel clicks queue behind the guard.
* **Transport** — the SDK resends a request on a connection failure regardless of HTTP verb. A create is held
  to a single outbound send (`MaxioWriteGuard`), and an unresolved write is settled by re-reading Maxio rather
  than assumed to have failed.

### Payment methods

This integration captures no payment method, so subscriptions are created with the site's non-automatic
collection method (`remittance` on Relationship Invoicing, `invoice` on legacy Statements) and Maxio invoices
rather than attempting to charge a card. There is no card capture and no 3-D Secure step.
