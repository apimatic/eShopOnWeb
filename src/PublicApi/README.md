# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio Advanced Billing)

Recurring subscriptions run alongside the one-time Catalog → Basket → Order flow, with Maxio as the billing
system of record. All three endpoints require a JWT from `POST /api/authenticate`.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | Plans of the product family named by `Maxio:ProductFamilyHandle` (archived products left out). |
| `POST /api/subscriptions` | Body `{ "planHandle": "...", "firstName"?: "...", "lastName"?: "..." }`. Finds or creates the shopper's Maxio customer, then subscribes them. Responses: `201` created; `200` already subscribed to that plan; `409` subscribed to another plan or a subscribe is already running; `202` Maxio has not confirmed the outcome yet (it is settled on the next request); `400` unknown plan; `422` rejected by Maxio; `502`/`504` Maxio unavailable or slow. |
| `GET /api/my-subscriptions` | The shopper's subscriptions as Maxio reports them: plan, price, state, `nextBillingAt`. |

A double-submitted subscribe never reaches Maxio twice: each shopper's enrollment row (`SubscriptionEnrollments`,
keyed by user name) is claimed before any Maxio write. Subscriptions are billed by invoice
(`remittance`, or `invoice` on legacy Statements sites), because this flow captures no payment method.

### Configuration

| Key | Required | Meaning |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Maxio API key. |
| `Maxio:Subdomain` | yes, unless `Maxio:BaseUrl` is set | Maxio site; the API host is `https://{subdomain}.chargify.com` (`.ebilling.maxio.com` for EU). |
| `Maxio:ProductFamilyHandle` | yes | The product family whose plans are offered. |
| `Maxio:BaseUrl` | no | When set, used verbatim as the API base address. |
| `Maxio:Environment` | no | `US` (default) or `EU` hosting. |

The host refuses to start when a required key is missing. Keep the values out of appsettings files: use
user-secrets in development, and environment variables (`Maxio__ApiKey`, ...) or a secret store elsewhere:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
dotnet user-secrets set "Maxio:Environment" "$MAXIO_ENVIRONMENT"
```

The Maxio .NET SDK is not on NuGet; it ships as source in the `maxio-advanced-billing` Claude Code plugin.
`src/Infrastructure/Infrastructure.csproj` references it through the `MaxioSdkProjectPath` MSBuild property, so
point that property at your plugin install when it is not beside this clone.
