# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

The JWT-authenticated subscription API is additive to the existing catalog and checkout API:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "planHandle": "..." }`
- `GET /api/my-subscriptions`

Maxio Advanced Billing remains the system of record. PublicApi refuses to start when `Maxio:ApiKey`,
`Maxio:Subdomain`, or `Maxio:ProductFamilyHandle` is blank. `Maxio:BaseUrl` is an optional absolute HTTPS
override and, when supplied, is used verbatim.

For local development, copy the supplied process environment variables into this project's user-secrets
(the values remain outside the repository):

```powershell
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi/PublicApi.csproj
```

On machines without SQL Server LocalDB, start PublicApi with `UseOnlyInMemoryDatabase=true`. The in-memory
provisioning ledger is intentionally process-local and is lost on restart; SQL deployments use the included
unique-index migration for cross-instance write coordination.
