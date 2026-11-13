# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscriptions (Maxio Advanced Billing)

Recurring-subscription billing runs alongside the one-time Catalog → Basket → Order flow. Maxio is the
system of record for plans, customers and subscriptions; this API keeps only the `BillingClaims` rows that
make subscribe idempotent.

| Endpoint | Auth | Purpose |
| --- | --- | --- |
| `GET /api/subscription-plans` | JWT | Active products of the configured Maxio product family (`truncated` flags a capped listing) |
| `POST /api/subscriptions` `{ "planHandle": "..." }` | JWT | Ensures a Maxio customer for the caller and subscribes it. `201` new, `200` already subscribed, `409` same request in flight, `400` unknown plan, `422` rejected by Maxio, `503` provider unavailable/outcome being reconciled |
| `GET /api/my-subscriptions` | JWT | The caller's subscriptions, read live from Maxio |

The caller is always the user named in the JWT (get one from `POST /api/authenticate`).

### Configuration (`Maxio` section)

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Site API key. Keep it in user-secrets / the host's secret store, never in a file in this repo |
| `Maxio:Subdomain` | yes, unless `Maxio:BaseUrl` is set | API base address is derived from it |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address when set |
| `Maxio:ProductFamilyHandle` | yes | Family whose products are offered as plans |
| `Maxio:Environment` | no | `US` (default) or `EU` |
| `Maxio:PaymentCollectionMethod` | no | `remittance` (default — no card is captured), `automatic`, `prepaid`, `invoice` |

The host refuses to start when a required key is missing. For local development:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
dotnet user-secrets set "Maxio:Environment" "$MAXIO_ENVIRONMENT"
```

### Building

The Maxio .NET SDK is not on NuGet; it ships as source in the `maxio` Claude Code plugin and is referenced
as a project. The default path assumes the plugin sits next to this clone; elsewhere pass
`-p:MaxioSdkProject=<path>/MaxioAdvancedBilling.csproj` (or set `MaxioSdkProject` as an environment variable).
The SDK is compiled with C# 14, so a .NET 10 SDK is needed to build (`global.json` rolls forward); the app
still targets and runs on .NET 8.

SQL Server deployments get the `BillingClaims` table from the `AddBillingClaims` migration.
