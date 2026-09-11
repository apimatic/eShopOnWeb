# PayPal payments & saved cards — eShopOnWeb PublicApi

Additive capability that lets a shopper pay for an order by card via **PayPal** and reuse a **saved
card**, plus the operator flows that follow a real payment (fulfil → capture, cancel → void, refund).
The existing catalog/basket/order flow is untouched; a new `OrderPayment` aggregate carries the money
and fulfilment state, linked 1:1 to the existing `Order`.

## What was added

- **Domain** (`ApplicationCore`): `OrderPayment` (+ `PaymentRefund`, `PaymentStatus`) and
  `SavedPaymentMethod` aggregates; `IPaymentProcessor` provider abstraction with plain DTOs;
  `PaymentService` / `PaymentMethodService`.
- **Infrastructure**: `PayPalPaymentProcessor` (all PayPal SDK usage is confined here), `PayPalSettings`
  with startup fail-fast, DI in `AddPayPalPayments`, EF configs + `CatalogContext` DbSets. The PayPal
  Server SDK (.NET) is vendored under `src/PayPalServerSdk` (it is not published to NuGet) and built
  from source.
- **PublicApi** (`src/PublicApi/PaymentEndpoints`): the JWT-authenticated HTTP endpoints below, using
  the project's existing `MinimalApi.Endpoint` convention.

| Endpoint | Who | What |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog items → `orderId`. Awaits payment. |
| `POST /api/orders/{orderId}/pay` | shopper | **Authorize** (hold) the order total with a card or a saved card. |
| `POST /api/orders/{orderId}/fulfil` | operator | **Capture** the held funds; records captured/fee/net. Renews a stale hold. |
| `POST /api/orders/{orderId}/cancel` | operator | **Void** the hold before fulfilment. |
| `POST /api/orders/{orderId}/refunds` | shopper | **Refund** a captured order, full/partial → `refundId`. Idempotency-keyed. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from&to` | operator | PayPal's transactions for a range vs eShop orders. |
| `POST /api/payment-methods` | shopper | Save (vault) a card → `paymentMethodId` + safe descriptor. |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{id}` | shopper | Remove a saved card. |

## Configuration & secrets

Settings bind from the `PayPal:` section (keys `ClientId`, `ClientSecret`, `Environment`, `Currency`,
optional `BaseUrl`). **No values live in the repo.** Load the sandbox credentials into .NET user-secrets
for `src/PublicApi` from your environment variables (run once):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   # sandbox
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"       # e.g. USD
```

The host **refuses to start** if any required credential is missing/blank (it names the missing key).
Optional `PayPal:BaseUrl`, when set, is used verbatim for every PayPal call including the OAuth token
request.

## Run the API (this machine's constraints)

```bash
export DOTNET_ROLL_FORWARD=Major          # global.json pins 8.0.x; only .NET 10 SDK is installed
export ASPNETCORE_ENVIRONMENT=Development  # loads user-secrets
export UseOnlyInMemoryDatabase=true        # no LocalDB here; data lives only within one run
export ASPNETCORE_URLS="https://localhost:31423;http://localhost:31424"
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

Because the in-memory store is per-host and reset on restart, **pay/fulfil/refund the orders you create
in the same run**. Swagger UI: `https://localhost:31423/swagger`.

## Verify end to end (curl; `-k` skips the dev-cert trust prompt)

```bash
B=https://localhost:31423 ; CT="Content-Type: application/json"

# 1. Tokens (both users seeded; password Pass@word1)
SHOP=$(curl -k -s -X POST $B/api/authenticate -H "$CT" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | grep -oP '"token":"\K[^"]+')
ADMIN=$(curl -k -s -X POST $B/api/authenticate -H "$CT" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | grep -oP '"token":"\K[^"]+')

# 2. Place an order (shopper) — returns orderId
curl -k -s -X POST $B/api/orders -H "$CT" -H "Authorization: Bearer $SHOP" \
  -d '{"items":[{"catalogItemId":2,"quantity":1},{"catalogItemId":3,"quantity":2}]}'

# 3. Pay = authorize a hold with the sandbox test card (Visa 4111...; any future expiry/CVC)
curl -k -s -X POST $B/api/orders/1/pay -H "$CT" -H "Authorization: Bearer $SHOP" \
  -d '{"card":{"cardNumber":"4111111111111111","expiry":"2030-01","securityCode":"123","cardholderName":"Demo User"}}'

# 4. Fulfil = capture (operator). Response shows capturedAmount / payPalFee / netAmount.
curl -k -s -X POST $B/api/orders/1/fulfil -H "Authorization: Bearer $ADMIN"

# 5. Refund part of it (shopper) — idempotencyKey required; repeat with same key never refunds twice
curl -k -s -X POST $B/api/orders/1/refunds -H "$CT" -H "Authorization: Bearer $SHOP" \
  -d '{"amount":10.00,"idempotencyKey":"my-refund-1"}'

# 6. Saved card: save it, then pay a NEW order with it (no card details re-entered)
curl -k -s -X POST $B/api/payment-methods -H "$CT" -H "Authorization: Bearer $SHOP" \
  -d '{"card":{"cardNumber":"4111111111111111","expiry":"2030-05","securityCode":"123","cardholderName":"Demo User"}}'
curl -k -s -X POST $B/api/orders -H "$CT" -H "Authorization: Bearer $SHOP" \
  -d '{"items":[{"catalogItemId":4,"quantity":1}]}'
curl -k -s -X POST $B/api/orders/2/pay -H "$CT" -H "Authorization: Bearer $SHOP" \
  -d '{"savedPaymentMethodId":1}'
curl -k -s -X POST $B/api/orders/2/fulfil -H "Authorization: Bearer $ADMIN"

# 7. Cancel (operator) releases a hold before fulfilment — try on a fresh authorized order.
curl -k -s -X POST $B/api/orders/3/cancel -H "Authorization: Bearer $ADMIN"

# 8. My orders (shopper) and reconciliation (operator; <=31-day range; may be empty due to reporting lag)
curl -k -s $B/api/my-orders -H "Authorization: Bearer $SHOP"
curl -k -s "$B/api/reconciliation?from=2026-08-13T00:00:00Z&to=2026-09-12T00:00:00Z" \
  -H "Authorization: Bearer $ADMIN"
```

Notes: PayPal's transaction reporting lags, so a reconciliation range covering payments you just created
may legitimately come back empty — run it over a range that already has data. Operator endpoints
(`fulfil`, `cancel`, `reconciliation`) require the `Administrators` role; every other endpoint acts only
on the caller's own data. Full card numbers are never stored or logged.

## Tests

```bash
DOTNET_ROLL_FORWARD=Major dotnet test tests/UnitTests/UnitTests.csproj \
  --filter "FullyQualifiedName~OrderPaymentTests|FullyQualifiedName~PaymentServiceTests"
```
