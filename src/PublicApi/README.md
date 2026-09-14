# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

`SubscriptionEndpoints/` adds recurring-subscription billing on top of the existing one-time
Catalog/Basket/Order flow, backed by [Maxio Advanced Billing](https://developers.maxio.com) as the
system of record for plans, customers and subscriptions (nothing is persisted locally):

- `GET /api/subscription-plans` - list plans available to subscribe to.
- `POST /api/subscriptions` - subscribe the caller (identity from the bearer token) to a plan. Idempotent.
- `GET /api/my-subscriptions` - list the caller's subscriptions.

All three require a JWT bearer token (see `AuthEndpoints`).

### Configuration

Settings are bound from the `Maxio` configuration section - set these via
[.NET user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) for this project
(`dotnet user-secrets set "Maxio:ApiKey" "..."` etc. from this directory), never in `appsettings*.json`:

| Key | Meaning |
|---|---|
| `Maxio:ApiKey` | API key for the target Maxio site (Basic Auth username). |
| `Maxio:Subdomain` | The Maxio site subdomain, e.g. `cp-exp-4`. |
| `Maxio:ProductFamilyHandle` | Handle of the product family containing the subscribable plans. |
| `Maxio:BaseUrl` | Optional - overrides the derived API base address verbatim. |
| `Maxio:Environment` | Optional, `US` (default) or `EU` - only affects the base address derived when `BaseUrl` is not set. |

