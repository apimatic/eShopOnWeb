# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio Advanced Billing)

Recurring subscriptions run alongside the one-time Catalog → Basket → Order flow, with Maxio Advanced Billing as
the billing system of record. All three endpoints need a JWT from `POST /api/authenticate`; the shopper is the
token's name claim.

| Endpoint | What it does |
| --- | --- |
| `GET /api/subscription-plans` | Plans offered: the products of the configured Maxio product family (cached 60 s). `truncated: true` means the list hit its page cap. |
| `POST /api/subscriptions` `{"planHandle":"eshop-pro"}` | Makes sure the shopper has a Maxio customer, then subscribes them. `201 created`, `200 already_subscribed` (repeat/double-click), `202 pending_confirmation` (sent but not yet confirmed; never created twice), `400` unknown plan, `409` in progress / already on another plan, `422` Maxio refused (its `errors[]` are returned), `502` billing misconfigured, `503` Maxio unavailable. |
| `GET /api/my-subscriptions` | The shopper's subscriptions, read live from Maxio: plan, price, state, next billing date. |

### Configuration

Settings are bound from the `Maxio` section. The host **refuses to start** if a required one is missing.

| Key | Required | Source |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | secret: user-secrets, `Maxio__ApiKey` or `MAXIO_API_KEY` |
| `Maxio:Subdomain` | yes, unless `Maxio:BaseUrl` is set | `Maxio__Subdomain` or `MAXIO_SITE_SUBDOMAIN` |
| `Maxio:ProductFamilyHandle` | yes | `Maxio__ProductFamilyHandle` or `MAXIO_DEFAULT_PRODUCT_FAMILY` |
| `Maxio:BaseUrl` | no | overrides the API base address verbatim (proxy, mock) |
| `Maxio:Environment` | no | `US` (default) or `EU` data center; `MAXIO_ENVIRONMENT` |
| `Maxio:PaymentCollectionMethod` | no | `remittance` (default: collected by invoice, since no card is captured), `invoice` (legacy Statements sites), `automatic`, `prepaid` |

The plain `MAXIO_*` variables are the lowest-precedence source, so appsettings, user-secrets and `Maxio__*` win.
Never put values in `appsettings*.json`. For local development:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

### Building

The Maxio .NET SDK is not on NuGet. It ships as source inside the `maxio` Claude Code plugin, and
`PublicApi.csproj` references it through the `MaxioSdkProject` MSBuild property. By default that property points
at a plugin checkout next to this repository. Anywhere else, pass
`-p:MaxioSdkProject=<path>/sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj`. The SDK needs the
.NET 10 SDK (C# 14) to compile, which is why `global.json` rolls forward to the latest major version.

### Duplicate prevention

Each Maxio write (create customer, create subscription) carries a deterministic, hashed reference derived from the
user name, and is guarded by a `BillingClaims` row keyed by that reference (claim → Maxio call → record result).
The database refuses a second claim, so a double-click never reaches Maxio twice. Maxio is also re-read by reference
before every create, which covers claims lost when the in-memory database restarts. A create whose connection drops
is settled by reading Maxio back. The `AddBillingClaims` migration creates the table on SQL Server.
