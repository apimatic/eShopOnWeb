# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscription billing

The additive subscription API exposes these JWT-protected routes:

- `GET /api/subscription-plans`
- `POST /api/subscriptions`
- `GET /api/my-subscriptions`

`POST /api/subscriptions` accepts `productHandle` and a caller-generated `idempotencyKey`.
Reuse the same key when retrying the same subscribe intent. Reusing a key for a different
plan returns `409 Conflict`.

PublicApi binds Maxio configuration from `Maxio:ApiKey`, `Maxio:Subdomain`,
`Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`. Store these in user-secrets or
another configuration secret provider. When `Maxio:BaseUrl` is present, the SDK uses that
string verbatim; otherwise it derives the host from `Maxio:Subdomain`.

For local development, load the supplied environment variables without printing their
values:

```powershell
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi
```

On a machine without LocalDB, set `UseOnlyInMemoryDatabase=true`. The in-memory provider
does not persist the local idempotency ledger across process restarts. SQL Server deployments
apply the `AddSubscriptionBilling` identity migration and retain the ledger durably.
