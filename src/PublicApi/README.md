# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

`SubscriptionEndpoints/` adds recurring-subscription billing on top of the existing one-time
commerce flow, using **Maxio Advanced Billing** (formerly Chargify) as the system of record.
All three endpoints are JWT-authenticated; the caller's identity comes from the token.

| Method & route | Purpose |
|----------------|---------|
| `GET /api/subscription-plans` | Lists the plans (products in the configured Maxio product family). |
| `POST /api/subscriptions` | Subscribes the caller to a plan (`{ "planHandle": "eshop-pro" }`, or omit to use the configured default). Ensures a single Maxio customer per user and is idempotent — a double-click returns the existing subscription (HTTP 200) instead of creating a duplicate (a new one returns HTTP 201). |
| `GET /api/my-subscriptions` | Lists the caller's subscriptions as held by Maxio. |

The implementation lives in `src/Infrastructure/Services/Maxio` behind the
`ISubscriptionBillingService` abstraction (in ApplicationCore) and uses the official
`Maxio.AdvancedBillingSdk` NuGet package.

### Configuration

Settings bind from the `Maxio` section. **Never commit secret values** — supply them via
.NET user-secrets or environment variables.

| Key | Meaning |
|-----|---------|
| `Maxio:ApiKey` | Site API key (HTTP Basic username; password is the literal `x`). |
| `Maxio:Subdomain` | Site subdomain, e.g. `cp-exp-8` → `https://cp-exp-8.chargify.com`. |
| `Maxio:ProductFamilyHandle` | Handle of the product family whose products are the plans. |
| `Maxio:BaseUrl` | Optional. When set, used verbatim as the API base address instead of deriving it from the subdomain. |
| `Maxio:Environment` | Optional data-center region, `US` (default) or `EU`. Ignored when `BaseUrl` is set. |
| `Maxio:DefaultPlanHandle` | Optional plan used when a subscribe request omits `planHandle`. |

Load the sandbox credentials into user-secrets (values come from environment variables; only
key/variable names appear here):

```bash
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
dotnet user-secrets set "Maxio:Environment" "$MAXIO_ENVIRONMENT" --project src/PublicApi
dotnet user-secrets set "Maxio:DefaultPlanHandle" "eshop-pro" --project src/PublicApi
```

