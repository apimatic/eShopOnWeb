# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscription billing

Subscription billing is additive to the existing basket and checkout flow. PublicApi exposes three JWT-protected routes:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "eshop-pro" }`
- `GET /api/my-subscriptions`

Maxio is authoritative for plans and subscription state. The catalog product-family ID and product IDs are resolved at runtime from handles. The local `MaxioCustomerLinks` and `MaxioSubscriptionEnrollments` tables only coordinate idempotency and ambiguous-write recovery.

PublicApi binds exactly these settings from the `Maxio` section:

- `Maxio:ApiKey`
- `Maxio:Subdomain`
- `Maxio:ProductFamilyHandle`
- `Maxio:BaseUrl` (optional; when supplied, it is used verbatim)

For local development, copy the supplied environment variables into the project's user-secret store without writing their values into this repository:

```powershell
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi
```

To use a mock, proxy, EU endpoint, or other explicit address, also set `Maxio:BaseUrl`. In deployed environments, the equivalent .NET environment-variable names use double underscores, for example `Maxio__ApiKey`.

On a machine without SQL Server LocalDB, run PublicApi with `UseOnlyInMemoryDatabase=true`. In-memory idempotency records survive only for the lifetime of that host process. SQL Server deployments apply the included `AddMaxioSubscriptionBilling` migration during normal catalog database startup.
