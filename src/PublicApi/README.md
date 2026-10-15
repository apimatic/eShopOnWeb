# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscriptions (Maxio Advanced Billing)

Recurring-subscription billing runs alongside the one-time Catalog → Basket → Order flow. Maxio Advanced Billing is the system of record for plans and subscriptions. All endpoints need a JWT from `POST /api/authenticate`.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | Plans offered by the configured Maxio product family (handle, name, price, interval). `isTruncated` is `true` if the list is incomplete. |
| `POST /api/subscriptions` `{ "planHandle": "..." }` | Ensures a Maxio customer exists for the caller and subscribes them. `201` on create, `200` with the existing subscription on a repeat, `202` when Maxio could not be reached after the create was sent (the outcome is confirmed on the next request), `409` while another request for the same user is in flight or when already subscribed to a different plan, `400` for a plan that is not offered, `422` when Maxio declines, and `502`/`503` for provider faults. |
| `GET /api/my-subscriptions` | The caller's subscriptions as Maxio holds them: plan, price, state, next billing date. |

### Configuration

Settings bind from the `Maxio` section. The host refuses to start if a required setting is missing. Never put the values in `appsettings*.json`.

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Maxio API key. Falls back to the `MAXIO_API_KEY` environment variable. |
| `Maxio:Subdomain` | unless `Maxio:BaseUrl` is set | Maxio site subdomain. Falls back to `MAXIO_SITE_SUBDOMAIN`. |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are offered as plans. Falls back to `MAXIO_DEFAULT_PRODUCT_FAMILY`. |
| `Maxio:BaseUrl` | no | When set, used verbatim as the API base address instead of `https://{subdomain}.chargify.com`. |

For local development, use user-secrets:

```bash
dotnet user-secrets set "Maxio:ApiKey" "<key>" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "<site>" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "<family-handle>" --project src/PublicApi
```

In deployments, use `Maxio__ApiKey`-style environment variables or a secret store. The API key is read once at startup, so restart the process after rotating it.

Subscriptions are created with Maxio's `remittance` payment collection: Maxio invoices the subscriber and no card is captured.

### Build prerequisite: the Maxio SDK

The Maxio Advanced Billing .NET SDK is not on NuGet. It ships as source inside the `maxio-advanced-billing` Claude Code plugin, and `src/Infrastructure/Infrastructure.csproj` references it through the `MaxioSdkProject` property. Its default is the plugin's location next to this clone. On another machine or in CI, install the plugin and point the property at it:

```bash
dotnet build -p:MaxioSdkProject=<plugin-root>/sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj
```

SQL Server deployments must apply the `AddSubscriptionEnrollments` migration (`CatalogContext`). The in-memory database (`UseOnlyInMemoryDatabase=true`) ignores migrations.
