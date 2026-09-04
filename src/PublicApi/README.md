# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

The JWT-protected subscription endpoints are:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "...", "productPricePointHandle": "..." }`
- `GET /api/my-subscriptions`

Configure the Maxio sandbox through .NET user-secrets. The API binds `Maxio:ApiKey`,
`Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and the optional verbatim
`Maxio:BaseUrl` override. For local setup, copy values from the environment without
putting them in source control:

```powershell
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi/PublicApi.csproj
```

The app uses the existing PublicApi JWT authentication endpoint (`POST /api/authenticate`)
and does not use the storefront cookie.
