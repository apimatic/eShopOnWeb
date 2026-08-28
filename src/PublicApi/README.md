# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

PublicApi supports card authorization, capture at fulfilment, void-on-cancel, partial/full refunds,
saved cards, and PayPal transaction reconciliation. Card numbers and security codes are sent directly
to PayPal and are never persisted by eShop.

The application binds these settings from the `PayPal` configuration section:

- `PayPal:ClientId`
- `PayPal:ClientSecret`
- `PayPal:Environment` (`Sandbox`, `Live`, or `Production`)
- `PayPal:Currency`
- `PayPal:BaseUrl` (optional; when present it is the base for OAuth and every API request)

For local sandbox work, copy the supplied environment variables into user-secrets without placing
their values in the repository:

```powershell
dotnet user-secrets set "PayPal:ClientId" $env:PAYPAL_CLIENT_ID --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:ClientSecret" $env:PAYPAL_CLIENT_SECRET --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:Environment" $env:PAYPAL_ENVIRONMENT --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:Currency" $env:PAYPAL_CURRENCY --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "UseOnlyInMemoryDatabase" "true" --project src/PublicApi/PublicApi.csproj
```

At runtime PublicApi also maps the four supplied `PAYPAL_*` variables into the same section. A normal
.NET hierarchical environment variable such as `PayPal__BaseUrl` can provide the optional override.

All payment routes require a bearer token from `POST /api/authenticate`. Shopper routes use the token's
name claim and never accept a buyer ID. `fulfil`, `cancel`, and `reconciliation` additionally require the
`Administrators` role.

The sandbox test card uses expiry in `YYYY-MM` format. A minimal order request is:

```json
{
  "items": [
    { "catalogItemId": 1, "quantity": 1 }
  ]
}
```

`shippingAddress` is optional for compatibility with this reference app's existing hard-coded demo
shipping address. Payment responses expose only PayPal IDs and safe card metadata.
