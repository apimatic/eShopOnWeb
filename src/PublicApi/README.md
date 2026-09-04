# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

The JWT-authenticated subscription endpoints use Maxio Advanced Billing as the billing
system of record:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "planHandle": "..." }`
- `GET /api/my-subscriptions`

Configure the integration through the `Maxio:` user-secrets section. The supported keys
are `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and the optional
`Maxio:BaseUrl`. When `Maxio:BaseUrl` is empty, the client derives the server from the
configured subdomain and the `MAXIO_ENVIRONMENT` (`US` or `EU`) environment variable.

For local setup, copy values only from the process environment into user-secrets; do not
put them in this repository:

```powershell
dotnet user-secrets set Maxio:ApiKey $env:MAXIO_API_KEY --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set Maxio:Subdomain $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set Maxio:ProductFamilyHandle $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi/PublicApi.csproj
```

The API uses the Maxio spec's Basic authentication scheme (`API key` as username and
`x` as password) and resolves product/customer/subscription records by handles and
references, not seeded numeric IDs.
