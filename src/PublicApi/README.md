# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio Advanced Billing)

Recurring subscriptions run alongside the one-time basket/order checkout, with Maxio Advanced Billing as the system of record. All three endpoints require a JWT from `POST /api/authenticate`.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | Plans on offer: the non-archived products of the configured Maxio product family. `isTruncated` is `true` if the catalog exceeded the read cap. |
| `POST /api/subscriptions` | `{ "planHandle": "...", "firstName"?: "...", "lastName"?: "..." }`. Ensures a Maxio customer for the caller (reference = eShop user id), then subscribes it. Responses: `201` created, `200` already subscribed to that plan (`alreadyExisted: true`), `202` sent but not yet confirmed, `400` unknown plan, `409` other live subscription / request in progress, `422` Maxio declined, `503` Maxio unavailable. Repeating the request is always safe. |
| `GET /api/my-subscriptions` | The caller's subscriptions as Maxio holds them: plan, price, state, next billing date. |

### Configuration (`Maxio:` section)

| Key | Required | Meaning |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Maxio API key (secret) |
| `Maxio:Subdomain` | yes, unless `Maxio:BaseUrl` is set | Maxio site subdomain; the base address is derived from it |
| `Maxio:ProductFamilyHandle` | yes | Handle of the product family whose products are the plans |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address instead of the derived one |
| `Maxio:Environment` | no | `US` (default) or `EU` hosting region |

The host refuses to start if a required key is missing or malformed. Never put these values in `appsettings*.json`. Locally, load them into user-secrets from the `MAXIO_*` environment variables:

```bash
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
dotnet user-secrets set "Maxio:Environment" "$MAXIO_ENVIRONMENT" --project src/PublicApi
```

Other deployments use environment variables (`Maxio__ApiKey`, …) or a secret store. The SDK client is built once per process, so a rotated key takes effect on the next restart.

### Build notes

- The Maxio .NET SDK is not on NuGet. It ships as source in the `maxio-advanced-billing` Claude Code plugin, and `src/Infrastructure` references it via the `MaxioSdkProjectPath` MSBuild property. The default path assumes the plugin marketplace sits next to this clone; elsewhere, pass `-p:MaxioSdkProjectPath=<plugin-root>/sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj`.
- The SDK requires C# 14, i.e. the .NET 10 SDK; `global.json` rolls forward to it (`latestMajor`).
- Double-submit protection: each Maxio write is preceded by a claim row keyed by the user id (`BillingCustomerLinks`, `SubscriptionEnrollments` in `CatalogContext`, migration `AddSubscriptionBilling`). With `UseOnlyInMemoryDatabase=true`, those rows and the seeded users' ids are lost on restart, so a restarted dev host gets a new Maxio customer for the same demo user.
