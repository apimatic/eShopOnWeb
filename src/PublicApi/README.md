# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

The payment API uses the PayPal OpenAPI documents under `api-specs/paypal` as its contract:

- Checkout Orders v2 creates and authorizes an order.
- Payments v2 gets, reauthorizes, captures and voids authorizations, and creates/gets refunds.
- Vault Payment Tokens v3 creates and deletes saved-card tokens.
- Transaction Search v1 supplies the paginated reconciliation feed.

No PayPal SDK is used. Configuration is bound from the `PayPal` section with the keys
`ClientId`, `ClientSecret`, `Environment`, `Currency`, and optional `BaseUrl`. The host also maps
`PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET`, `PAYPAL_ENVIRONMENT`, `PAYPAL_CURRENCY`, and optional
`PAYPAL_BASE_URL` into that section. `BaseUrl`, when present, is the base for the OAuth token call
as well as every API call.

For local in-memory development, keep PublicApi running for the whole create/pay/fulfil/refund
sequence:

```powershell
$env:DOTNET_ROLL_FORWARD = 'Major'
$env:UseOnlyInMemoryDatabase = 'true'
dotnet run --project src/PublicApi --launch-profile PublicApi
```

Authenticate at `POST /api/authenticate`, then supply its token as `Authorization: Bearer ...`.
Shopper routes are `/api/orders`, `/api/orders/{orderId}/pay`, `/api/orders/{orderId}/refunds`,
`/api/my-orders`, and `/api/payment-methods`. Administrator-only routes are fulfil, cancel, and
`/api/reconciliation`.
