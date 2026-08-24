# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

JWT-authenticated endpoints backed by Maxio Advanced Billing:

- `GET /api/subscription-plans` — plans in the configured Maxio product family
- `POST /api/subscriptions` — subscribe the authenticated user to a plan (`{ "productHandle": "..." }`); idempotent
- `GET /api/my-subscriptions` — the authenticated user's subscriptions

Configuration is bound from the `Maxio:` section with these keys: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and `Maxio:BaseUrl` (optional override; when set it is used verbatim instead of `https://{Subdomain}.chargify.com`). Supply secrets via user-secrets or environment variables — never commit them:

```bash
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

