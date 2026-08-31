# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

PublicApi supports card authorization, capture-on-fulfilment, void-on-cancellation, refunds,
saved cards, and transaction reconciliation. Card numbers and security codes are sent directly
to PayPal and are never persisted by eShopOnWeb. Direct card handling requires the merchant's
PayPal account and deployment to meet PayPal's Advanced Card Payments and PCI SAQ D requirements.

Configuration is bound from `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`,
`PayPal:Currency`, and optional `PayPal:BaseUrl`. For local development, load the four supplied
environment variables into user-secrets:

```powershell
dotnet user-secrets set "PayPal:ClientId" $env:PAYPAL_CLIENT_ID --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" $env:PAYPAL_CLIENT_SECRET --project src/PublicApi
dotnet user-secrets set "PayPal:Environment" $env:PAYPAL_ENVIRONMENT --project src/PublicApi
dotnet user-secrets set "PayPal:Currency" $env:PAYPAL_CURRENCY --project src/PublicApi
```

Run the API with its single in-memory catalog/order store:

```powershell
$env:UseOnlyInMemoryDatabase = "true"
dotnet run --project src/PublicApi
```

Authenticate at `POST /api/authenticate`, pass the returned JWT as `Authorization: Bearer TOKEN`,
then drive the flow through these routes:

- `POST /api/orders`
- `POST /api/orders/{orderId}/pay`
- `POST /api/orders/{orderId}/fulfil` (administrator)
- `POST /api/orders/{orderId}/cancel` (administrator)
- `POST /api/orders/{orderId}/refunds`
- `GET /api/my-orders`
- `POST`, `GET`, and `DELETE /api/payment-methods`
- `GET /api/reconciliation?from=...&to=...` (administrator)

OpenAPI request schemas and response shapes are available at `/swagger` while the API is running.
