# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

The payment endpoints use the PayPal Orders v2, Payments v2, Payment Method Tokens v3, and
Transaction Search v1 contracts under `api-specs/paypal`. No PayPal SDK is used. PayPal access
tokens are cached in memory, external mutations carry stable `PayPal-Request-Id` values, and the
application stores only PayPal resource IDs plus masked card metadata.

Configure the integration from environment variables or .NET user-secrets. Environment variables
are mapped to the `PayPal:` section at startup:

```powershell
dotnet user-secrets set "PayPal:ClientId" $env:PAYPAL_CLIENT_ID --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" $env:PAYPAL_CLIENT_SECRET --project src/PublicApi
dotnet user-secrets set "PayPal:Environment" $env:PAYPAL_ENVIRONMENT --project src/PublicApi
dotnet user-secrets set "PayPal:Currency" $env:PAYPAL_CURRENCY --project src/PublicApi
```

`PayPal:BaseUrl` is optional. When present, it is the base address for every PayPal call, including
OAuth token acquisition. For local in-memory verification, set `UseOnlyInMemoryDatabase=true` and
keep the PublicApi process running for the complete order lifecycle.

The JWT-authenticated routes are:

- `POST /api/orders`, `POST /api/orders/{id}/pay`, and `GET /api/my-orders`
- `POST`, `GET`, and `DELETE /api/payment-methods`
- `POST /api/orders/{id}/refunds` with a caller-supplied `idempotencyKey`
- administrator-only `POST /api/orders/{id}/fulfil`, `POST /api/orders/{id}/cancel`, and
  `GET /api/reconciliation?from=...&to=...`

Card details are accepted only in the request that uses or vaults them. They are never persisted or
returned. A PayPal `PAYER_ACTION_REQUIRED` result is returned as a conflict because this API is a
headless direct-card integration and does not implement a browser approval round-trip.
