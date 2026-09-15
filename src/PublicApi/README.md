# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

PublicApi supports an authorize-at-checkout/capture-at-fulfilment PayPal flow and PayPal-vaulted
cards. Configuration is bound from `PayPal:ClientId`, `PayPal:ClientSecret`,
`PayPal:Environment`, `PayPal:Currency`, and optional `PayPal:BaseUrl`. The first four can be
loaded from the corresponding `PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET`,
`PAYPAL_ENVIRONMENT`, and `PAYPAL_CURRENCY` environment variables:

```powershell
dotnet user-secrets set --project src/PublicApi/PublicApi.csproj "PayPal:ClientId" $env:PAYPAL_CLIENT_ID
dotnet user-secrets set --project src/PublicApi/PublicApi.csproj "PayPal:ClientSecret" $env:PAYPAL_CLIENT_SECRET
dotnet user-secrets set --project src/PublicApi/PublicApi.csproj "PayPal:Environment" $env:PAYPAL_ENVIRONMENT
dotnet user-secrets set --project src/PublicApi/PublicApi.csproj "PayPal:Currency" $env:PAYPAL_CURRENCY
$env:UseOnlyInMemoryDatabase="true"
$env:DOTNET_ROLL_FORWARD="Major"
dotnet run --project src/PublicApi/PublicApi.csproj --launch-profile PublicApi
```

Authenticate at `POST /api/authenticate`, then use the bearer token with these routes:

- `POST /api/orders`, `POST /api/orders/{id}/pay`, and `GET /api/my-orders`
- `POST|GET /api/payment-methods` and `DELETE /api/payment-methods/{id}`
- `POST /api/orders/{id}/refunds`
- Administrator only: `POST /api/orders/{id}/fulfil`, `POST /api/orders/{id}/cancel`, and
  `GET /api/reconciliation?from={iso-date-time}&to={iso-date-time}`

Raw card fields are forwarded to PayPal only. The application persists vault IDs and masked card
metadata, never card numbers or security codes.
