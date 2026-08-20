# Public API

The subscription API uses Maxio Advanced Billing as its system of record. Its endpoints follow the existing endpoint-class convention and require a PublicApi JWT bearer token:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "eshop-pro" }`
- `GET /api/my-subscriptions`

## Local Maxio configuration

Set the sandbox credentials in the environment, then copy them into the PublicApi user-secrets store. This keeps their values outside the repository:

```powershell
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi
dotnet user-secrets set "UseOnlyInMemoryDatabase" "true" --project src/PublicApi
```

The default API base is `https://{Maxio:Subdomain}.chargify.com`. Set `Maxio:BaseUrl` as a user secret only when the site requires an alternate Maxio API base; an explicit value is used as-is.

Run locally with SDK and runtime major-version roll-forward enabled:

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet run --project src/PublicApi
```

Authenticate through `POST /api/authenticate`, send its token as `Authorization: Bearer <token>`, and call the three subscription endpoints above. Repeating the same subscription request returns the original subscription with `created: false`.
