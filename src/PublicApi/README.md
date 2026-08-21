# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscription billing

The JWT-authenticated subscription endpoints are:

- `GET /api/subscription-plans`
- `POST /api/subscriptions`
- `GET /api/my-subscriptions`

PublicApi reads Maxio settings from `Maxio:ApiKey`, `Maxio:Subdomain`,
`Maxio:ProductFamilyHandle`, and the optional `Maxio:BaseUrl` override. For local
development, load the environment-provided values into the existing user-secret store:

```powershell
dotnet user-secrets set "Maxio:ApiKey" "$env:MAXIO_API_KEY" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:Subdomain" "$env:MAXIO_SITE_SUBDOMAIN" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$env:MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi/PublicApi.csproj
```

Do not put credentials in an appsettings file. In deployed environments, use the
configuration provider appropriate to the host (for example, environment variables named
`Maxio__ApiKey`, `Maxio__Subdomain`, and `Maxio__ProductFamilyHandle`).
