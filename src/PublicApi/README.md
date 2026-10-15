# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscriptions (Maxio Advanced Billing)

Recurring-subscription billing runs alongside the one-time Catalog → Basket → Order flow, with Maxio Advanced Billing as the system of record. All three endpoints need a JWT from `POST api/authenticate`; the caller's identity comes from the token's name claim.

| Endpoint | Purpose |
| --- | --- |
| `GET api/subscription-plans` | Plans (products) of the configured Maxio product family; `truncated` is true if the catalog exceeded the read cap |
| `POST api/subscriptions` `{ "planHandle": "..." }` | Ensures a Maxio customer for the user, then subscribes them. `201` created, `200` already subscribed (idempotent replay), `202` created-but-unconfirmed (safe to repeat), `400` unknown plan, `409` a request for the same plan is in flight, `422` Maxio refused, `502`/`503` provider misconfigured/unavailable |
| `GET api/my-subscriptions` | The caller's subscriptions as Maxio reports them (plan, price, state, next billing date) |

### Configuration

Bound from the `Maxio:` section. Never put values in `appsettings*.json`: use user-secrets locally and environment variables (`Maxio__ApiKey`, …) when deployed. The host refuses to start when a required key is missing.

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Site API key |
| `Maxio:Subdomain` | unless `Maxio:BaseUrl` is set | Site subdomain → `https://{subdomain}.chargify.com` |
| `Maxio:ProductFamilyHandle` | yes | Plans are read from this family by handle |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address |
| `Maxio:Environment` | no | `US` (default) or `EU` |
| `Maxio:PaymentCollectionMethod` | no | `remittance` (default; invoiced, no card on file) or `invoice` for a legacy Statements site |

```bash
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

### Duplicate protection and unknown outcomes

Before any Maxio write, a claim row is inserted in `CatalogContext`. `BillingCustomerLinks` is keyed by user and `SubscriptionEnrollments` by (user, plan). A second request for the same key is refused by the primary key, so a double-click never creates two customers or subscriptions. The SDK never resends POSTs. If a create's connection fails after Maxio may have acted, the code reads back by the reference it sent: the deterministic customer reference, or the subscription reference stored on the claim. If that read-back also fails, the claim is marked Unknown and the next request settles it first. After a restart with the in-memory database, the customer is found again by reference and a live subscription to the same plan is adopted rather than duplicated. The `AddSubscriptionBilling` migration creates both tables for SQL Server.

The Maxio SDK is referenced as a project inside the locally installed `maxio-advanced-billing` plugin (`src/Infrastructure/Infrastructure.csproj`). Another machine or CI needs the plugin installed at the same relative path, or the reference re-pointed.
