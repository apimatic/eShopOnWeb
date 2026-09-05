# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscriptions

The PublicApi exposes the additive subscription flow through JWT-authenticated endpoints:

- `GET /api/subscription-plans` returns active plans in the configured Maxio product family.
- `POST /api/subscriptions` accepts `{ "planHandle": "eshop-pro" }` and creates or returns the caller's subscription.
- `GET /api/my-subscriptions` returns the caller's current Maxio subscriptions.

Maxio configuration is read from the `Maxio` section using `ApiKey`, `Subdomain`, `ProductFamilyHandle`, and optional `BaseUrl`. When `BaseUrl` is empty, the client uses `https://{Subdomain}.chargify.com`, which is also the documented endpoint for Maxio test sites. The API key is used server-side with HTTP Basic Authentication (`API key` as username and `X` as password).

For local development, load the sandbox environment variables into the PublicApi user-secret store without putting their values in the repository:

```powershell
dotnet user-secrets set "Maxio:ApiKey" "$env:MAXIO_API_KEY" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:Subdomain" "$env:MAXIO_SITE_SUBDOMAIN" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$env:MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi/PublicApi.csproj
```

Run with `UseOnlyInMemoryDatabase=true` on a machine without SQL Server LocalDB. The subscription mapping is migrated for relational deployments and can be rebuilt from Maxio by its stable customer and subscription references if an in-memory process restarts.
