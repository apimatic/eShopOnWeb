# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

The payment integration is driven exclusively through JWT-authenticated PublicApi routes:

- `POST /api/orders`, `POST /api/orders/{id}/pay`, and `GET /api/my-orders`
- `POST /api/orders/{id}/fulfil` and `POST /api/orders/{id}/cancel` (Administrators)
- `POST /api/orders/{id}/refunds`
- `POST|GET /api/payment-methods` and `DELETE /api/payment-methods/{id}`
- `GET /api/reconciliation?from=...&to=...` (Administrators; maximum 31-day range)

The client is handwritten against the repository contracts in `api-specs/paypal`: Checkout Orders v2,
Payments v2, Vault Payment Tokens v3, and Transaction Search v1. No PayPal SDK is used. PayPal request IDs
are deterministic for authorize/capture/void/refund operations, and the database stores the remote resource
IDs and reported financial state needed by later operations.

Configuration binds from the `PayPal` section. Load environment-provided values into this project's
user-secrets without copying their values into repository files:

```powershell
dotnet user-secrets set "PayPal:ClientId" $env:PAYPAL_CLIENT_ID --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" $env:PAYPAL_CLIENT_SECRET --project src/PublicApi
dotnet user-secrets set "PayPal:Environment" $env:PAYPAL_ENVIRONMENT --project src/PublicApi
dotnet user-secrets set "PayPal:Currency" $env:PAYPAL_CURRENCY --project src/PublicApi
```

`PayPal:BaseUrl` is optional. When present, the client uses it for OAuth and every API request. Card numbers
and security codes are forwarded to PayPal for the current request only; the application persists and returns
only vault tokens and safe card metadata.
