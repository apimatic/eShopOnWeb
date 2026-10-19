# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

Recurring subscriptions run alongside the one-time Catalog → Basket → Order flow. Maxio Advanced Billing is the billing system of record. All three endpoints need a JWT from `POST api/authenticate`, and the caller is identified by the token's name claim.

| Endpoint | Purpose | Responses |
| --- | --- | --- |
| `GET api/subscription-plans` | Lists the plans offered: active products of the configured Maxio product family | 200. `isComplete: false` means the list was cut short |
| `POST api/subscriptions` `{ "planHandle": "eshop-pro" }` | Makes sure the shopper has a Maxio customer, then subscribes them to the plan | 201 `created` · 200 `already_subscribed` (idempotent repeat) · 202 `pending` (outcome being confirmed) · 400 unknown plan · 409 same request already in flight · 422 rejected by Maxio · 503 Maxio unavailable |
| `GET api/my-subscriptions` | Lists the shopper's subscriptions: plan, price, state, next billing date | 200 |

### Configuration

The settings are bound from the `Maxio` section. **Never commit values.**

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Site API key. |
| `Maxio:Subdomain` | yes, unless `BaseUrl` is set | The site subdomain. The API base address is derived from it. |
| `Maxio:ProductFamilyHandle` | yes | The product family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | When set, it is used verbatim as the API base address instead of the subdomain. |
| `Maxio:Environment` | no | `US` (default) or `EU` hosting. |

For local development, use user-secrets:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
dotnet user-secrets set "Maxio:Environment" "$MAXIO_ENVIRONMENT"
```

Elsewhere, use `Maxio__ApiKey`-style environment variables or a secret store. Any key that no other source sets falls back to the `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_DEFAULT_PRODUCT_FAMILY` and `MAXIO_ENVIRONMENT` variables. If a required key is missing, the host refuses to start.

### Duplicate protection

- Every Maxio write takes a claim in `CatalogContext` before it is sent: one row per user (`BillingCustomers`) and one per user and plan (`SubscriptionEnrollments`). A second concurrent request is refused by the primary key and gets a 409.
- If a create's outcome is unknown (connection drop, timeout), the service looks the subscription up by the reference it sent. If it still can't confirm the result, it records the claim as `Unknown` and returns 202. `BillingClaimSweeper` settles these claims every minute.
- With `UseOnlyInMemoryDatabase=true`, claims last only for the life of the process. A repeat after a restart is still safe: the service finds the existing Maxio customer by reference and adopts a live subscription to the same plan instead of creating a second one.
- Against SQL Server, apply the `AddSubscriptionBilling` migration. The seeder applies migrations at startup.

### Build note: the SDK reference

The Maxio .NET SDK is not on NuGet. It ships as source in the `maxio-advanced-billing` Claude Code plugin. `src/Infrastructure/Infrastructure.csproj` references it through the `MaxioSdkRoot` MSBuild property, which defaults to the plugin location on the machine where this was built. Anywhere else (a teammate's machine, CI, the Dockerfile build), pass `-p:MaxioSdkRoot=<plugin-root>/sdk/dotnet`.

