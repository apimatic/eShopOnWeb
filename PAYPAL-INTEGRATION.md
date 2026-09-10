# PayPal payments & saved cards (eShopOnWeb PublicApi)

An additive capability on **`src/PublicApi`**: a shopper places an order, pays for it by card (via
**PayPal**, sandbox), and an operator fulfils/cancels/refunds it; a shopper can also save a card and reuse it.
It does not replace the existing catalog/basket/order flow.

## What was added

- **Domain** (`src/ApplicationCore/Entities/PaymentAggregate/`): `OrderPayment` (payment + fulfilment state,
  1:1 with the existing `Order`), `PaymentRefund`, `SavedPaymentMethod`, `PaymentStatus`.
- **Gateway seam**: `IPaymentGateway` (SDK-free, in `ApplicationCore`) implemented by
  `Infrastructure/Payments/PayPalPaymentGateway` over the vendored **PayPal Server SDK** (`src/PayPalServerSdk/`).
- **Services**: `OrderPaymentService`, `SavedCardService` (`ApplicationCore/Services`).
- **Endpoints** (`src/PublicApi/PaymentEndpoints/`), JWT-authenticated, routed under `/api/`.
- Card numbers/CVV are never stored or logged; only PayPal's vault token id + safe display (brand, last-4,
  expiry) is kept.

## Endpoints

| Method & route | Who | Purpose |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog items → `orderId` |
| `POST /api/orders/{orderId}/pay` | shopper (own) | Authorize (hold) the total — one-off card **or** `savedPaymentMethodId` |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Capture the held funds (renews a stale hold; reports gross/fee/net) |
| `POST /api/orders/{orderId}/cancel` | **admin** | Void the hold before fulfilment (releases funds) |
| `POST /api/orders/{orderId}/refunds` | shopper (own) | Refund a capture, full or partial → `refundId` (carries `idempotencyKey`) |
| `GET /api/my-orders` | shopper | The caller's orders + payment state |
| `GET /api/reconciliation?from&to` | **admin** | PayPal's transactions lined up against eShop orders (ISO-8601 range) |
| `POST /api/payment-methods` | shopper | Save (vault) a card → `paymentMethodId` |
| `GET /api/payment-methods` | shopper | The caller's saved cards |
| `DELETE /api/payment-methods/{id}` | shopper (own) | Remove a saved card |

## Configuration (secrets)

Settings bind from the **`PayPal:`** section (`PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`,
`PayPal:Currency`, optional `PayPal:BaseUrl`). Never put values in the repo. Load them into user-secrets from
the environment variables:

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   # sandbox
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      # e.g. USD
```

The host **fails fast** at startup if any of these is missing or blank.

## Run it (this machine)

The SDK is pinned to 8.0.x but only the .NET 10 SDK is installed and the ASP.NET Core 8 runtime is missing,
and there is no LocalDB — so roll forward and use the in-memory database:

```bash
cd <repo root>
export DOTNET_ROLL_FORWARD=Major
dotnet build src/PublicApi/PublicApi.csproj

ASPNETCORE_ENVIRONMENT=Development \
UseOnlyInMemoryDatabase=true \
ASPNETCORE_URLS="https://localhost:30403;http://localhost:30404" \
dotnet run --project src/PublicApi --no-launch-profile --no-build
```

> The in-memory store is per-process and resets on restart, and Web/PublicApi have separate stores — so
> create, pay, fulfil and refund an order **within the same PublicApi run**, driving everything through this
> API (that is why `POST /api/orders` exists).

## Verify it yourself (curl)

`-k` accepts the dev cert. Get a shopper and an operator token first:

```bash
B=https://localhost:30403
SHOP=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | grep -oE '"token":"[^"]+"' | sed 's/"token":"//;s/"//')
ADMIN=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}'   | grep -oE '"token":"[^"]+"' | sed 's/"token":"//;s/"//')
```

### Flow 1 — pay, fulfil, refund

```bash
# 1) place an order (note the returned orderId, e.g. 1)
curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" -d '{
  "items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":2,"quantity":1}],
  "shipToAddress":{"street":"1 Main","city":"Redmond","state":"WA","country":"USA","zipCode":"98052"}}'

# 2) authorize with the sandbox test card (a hold; no money taken yet)
curl -sk -X POST $B/api/orders/1/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" -d '{
  "card":{"number":"4111111111111111","expiry":"2027-01","securityCode":"123","cardholderName":"Demo User"}}'

# 3) operator fulfils → capture; response shows capturedAmount, payPalFee, netAmount
curl -sk -X POST $B/api/orders/1/fulfil -H "Authorization: Bearer $ADMIN"

# 4) shopper's orders with payment state
curl -sk $B/api/my-orders -H "Authorization: Bearer $SHOP"

# 5) partial refund (idempotencyKey must be unique per distinct refund; repeating it returns the same refund)
curl -sk -X POST $B/api/orders/1/refunds -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"amount":10.00,"idempotencyKey":"refund-'"$(date +%s)"'-a"}'
```

Cancel instead of fulfil (before fulfilment) to release the hold:

```bash
curl -sk -X POST $B/api/orders/<id>/cancel -H "Authorization: Bearer $ADMIN"
```

### Flow 2 — saved cards

```bash
# save a card (returns paymentMethodId; only brand + last 4 are shown)
curl -sk -X POST $B/api/payment-methods -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" -d '{
  "card":{"number":"4111111111111111","expiry":"2028-05","securityCode":"123","cardholderName":"Demo User"}}'

curl -sk $B/api/payment-methods -H "Authorization: Bearer $SHOP"

# place a second order, then pay it with the saved card
curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" -d '{
  "items":[{"catalogItemId":3,"quantity":1}],
  "shipToAddress":{"street":"2 Main","city":"Redmond","state":"WA","country":"USA","zipCode":"98052"}}'
curl -sk -X POST $B/api/orders/2/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"savedPaymentMethodId":1}'

# delete the saved card (afterwards it no longer lists and can no longer pay)
curl -sk -i -X DELETE $B/api/payment-methods/1 -H "Authorization: Bearer $SHOP"
```

### Reconciliation (operator)

```bash
curl -sk "$B/api/reconciliation?from=2026-09-01T00:00:00Z&to=2026-09-11T00:00:00Z" -H "Authorization: Bearer $ADMIN"
```

Lists PayPal's own transactions for the range (all windows/pages) lined up against eShop orders
(`Matched` / `PayPalOnly` / `EShopOnly`). **Note:** PayPal's transaction reporting lags, so payments you
just created may show as `EShopOnly` (or the range may be empty) — that is expected sandbox behaviour, not a
missing capability.

## Notes

- Operator actions (`fulfil`, `cancel`, `reconciliation`) require the `Administrators` role; everything else
  is shopper-scoped and acts only on the caller's own data.
- Payment operations are idempotent in effect (a double-click never authorizes or captures twice); refunds
  are deduplicated by the caller-supplied `idempotencyKey`, and a partial refund can never exceed the
  captured amount.
- If PayPal ever answered a card payment with a browser-approval challenge (3-D Secure / payer action), the
  API returns an actionable error rather than attempting a browser round-trip. This did not occur with the
  sandbox test card.
