# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

The payment integration is implemented directly from the OpenAPI contracts in
`api-specs/paypal` (Checkout Orders v2, Payments v2, Vault Payment Tokens v3 and
Transaction Search v1). It does not use a PayPal SDK.

Configure the PublicApi user-secret store without copying secret values into the
repository:

```powershell
dotnet user-secrets set "PayPal:ClientId" $env:PAYPAL_CLIENT_ID --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" $env:PAYPAL_CLIENT_SECRET --project src/PublicApi
dotnet user-secrets set "PayPal:Environment" $env:PAYPAL_ENVIRONMENT --project src/PublicApi
dotnet user-secrets set "PayPal:Currency" $env:PAYPAL_CURRENCY --project src/PublicApi
```

`PayPal:BaseUrl` is optional. When configured, it is used as the base address for
the OAuth token request and every API request. Otherwise the address is derived
from `PayPal:Environment`.

For the machine-local in-memory setup:

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
dotnet run --project src/PublicApi --launch-profile PublicApi
```

Obtain JWTs from `POST /api/authenticate`. The shopper API is:

- `POST /api/orders`
- `POST /api/orders/{orderId}/pay`
- `POST /api/orders/{orderId}/refunds`
- `GET /api/my-orders`
- `POST`, `GET` and `DELETE /api/payment-methods`

Administrator JWTs are required for fulfilment, cancellation and reconciliation:

- `POST /api/orders/{orderId}/fulfil`
- `POST /api/orders/{orderId}/cancel`
- `GET /api/reconciliation?from={ISO-8601}&to={ISO-8601}`

The Swagger document at `/swagger` contains the request and response schemas.
Card data is accepted only in the save/pay request and is sent directly to
PayPal; the application persists only the PayPal vault token and a safe card
description.
