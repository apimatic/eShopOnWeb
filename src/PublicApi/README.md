# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscriptions

The PublicApi subscription endpoints are JWT-authenticated and keep Maxio Advanced Billing as the system of record:

* `GET /api/subscription-plans`
* `POST /api/subscriptions` with `{ "productHandle": "..." }`
* `GET /api/my-subscriptions`

Configure the PublicApi user-secrets store from the environment without committing secret values:

```powershell
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi/PublicApi.csproj
```

`Maxio:BaseUrl` is optional. If present, it is used as the API base address; otherwise the client derives `https://{Maxio:Subdomain}.chargify.com/`. The client uses Maxio’s documented Basic Authentication scheme (`ApiKey:X`) server-side.
