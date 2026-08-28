# Public API

PublicApi uses JWT bearer authentication. Obtain a token from `POST /api/authenticate` and send
it as `Authorization: Bearer <token>`.

## PayPal configuration

The integration binds only the `PayPal` configuration section. For local development, import the
environment-provided values into this project's user-secrets store:

```powershell
dotnet user-secrets set 'PayPal:ClientId' $env:PAYPAL_CLIENT_ID --project src/PublicApi
dotnet user-secrets set 'PayPal:ClientSecret' $env:PAYPAL_CLIENT_SECRET --project src/PublicApi
dotnet user-secrets set 'PayPal:Environment' $env:PAYPAL_ENVIRONMENT --project src/PublicApi
dotnet user-secrets set 'PayPal:Currency' $env:PAYPAL_CURRENCY --project src/PublicApi
```

`PayPal:BaseUrl` is optional. When set, it is the base address for OAuth and every PayPal API call.
Card numbers and security codes are accepted only in request bodies and are never persisted.

## Payment routes

- `POST /api/orders` and `GET /api/my-orders`
- `POST /api/orders/{orderId}/pay`
- `POST /api/orders/{orderId}/fulfil` (administrator)
- `POST /api/orders/{orderId}/cancel` (administrator)
- `POST /api/orders/{orderId}/refunds`
- `POST`, `GET`, and `DELETE /api/payment-methods`
- `GET /api/reconciliation?from=<ISO-8601>&to=<ISO-8601>` (administrator)

Run without SQL Server by setting `UseOnlyInMemoryDatabase=true`. Keep one PublicApi process alive
for an entire payment flow because its in-memory data is process-local.
