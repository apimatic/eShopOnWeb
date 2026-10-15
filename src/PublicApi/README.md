# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscriptions (Maxio Advanced Billing)

Recurring subscriptions run alongside the one-time basket/order flow. Maxio Advanced Billing is the system of record.
All three endpoints require a JWT from `POST /api/authenticate`; the user comes from the token.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | Plans (products) of the configured Maxio product family. `truncated: true` means the list is incomplete. |
| `POST /api/subscriptions` `{ "planHandle": "..." }` | Ensures a Maxio customer for the user, then subscribes. `201 created`, `200 already_subscribed`, `202 pending` (provider did not confirm; settled on the next request), `409` (same request already in flight), `400` (plan not offered), `422` (Maxio rejected it). |
| `GET /api/my-subscriptions` | The user's subscriptions as held by Maxio (plan, price, state, next billing date). |

### Configuration

Bound from the `Maxio` section; the host refuses to start when a required value is missing.
Never put these values in `appsettings*.json`.

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | API key (Basic-auth username). |
| `Maxio:Subdomain` | unless `Maxio:BaseUrl` is set | Site subdomain; the base address is derived from it. |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | When set, used verbatim as the API base address instead of the subdomain. |

Development (user-secrets, from the sandbox environment variables):

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

Elsewhere, use environment variables (`Maxio__ApiKey`, ...) or a secret store. Options are read once at startup, so restart the process after rotating the key.

### Notes

- Subscriptions are created with `remittance` collection (invoiced), because eShopOnWeb captures no payment method.
- The Maxio .NET SDK is referenced as a project from the `maxio-advanced-billing` Claude Code plugin install directory, not from NuGet. Building PublicApi needs that plugin installed at the referenced path, or the `ProjectReference` in `PublicApi.csproj` re-pointed at your copy. This also applies to CI and the Docker image.
- Duplicate protection uses claim rows in `CatalogContext` (`BillingCustomerLinks`, `SubscriptionClaims`). On SQL Server, add an EF migration for them. With `UseOnlyInMemoryDatabase=true` they last for one process run; across restarts the Maxio customer reference and the provider-side check for an existing subscription still prevent duplicates.
