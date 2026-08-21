# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

The JWT-authenticated subscription API is additive to the existing commerce flow:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "..." }`
- `GET /api/my-subscriptions`

Maxio Advanced Billing configuration is bound from `Maxio:ApiKey`, `Maxio:Subdomain`,
`Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`. The first three settings can
also be supplied by `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, and
`MAXIO_DEFAULT_PRODUCT_FAMILY`; environment variables take precedence. For local
development, copy them to the PublicApi user-secret store without placing values in the
repository:

```powershell
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi
```

When `Maxio:BaseUrl` is absent, the base address is derived from the OpenAPI server
template as `https://{subdomain}.chargify.com`. Subscriptions use Maxio's `remittance`
collection method so products configured without a required payment method can enroll
without card capture.
