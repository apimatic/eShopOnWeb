# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscription billing

The subscription endpoints use Maxio Advanced Billing in parallel with the existing
catalog, basket, and order endpoints:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "<handle from the plans endpoint>" }`
- `GET /api/my-subscriptions`

All three require a bearer token from `POST /api/authenticate`. The API derives the
customer identity from that token; callers cannot submit customer IDs or email addresses.

Configure the integration in .NET user-secrets. The local environment variables can be
copied without writing their values into the repository:

```powershell
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi
```

`Maxio:BaseUrl` is an optional absolute HTTPS override. When absent, the client derives
the site URL from `Maxio:Subdomain`. The customer reference is the eShop identity user ID,
and the subscription reference is deterministic per user and product, making repeated
subscribe requests return the original Maxio subscription.
