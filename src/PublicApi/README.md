# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

Recurring subscriptions, parallel to the one-time Catalog → Basket → Order flow. Maxio Advanced Billing is the
system of record; the adapter lives in `src/Infrastructure.Maxio`. All three endpoints require a JWT from
`POST api/authenticate`; the buyer is the token's user name, never the request body.

| Endpoint | Purpose |
| --- | --- |
| `GET api/subscription-plans` | Plans (products) of the configured product family. `isTruncated` is true if the listing hit its page cap. |
| `POST api/subscriptions` `{ "planHandle": "...", "firstName"?: "...", "lastName"?: "..." }` | Subscribe. `201` created · `200` already subscribed (a repeat or double-click never creates a second customer or subscription) · `202` outcome still being confirmed · `400` plan not offered · `409` same request already in flight · `422` rejected by Maxio · `503` Maxio unreachable/timed out. |
| `GET api/my-subscriptions` | The caller's subscriptions as Maxio reports them (plan, price, state, next billing date). |

### Configuration (`Maxio:` section)

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Secret. Never put it in appsettings or launch profiles. |
| `Maxio:Subdomain` | unless `Maxio:BaseUrl` is set | The Advanced Billing site. |
| `Maxio:ProductFamilyHandle` | yes | The family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | When set, used verbatim as the API base address. |
| `Maxio:Environment` | no | `US` (default) or `EU`. |
| `Maxio:PaymentCollectionMethod` | no | `remittance` (default; invoiced, no card needed) — `invoice` on legacy Statements sites. |

The host refuses to start when a required key is missing. Values come from (highest precedence first) the command
line, `Maxio__*` environment variables, user-secrets (Development), appsettings, and finally the `MAXIO_API_KEY`,
`MAXIO_SITE_SUBDOMAIN`, `MAXIO_DEFAULT_PRODUCT_FAMILY`, `MAXIO_ENVIRONMENT` and `MAXIO_BASE_URL` variables.
For local development:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

Customer and enrollment claims are stored in `CatalogContext` (migration `AddSubscriptionBilling`). With
`UseOnlyInMemoryDatabase=true` they are lost on restart; a re-subscribe after a restart still finds the existing
subscription in Maxio and returns it instead of creating another.

### Building

The Maxio .NET SDK is not on NuGet: it ships as source in the `maxio-advanced-billing` Claude Code plugin.
`src/Infrastructure.Maxio` references it at `..\..\..\marketplace\plugins\maxio-advanced-billing\sdk\dotnet\src\MaxioAdvancedBilling\MaxioAdvancedBilling.csproj`;
point it at your copy with `-p:MaxioSdkProject=<path to MaxioAdvancedBilling.csproj>`. Machines (CI, Docker
builds) without the plugin cannot build PublicApi until the SDK is available to them.

