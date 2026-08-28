# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

The payment API uses PayPal Orders v2, Payments v2, Vault v3, and Transaction Search v1. It creates local orders from catalog prices, authorizes at `/pay`, captures at `/fulfil`, voids at `/cancel`, and refunds a capture through `/refunds`. Raw card data is forwarded directly to PayPal and is never persisted.

Configuration is bound from the `PayPal` section. Load the required environment variables into this project's user-secrets store without adding their values to configuration files:

```powershell
dotnet user-secrets set "PayPal:ClientId" $env:PAYPAL_CLIENT_ID --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:ClientSecret" $env:PAYPAL_CLIENT_SECRET --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:Environment" $env:PAYPAL_ENVIRONMENT --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:Currency" $env:PAYPAL_CURRENCY --project src/PublicApi/PublicApi.csproj
```

`PayPal:BaseUrl` is optional. When present it is the base address for every PayPal request, including OAuth token requests.

The JWT-authenticated routes are:

- `POST /api/orders`
- `POST /api/orders/{orderId}/pay`
- `POST /api/orders/{orderId}/fulfil` (administrator)
- `POST /api/orders/{orderId}/cancel` (administrator)
- `POST /api/orders/{orderId}/refunds`
- `GET /api/my-orders`
- `POST`, `GET`, and `DELETE /api/payment-methods`
- `GET /api/reconciliation?from=...&to=...` (administrator)
