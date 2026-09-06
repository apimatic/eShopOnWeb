# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

`SubscriptionEndpoints/` adds recurring-subscription billing alongside the existing one-time
Catalog → Basket → Order flow. It does not change that flow; it runs beside it.

[Maxio Advanced Billing](https://docs.maxio.com) is the **system of record**. eShopOnWeb stores no
plans, customers or subscriptions of its own, so the feature behaves correctly across restarts even
when the app runs on the in-memory database, and it does not need a migration.

The integration is built against the OpenAPI specification in `maxio-spec/`. Every call maps to one
operation in that spec; see `src/Infrastructure/Maxio/IMaxioApiClient.cs`, where each member names
its `operationId` and path.

### Endpoints

All three require a JWT bearer token from `POST /api/authenticate`. The shopper is taken from the
token — never from the request body — so a caller can only read and change their own billing.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/subscription-plans` | Plans in the configured product family, with price, currency and billing period. |
| `POST` | `/api/subscriptions` | Subscribe the caller to a plan. `201` when the enrollment is new, `200` when they were already subscribed. |
| `GET` | `/api/my-subscriptions` | The caller's own subscriptions, newest first. |

`POST /api/subscriptions` body (all fields optional):

```json
{ "planHandle": "eshop-pro", "firstName": "Ada", "lastName": "Lovelace" }
```

`planHandle` may be omitted only when `Maxio:DefaultPlanHandle` is configured. `firstName` and
`lastName` are used solely when the shopper's billing customer record is first created; Maxio
requires a name and eShopOnWeb's identity model stores none, so they are otherwise derived from the
email address.

### Idempotency

Subscribing twice never produces two customers or two subscriptions. Three mechanisms combine:

1. The shopper's Maxio customer is addressed by a `reference` derived from their login name, and
   Maxio enforces that references are unique. A losing concurrent create comes back `422` and is
   resolved by re-reading the customer.
2. Before creating anything, the service checks the shopper's existing subscriptions and returns any
   live one for the same plan (`created: false`, `200 OK`).
3. Each subscription is stamped with a deterministic `reference` of `{customerReference}:{planHandle}`.
   Maxio enforces uniqueness here too, which catches a race between two app instances, where the
   in-process lock does not reach. Re-subscribing after a cancellation takes the next free suffix.

### Configuration

Bound from the `Maxio` section. **Never commit these values.**

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Advanced Billing API key. Sent as the HTTP Basic username with password `x`, per the spec's `BasicAuth` scheme. |
| `Maxio:Subdomain` | yes* | Substituted into the spec's server template `https://{site}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | yes | Handle of the product family holding the subscribable plans. Only plans in it are listed or subscribable. |
| `Maxio:BaseUrl` | no | Absolute base address override, used **verbatim** when set. Needed for EU-hosted sites (`https://{site}.ebilling.maxio.com`). |
| `Maxio:DefaultPlanHandle` | no | Plan used when a subscribe request omits `planHandle`. Unset, omitting it is a `400`. |
| `Maxio:PaymentCollectionMethod` | no | Default `remittance`. One of the spec's `Collection-Method` values. |
| `Maxio:CustomerReferencePrefix` | no | Default `eshoponweb`. Namespaces this app's customers on a shared site. |
| `Maxio:CatalogCacheSeconds` | no | Default `60`. Plan/site cache lifetime; `0` disables. |
| `Maxio:TimeoutSeconds` | no | Default `30`. Total budget per call, retries included. |
| `Maxio:MaxRetryAttempts` | no | Default `3`. Retries for 429/5xx and connection faults. |
| `Maxio:PageSize` | no | Default `200`, the spec maximum. |

\* Required unless `Maxio:BaseUrl` is set.

Load the credentials from your environment into user-secrets (they stay outside the repository):

```powershell
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY
dotnet user-secrets set "Maxio:DefaultPlanHandle" "eshop-pro"
```

Environment variables work too, using the double-underscore form: `Maxio__ApiKey`, and so on.

If configuration is missing the app still starts — it logs a warning at startup and the three
subscription endpoints return `503` with the names (not values) of the missing keys.

### Why `remittance` collection

Both demo plans have `require_credit_card: false`, but the sandbox site's default collection method
is `automatic`, which makes Advanced Billing attempt to collect the first period immediately and
reject the signup with *"No payment method was on file for the $299.00 balance"*. Creating the
subscription with `payment_collection_method: remittance` invoices the customer instead, so signup
succeeds without capturing a card or running 3-D Secure. Set
`Maxio:PaymentCollectionMethod=automatic` on deployments that attach a payment profile first.

### Failure mapping

| Condition | Status |
| --- | --- |
| Unknown plan, or a plan outside the configured product family | `404` |
| Missing plan handle with no configured default; archived plan; Maxio validation error | `400` |
| Maxio unreachable, throttled, failing, or clearing site data; integration not configured | `503` |
| Anything else from Maxio | `502` |

Maxio rejecting *this application's* API key is treated as a server-side configuration fault
(`503`), never as a client error. The API key is never logged and never appears in an error body.

### Layout

| Path | Contents |
| --- | --- |
| `src/ApplicationCore/Subscriptions/`, `Interfaces/ISubscriptionBillingService.cs` | Provider-neutral domain model and port. |
| `src/Infrastructure/Maxio/` | Options, typed spec client, retry handler, keyed lock, orchestration. |
| `src/Infrastructure/Maxio/Contracts/` | DTOs mirroring the spec schemas, each naming the schema it models. |
| `src/PublicApi/SubscriptionEndpoints/` | The three HTTP endpoints and their DTOs. |
| `tests/UnitTests/Infrastructure/Maxio/` | Idempotency, catalog scoping, options and error-parsing tests. |
| `tests/PublicApiIntegrationTests/SubscriptionEndpoints/` | Endpoint tests over the real host with a stubbed provider. |
