# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscription billing

The subscription endpoints are a JWT-authenticated capability parallel to the existing basket and order flow:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "<handle returned by the plans endpoint>" }`
- `GET /api/my-subscriptions`

Maxio Advanced Billing remains the system of record. The API resolves the configured product family by its stable handle, reads current plans and subscription state from Maxio, and stores only the local user-to-subscription association. Customer and subscription references are deterministic hashes, so retries and concurrent clicks resolve the existing Maxio resources.

Configuration binds from the `Maxio` section:

- `Maxio:ApiKey`
- `Maxio:Subdomain`
- `Maxio:ProductFamilyHandle`
- `Maxio:BaseUrl` (optional; when supplied it replaces the subdomain-derived base URL)

For local development, load the provided environment variables into the PublicApi user-secret store:

```powershell
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi
```

Do not place credential values in an appsettings file or launch profile. In deployed environments, standard .NET hierarchical environment names such as `Maxio__ApiKey` can be used.
