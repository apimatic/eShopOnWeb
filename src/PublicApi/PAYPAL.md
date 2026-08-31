# PayPal payments API

The PublicApi supports two-step PayPal card payments: checkout authorizes the order total, fulfilment captures it, cancellation voids an uncaptured authorization, and returns refund a capture. It also supports PayPal-vaulted cards and transaction reconciliation.

## Configuration

Configuration is bound from the `PayPal` section. The application maps the supplied environment variables to that section, and local development can also use user-secrets:

```powershell
dotnet user-secrets set 'PayPal:ClientId' $env:PAYPAL_CLIENT_ID --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'PayPal:ClientSecret' $env:PAYPAL_CLIENT_SECRET --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'PayPal:Environment' $env:PAYPAL_ENVIRONMENT --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'PayPal:Currency' $env:PAYPAL_CURRENCY --project src/PublicApi/PublicApi.csproj
```

`PayPal:BaseUrl` is optional. When present, it is used verbatim as the base address for the OAuth token request and every API call. Otherwise `PayPal:Environment` selects PayPal's sandbox or live base address.

For this repository's in-memory setup:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
$env:UseOnlyInMemoryDatabase='true'
dotnet run --project src/PublicApi/PublicApi.csproj
```

Keep PublicApi running for the whole test because its in-memory database is process-local. Authenticate at `POST /api/authenticate`, then pass the returned token as `Authorization: Bearer <token>`.

## API sequence

1. `POST /api/payment-methods` with `name`, `number`, `expiry`, `securityCode`, and `billingAddress` to vault a card. The response returns `paymentMethodId` plus brand, last four, and expiry only.
2. `POST /api/orders` with catalog item IDs, quantities, and a shipping address. The response returns `orderId`.
3. `POST /api/orders/{orderId}/pay` with either a `card` object or `{ "paymentMethodId": 1 }`. PayPal holds exactly the catalog-derived order total.
4. As an administrator, call `POST /api/orders/{orderId}/fulfil` to capture or `POST /api/orders/{orderId}/cancel` to void the hold.
5. After capture, call `POST /api/orders/{orderId}/refunds` as the owning shopper. Supply `amount` (omit it for the remaining full amount) and a unique `idempotencyKey`. Reusing the key returns the original `refundId`.
6. Use `GET /api/my-orders`, `GET /api/payment-methods`, and administrator-only `GET /api/reconciliation?from=<ISO-8601>&to=<ISO-8601>` to inspect state.

Direct card fields exist only in the incoming request and the in-memory PayPal request body. They are never persisted or logged. Direct card handling requires the merchant to maintain the applicable PCI compliance level.
