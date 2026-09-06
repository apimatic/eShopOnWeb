# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

`SubscriptionEndpoints/` adds recurring-subscription billing alongside the existing one-time
Catalog → Basket → Order flow. It does not replace or touch that flow. **Maxio Advanced Billing is the
system of record**: eShopOnWeb stores no subscription state and reads everything back from Maxio, so
the feature behaves correctly even when the app runs on the in-memory database.

See [docs/subscription-billing.md](../../docs/subscription-billing.md) for the design, the Maxio
endpoints used, and the idempotency model.

### Endpoints

All three require a JWT bearer token from `POST /api/authenticate`. The subscriber is always the
caller identified by the token — no endpoint accepts a user identifier from the request.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/subscription-plans` | Plans on offer in the configured product family, cheapest first. |
| `POST` | `/api/subscriptions` | Subscribe the caller to a plan. Idempotent per user and plan. |
| `GET` | `/api/my-subscriptions` | Every subscription the caller holds, newest first. |

`POST /api/subscriptions` takes an optional body of `{ "planHandle": "<handle>" }`. With no body it
uses `Maxio:DefaultPlanHandle`, or the only plan on offer when the product family has exactly one.
It answers `201` when it creates a subscription and `200` with `"alreadySubscribed": true` when the
caller already holds a live subscription to that plan.

### Configuration

Settings bind from the `Maxio` configuration section. Nothing is hard-coded, so the same build runs
against a different Maxio site and a different catalog by configuration alone.

| Key | Required | Meaning |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Maxio API key. **Secret** — never commit it. |
| `Maxio:Subdomain` | yes, unless `BaseUrl` is set | Site subdomain, e.g. the `acme` in `acme.chargify.com`. |
| `Maxio:ProductFamilyHandle` | yes | Handle of the product family whose products are published as plans. |
| `Maxio:BaseUrl` | no | Overrides the API base address; used verbatim instead of deriving one from the subdomain. Needed for EU-hosted sites (`https://{site}.ebilling.maxio.com`). |
| `Maxio:DefaultPlanHandle` | no | Plan used when a request names none. |
| `Maxio:PaymentCollectionMethod` | no | Defaults to `remittance`. See the design doc for why. |
| `Maxio:CatalogCacheDuration` | no | Defaults to 5 minutes. |
| `Maxio:Timeout` | no | Per-call budget including retries. Defaults to 30 seconds. |
| `Maxio:MaxRetryAttempts` | no | Defaults to 3. |
| `Maxio:ReferencePrefix` | no | Namespaces the references written into Maxio. Defaults to `eshoponweb`. |

Supply the secret through .NET user-secrets in development, or environment variables
(`Maxio__ApiKey`) or a key vault elsewhere:

```bash
dotnet user-secrets --project src/PublicApi set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets --project src/PublicApi set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets --project src/PublicApi set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
dotnet user-secrets --project src/PublicApi set "Maxio:DefaultPlanHandle"   "<your default plan handle>"
```

An application with no Maxio configuration still starts and serves every other endpoint; only the
three subscription routes fail, with `503` and a message naming the keys to set.
