# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio Advanced Billing)

Recurring subscriptions run alongside the one-time Catalog → Basket → Order flow, with Maxio Advanced Billing as the
system of record. All three endpoints require a JWT from `POST /api/authenticate`; the buyer is always the token's user.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | Non-archived products of the configured Maxio product family (`truncated` is true if the list hit its page cap). |
| `POST /api/subscriptions` | `{ "planHandle": "...", "firstName"?: "...", "lastName"?: "..." }` — ensures one Maxio customer per user, then one subscription per user and plan. `201` created, `200` already subscribed, `202` sent but not yet confirmed (see `GET /api/my-subscriptions`), `409` another request for the same plan is in flight. |
| `GET /api/my-subscriptions` | The caller's Maxio subscriptions (plan, price, state, next billing date) plus any unconfirmed requests. |

### Configuration (`Maxio:` section)

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Site API key. The host refuses to start when it is missing or blank. |
| `Maxio:Subdomain` | unless `BaseUrl` is set | Site subdomain; the API address is `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | yes | Handle (not id) of the product family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address instead of the subdomain-derived one. |
| `Maxio:PaymentCollectionMethod` | no | Default `remittance` (invoiced; no card is captured by this flow). Use `invoice` on a legacy Statements site. |
| `Maxio:AttemptTimeoutSeconds` / `Maxio:OperationBudgetSeconds` / `Maxio:PlanCacheSeconds` | no | Defaults 10 / 30 / 60. |

Never put these values in `appsettings*.json`. For local development load them into user-secrets from the environment:

```powershell
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi
```

Deployed environments supply the same keys as environment variables (`Maxio__ApiKey`, ...) or from a secret store.
The key is read once at startup, so a rotated key takes effect on restart.

### Building

The Maxio .NET SDK is not on NuGet; it ships as source in the `maxio-advanced-billing` Claude Code plugin.
`PublicApi.csproj` references it at a default path beside this clone. Elsewhere, build with
`-p:MaxioAdvancedBillingSdkProject=<path to MaxioAdvancedBilling.csproj>`. The PublicApi Dockerfile does not include
the SDK source, so container builds need it copied into the build context first.

### Persistence

Duplicate protection uses two `CatalogContext` tables, `BillingCustomers` and `SubscriptionEnrollments`. Their
primary keys are the claims that stop a double-submit before it reaches Maxio. Migration:
`20261014120945_AddSubscriptionBilling`. With `UseOnlyInMemoryDatabase=true` the claims last only for the process
lifetime; after a restart, the reference lookups at Maxio stop a second customer or subscription from being created.
