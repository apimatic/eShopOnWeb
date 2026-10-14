# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

Recurring subscriptions run alongside the one-time Catalog → Basket → Order flow, with Maxio Advanced Billing
as the billing system of record. All endpoints require a JWT from `POST api/authenticate`; the shopper's identity
comes from the token.

| Endpoint | Purpose |
| --- | --- |
| `GET api/subscription-plans` | Plans = non-archived products of the configured Maxio product family (`isTruncated` is set if the list hit its cap) |
| `POST api/subscriptions` `{ "planHandle": "...", "firstName"?, "lastName"? }` | Subscribe. `201` created · `200` already subscribed to that plan (same subscription returned) · `202` in progress / being confirmed · `409` subscribed to another plan · `400` unknown plan · `422` rejected by Maxio |
| `GET api/my-subscriptions` | The shopper's subscriptions, read live from Maxio |

### Configuration (`Maxio:` section)

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Maxio API key (Basic-auth username; password is the fixed `x`) |
| `Maxio:Subdomain` | unless `BaseUrl` set | Maxio site subdomain |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are offered as plans |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address instead of the subdomain-derived one |
| `Maxio:Environment` | no | `US` (default) or `EU` hosting |

The host refuses to start when a required value is missing. Never put values in `appsettings*.json`; for local
development load them into user-secrets from the environment:

```powershell
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi
dotnet user-secrets set "Maxio:Environment" $env:MAXIO_ENVIRONMENT --project src/PublicApi
```

Elsewhere use environment variables (`Maxio__ApiKey`, …) or a secret store. A rotated key takes effect on restart.

### How duplicates and failures are handled

- **Double-submit:** a subscribe first inserts a claim row (`SubscriptionEnrollments`, primary key = user + product
  family) in the app database; a second concurrent request is refused by the key before it reaches Maxio. The
  Maxio customer is claimed the same way (`BillingCustomers`, key = user) and is looked up by its reference
  (`eshop-user-{userId}`) before it is created.
- **Unknown outcomes:** if the connection drops after Maxio may have created the subscription, the service looks it
  up by its reference (`eshop-sub-{userId}-{family}`) instead of reporting a failure. If that is still inconclusive
  the enrollment stays `Unknown` (the API answers `202`) and `BillingReconciliationService` settles it in the background.
- Creates (`POST`) are never retried by the SDK; reads retry twice. Each API call has a 30 s total budget.
- Subscriptions are created with `payment_collection_method = remittance` (invoice), since the storefront captures
  no payment method.

The SDK ships as source inside the `maxio-advanced-billing` plugin and is referenced from `Infrastructure.csproj`
via the `MaxioSdkProject` MSBuild property. Override it (`/p:MaxioSdkProject=<path>`) where the plugin is installed elsewhere.
