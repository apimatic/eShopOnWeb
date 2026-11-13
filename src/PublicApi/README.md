# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

Recurring subscriptions run alongside the one-time Catalog → Basket → Order flow. Maxio Advanced Billing is the
system of record for plans, customers and subscriptions. All three routes require a JWT from `POST /api/authenticate`;
the caller's identity always comes from the token.

| Route | What it does |
| --- | --- |
| `GET /api/subscription-plans` | The products of the configured Maxio product family (`complete: false` means the listing hit its page cap). |
| `POST /api/subscriptions` `{ "productHandle": "<handle>" }` | Finds or creates the caller's Maxio customer, then subscribes them. `201` created, `200` already subscribed, `202` outcome still being confirmed, `400` unknown plan, `409` identical request in progress, `422` rejected by Maxio, `502/503` provider problem. |
| `GET /api/my-subscriptions` | The caller's subscriptions as Maxio reports them (plan, price, state, next billing date). |

### Configuration

Bound from the `Maxio` section; the host refuses to start if a required key is missing or blank. Never put values in
`appsettings*.json` — use user-secrets in Development and environment variables / a secret store elsewhere
(`Maxio__ApiKey`, …).

| Key | Required | Meaning |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Maxio API key (secret). |
| `Maxio:Subdomain` | unless `Maxio:BaseUrl` is set | Site subdomain; the API host is `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | yes | Handle of the product family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address instead of the subdomain-derived host (proxy, EU host, mock). |

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

### Duplicate protection

Every write takes a primary-key claim row in `CatalogContext` (`BillingCustomerLinks`, `SubscriptionEnrollments`)
before calling Maxio, keyed by a deterministic reference that is also sent to Maxio. A double-click is refused by the
store; Maxio is re-read by that reference before anything is created; and a write whose outcome is unknown
(connection drop, timeout) is settled by reference rather than reported as a failure. With
`UseOnlyInMemoryDatabase=true` the claim rows live only as long as the process; the re-read by reference still
prevents duplicates across restarts for sequential requests.

### SDK reference

The Maxio .NET SDK is not on NuGet; it ships as source in the `maxio` Claude Code plugin and is referenced from
`src/Infrastructure/Infrastructure.csproj` through the `MaxioSdkProjectPath` MSBuild property. Where the plugin is
installed elsewhere, build with `-p:MaxioSdkProjectPath=<path>/sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj`.
