# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

PublicApi exposes the complete authorize/capture/refund and saved-card flows under `/api`.
Card data is sent directly to PayPal and is never persisted by eShopOnWeb; saved methods contain
only PayPal's vault token, brand, last four digits, and expiry.

Configure the project through .NET user-secrets (or another .NET configuration provider):

```powershell
dotnet user-secrets set "PayPal:ClientId" $env:PAYPAL_CLIENT_ID --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:ClientSecret" $env:PAYPAL_CLIENT_SECRET --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:Environment" $env:PAYPAL_ENVIRONMENT --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:Currency" $env:PAYPAL_CURRENCY --project src/PublicApi/PublicApi.csproj
```

`PayPal:BaseUrl` is an optional absolute base-address override. When present it is used for every
PayPal request, including `/v1/oauth2/token`. Otherwise `PayPal:Environment` selects PayPal's
sandbox or live REST base address.

For this repository's local in-memory setup:

```powershell
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet run --project src/PublicApi/PublicApi.csproj --launch-profile PublicApi
```

Authenticate at `POST /api/authenticate`, then use its bearer token. Shopper routes are
`POST /api/orders`, `POST /api/orders/{id}/pay`, `POST /api/orders/{id}/refunds`,
`GET /api/my-orders`, and the three `/api/payment-methods` routes. Administrator-only routes are
fulfil, cancel, and reconciliation. Swagger at `/swagger` contains the request schemas.
