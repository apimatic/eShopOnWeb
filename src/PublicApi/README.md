# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

The payment API uses PayPal Orders v2, Payments v2, Payment Method Tokens v3, and
Transaction Search v1. Card numbers and security codes are forwarded to PayPal for the
current request only; the application persists only PayPal identifiers and safe card
metadata. A production deployment that accepts raw card data must maintain PCI SAQ D
compliance.

Configure the PublicApi project's user-secrets (or its environment) without placing values
in repository files:

```powershell
dotnet user-secrets set 'PayPal:ClientId' $env:PAYPAL_CLIENT_ID --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'PayPal:ClientSecret' $env:PAYPAL_CLIENT_SECRET --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'PayPal:Environment' $env:PAYPAL_ENVIRONMENT --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'PayPal:Currency' $env:PAYPAL_CURRENCY --project src/PublicApi/PublicApi.csproj
```

`PayPal:BaseUrl` is optional. When present, it is the base address for every PayPal request,
including OAuth. Otherwise `PayPal:Environment` selects PayPal's sandbox or live base URL.
The environment variables named above are also mapped to the same `PayPal:` keys in memory.

Run locally with the shared in-memory store:

```powershell
$env:UseOnlyInMemoryDatabase = 'true'
dotnet run --project src/PublicApi/PublicApi.csproj
```

Authenticate at `POST /api/authenticate` and use its token as a Bearer token. The API surface is:

- `POST /api/orders`
- `POST /api/orders/{orderId}/pay`
- `POST /api/orders/{orderId}/fulfil` (administrator)
- `POST /api/orders/{orderId}/cancel` (administrator)
- `POST /api/orders/{orderId}/refunds`
- `GET /api/my-orders`
- `POST`, `GET`, and `DELETE /api/payment-methods`
- `GET /api/reconciliation?from={iso-date-time}&to={iso-date-time}` (administrator)

Refund requests require an `idempotencyKey` in the JSON body. Saving a card also accepts an
optional `Idempotency-Key` header. Reconciliation returns the requested range plus
`payPalDataThrough`, which makes PayPal's documented reporting delay explicit while local
records continue through the requested `to` value.
