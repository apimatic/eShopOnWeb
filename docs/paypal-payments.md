# PayPal payments API

PublicApi exposes a headless, JWT-authenticated PayPal authorization/capture/refund flow and PayPal Vault saved cards. Card numbers and security codes are transient request data and are never persisted.

## Configuration

Load the supplied environment variables into PublicApi user-secrets (the values stay outside the repository):

```powershell
dotnet user-secrets set "PayPal:ClientId" "$env:PAYPAL_CLIENT_ID" --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" "$env:PAYPAL_CLIENT_SECRET" --project src/PublicApi
dotnet user-secrets set "PayPal:Environment" "$env:PAYPAL_ENVIRONMENT" --project src/PublicApi
dotnet user-secrets set "PayPal:Currency" "$env:PAYPAL_CURRENCY" --project src/PublicApi
```

`PayPal:BaseUrl` is optional. When configured, it is the base address for OAuth and every PayPal API call. Otherwise the base address is derived from `PayPal:Environment` (`sandbox` or `live`).

For the local in-memory run:

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
dotnet run --project src/PublicApi --launch-profile PublicApi
```

## End-to-end sequence

1. `POST /api/authenticate` as `demouser@microsoft.com`, then use its `token` as a bearer token.
2. `POST /api/orders` with catalog item IDs/quantities and a shipping address. Keep the top-level `orderId`.
3. `POST /api/orders/{orderId}/pay` with either `card` or `paymentMethodId`. A card has `number`, `expiry` (`YYYY-MM`), `securityCode`, `name`, and `billingAddress`. The response must be `Authorized` and contains the PayPal authorization ID.
4. Authenticate as `admin@microsoft.com`; `POST /api/orders/{orderId}/fulfil`. The response contains the PayPal capture ID, captured amount, PayPal fee, and merchant net.
5. As the shopper, `POST /api/orders/{orderId}/refunds` with a unique `idempotencyKey` and optional `amount`. Omitting `amount` refunds the remaining capture. Keep the top-level `refundId`.
6. To test cancellation instead, authorize another order and call `POST /api/orders/{orderId}/cancel` as the administrator. The authorization status becomes `VOIDED`.
7. `POST /api/payment-methods` with a card, keep the top-level `paymentMethodId`, and use it in step 3 for another order. `GET /api/payment-methods` returns only redacted recognition data. `DELETE /api/payment-methods/{paymentMethodId}` removes the PayPal token and prevents later use.
8. As the administrator, call `GET /api/reconciliation?from={ISO-8601}&to={ISO-8601}`. `payPalDataThrough` identifies the reporting freshness boundary; recent local captures can appear as `EShopOnly` until PayPal's documented reporting delay passes.

`POST /pay`, `/fulfil`, and `/cancel` are idempotent for an order. Refund idempotency is scoped to the caller-provided key, so repeated keys return the original refund while distinct keys permit legitimate partial refunds.
