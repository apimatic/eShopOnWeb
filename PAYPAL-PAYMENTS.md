# PayPal payments & saved cards (PublicApi)

Additive capability on top of eShopOnWeb: collect money for orders through **PayPal** (direct
card, server-only — no browser step) and let a shopper **save a card** for reuse. All PayPal
calls go through the **paypal-sdk** plugin (`AsadAli.Checkout.Sdk`). The existing
catalog/basket/order flow is untouched.

## What was added

All endpoints live on **`src/PublicApi`** (JWT-authenticated, routed under `/api/`).

| Method & route | Who | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog items → returns **`orderId`**. Starts `PendingAuthorization`. |
| `POST /api/orders/{orderId}/pay` | shopper (own) | **Authorize** (hold) the total with a one-off card **or** a saved card. |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Fulfil → **capture** the money; records captured amount, PayPal fee, net. Renews a stale hold. |
| `POST /api/orders/{orderId}/cancel` | **admin** | Cancel before fulfilment → **void** the hold (no money moved). |
| `POST /api/orders/{orderId}/refunds` | shopper (own) | **Refund** a capture, full/partial → returns **`refundId`**. Caller-supplied `idempotencyKey`. |
| `GET /api/my-orders` | shopper | The caller's orders + payment state. |
| `GET /api/reconciliation?from=&to=` | **admin** | PayPal's transactions for a range, lined up against eShop orders. |
| `POST /api/payment-methods` | shopper | Save a card (vault) → returns **`paymentMethodId`**. Safe description only. |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper (own) | Remove a saved card; afterwards unusable. |

Design: `OrderPayment` + `PaymentRefund` + `SavedCard` aggregates (ApplicationCore); the
orchestration lives in `OrderPaymentService`; every PayPal call is behind `IPaymentGateway`,
implemented by `PayPalPaymentGateway` (Infrastructure). Orders reuse the app's existing
`Order`/`OrderItem` model.

## Configuration (secrets stay out of the repo)

Settings bind from the `PayPal:` section — `PayPal:ClientId`, `PayPal:ClientSecret`,
`PayPal:Environment`, `PayPal:Currency`, and optional `PayPal:BaseUrl` (a verbatim base-URL
override used for **every** call, including the OAuth token request; leave unset to use the
sandbox default). Load the values into **.NET user-secrets** from the environment variables
(run once, from `src/PublicApi`):

```bash
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

## Run it (this machine)

Only the .NET 10 SDK is installed (global.json pins 8.0.x) and there's no LocalDB, so run with
roll-forward + the in-memory database. From the repo root:

```bash
export DOTNET_ROLL_FORWARD=Major
cd src/PublicApi
DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true \
  ASPNETCORE_URLS="https://localhost:9863;http://localhost:9864" \
  dotnet run --no-launch-profile
```

> In-memory data is per-process and lost on restart — place, pay, fulfil and refund within one
> run. Web and PublicApi have separate in-memory stores, so drive everything through PublicApi.

Seeded users (password `Pass@word1`): `demouser@microsoft.com` (shopper),
`admin@microsoft.com` (administrator). Swagger UI: `https://localhost:9863/swagger`.

## Verify end-to-end (curl; `-k` trusts the dev cert)

```bash
API=https://localhost:9863
ST=$(curl -sk -X POST $API/api/authenticate -H "Content-Type: application/json" \
     -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
AT=$(curl -sk -X POST $API/api/authenticate -H "Content-Type: application/json" \
     -d '{"username":"admin@microsoft.com","password":"Pass@word1"}'    | python -c "import sys,json;print(json.load(sys.stdin)['token'])")

# 1) Place an order (shopper) -> orderId
curl -sk -X POST $API/api/orders -H "Authorization: Bearer $ST" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":2,"quantity":1}]}'

# 2) Authorize with the sandbox test card (hold, not captured)
curl -sk -X POST $API/api/orders/1/pay -H "Authorization: Bearer $ST" -H "Content-Type: application/json" -d '{
  "card":{"number":"4111111111111111","expiryMonth":"12","expiryYear":"2027","securityCode":"123",
          "cardholderName":"Demo User","billingAddress":{"addressLine1":"1 Market St","city":"San Jose",
          "state":"CA","postalCode":"95131","countryCode":"US"}}}'

# 3) Fulfil = capture (admin). Response shows capturedAmount / payPalFee / netAmount.
curl -sk -X POST $API/api/orders/1/fulfil -H "Authorization: Bearer $AT"

# 4) Partial refund (shopper). Repeating the SAME idempotencyKey never refunds twice.
curl -sk -X POST $API/api/orders/1/refunds -H "Authorization: Bearer $ST" -H "Content-Type: application/json" \
  -d '{"amount":10.00,"idempotencyKey":"r1"}'
curl -sk -X POST $API/api/orders/1/refunds -H "Authorization: Bearer $ST" -H "Content-Type: application/json" \
  -d '{"amount":10.00,"idempotencyKey":"r1"}'   # idempotent: same refundId, remaining unchanged
curl -sk -X POST $API/api/orders/1/refunds -H "Authorization: Bearer $ST" -H "Content-Type: application/json" \
  -d '{"amount":5.00,"idempotencyKey":"r2"}'    # distinct partial refund

# 5) See your orders + payment state
curl -sk $API/api/my-orders -H "Authorization: Bearer $ST"

# 6) Save a card (vault) -> paymentMethodId; then list
curl -sk -X POST $API/api/payment-methods -H "Authorization: Bearer $ST" -H "Content-Type: application/json" -d '{
  "card":{"number":"4111111111111111","expiryMonth":"11","expiryYear":"2028","securityCode":"321",
          "cardholderName":"Demo User","billingAddress":{"addressLine1":"1 Market St","city":"San Jose",
          "state":"CA","postalCode":"95131","countryCode":"US"}},"alias":"my visa"}'
curl -sk $API/api/payment-methods -H "Authorization: Bearer $ST"

# 7) Pay a SECOND order with the saved card, then cancel it (void the hold)
curl -sk -X POST $API/api/orders -H "Authorization: Bearer $ST" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":3,"quantity":1}]}'
curl -sk -X POST $API/api/orders/2/pay -H "Authorization: Bearer $ST" -H "Content-Type: application/json" \
  -d '{"savedCardId":1}'
curl -sk -X POST $API/api/orders/2/cancel -H "Authorization: Bearer $AT"

# 8) Reconciliation (admin). Ranges wider than 31 days are chunked automatically.
curl -sk "$API/api/reconciliation?from=2026-07-20T00:00:00Z&to=2026-08-16T23:59:59Z" -H "Authorization: Bearer $AT"
```

### Notes on expected results

- **Amounts match to the cent**: the authorized/captured amount equals the order total.
- **Reconciliation lag is normal**: PayPal's transaction report lags live activity by a few
  days, so a range covering payments you *just* made may show them as `eShopOnly` (or the recent
  range may come back empty). Over a range that already has settled data the report is correct
  and covers the whole range (it walks every page and chunks >31-day ranges).
- **Stale authorization**: at fulfilment, if the hold has expired the service renews it
  (reauthorize) and captures against the renewed hold; if it can no longer be renewed the
  response says so (HTTP 409, operator-actionable). This can't be forced quickly in the sandbox,
  so it is covered by unit tests (`Fulfil_WhenAuthorizationStale_ReauthorizesThenCaptures`,
  `Fulfil_WhenReauthorizationNotAllowed_Propagates`).
- **3-D Secure / browser challenge**: if PayPal ever answered a card payment with a challenge,
  the pay endpoint stops and returns HTTP 409 rather than building an approval round-trip. The
  sandbox test card authorizes without a challenge.

## Tests

```bash
DOTNET_ROLL_FORWARD=Major dotnet test tests/UnitTests/UnitTests.csproj                 # 60 pass (16 new)
DOTNET_ROLL_FORWARD=Major dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj   # 15 pass
```
