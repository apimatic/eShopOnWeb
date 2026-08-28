# PayPal payments API

Obtain a bearer token with `POST /api/authenticate`; shopper ownership comes exclusively from
that token.

## Configuration

The integration binds the `PayPal:` configuration section. Store credentials outside the
repository; for local development, load the supplied environment variables into user-secrets:

```powershell
dotnet user-secrets set 'PayPal:ClientId' $env:PAYPAL_CLIENT_ID --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'PayPal:ClientSecret' $env:PAYPAL_CLIENT_SECRET --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'PayPal:Environment' $env:PAYPAL_ENVIRONMENT --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'PayPal:Currency' $env:PAYPAL_CURRENCY --project src/PublicApi/PublicApi.csproj
```

The exact supported keys are `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`,
`PayPal:Currency`, and optional `PayPal:BaseUrl`. When present, `BaseUrl` is used for every
PayPal request, including OAuth.

For this machine, run with `UseOnlyInMemoryDatabase=true` and `DOTNET_ROLL_FORWARD=Major`.
The in-memory database lasts only for the life of the PublicApi process.

## Endpoints

- `POST /api/orders`
- `POST /api/orders/{orderId}/pay`
- `POST /api/orders/{orderId}/fulfil` (administrator)
- `POST /api/orders/{orderId}/cancel` (administrator)
- `POST /api/orders/{orderId}/refunds`
- `GET /api/my-orders`
- `POST`, `GET`, `DELETE /api/payment-methods[/{paymentMethodId}]`
- `GET /api/reconciliation?from={ISO-8601}&to={ISO-8601}` (administrator)

Refund idempotency can be supplied as `idempotencyKey` in the JSON body or as the
`Idempotency-Key` header. Card requests contain `number`, `expiry` (`YYYY-MM`), `securityCode`,
`name`, and `billingAddress`; card number and security code are sent only to PayPal and are not
persisted or logged by the application.

The PayPal calls follow the official [Orders v2](https://developer.paypal.com/api/orders/v2/),
[Payments v2](https://developer.paypal.com/api/payments/v2/),
[Payment Method Tokens v3](https://developer.paypal.com/api/payment-tokens/v3/), and
[Transaction Search v1](https://developer.paypal.com/api/transaction-search/v1/) contracts.
