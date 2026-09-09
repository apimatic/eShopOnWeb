# PayPal payments & saved cards (PublicApi)

This adds real money movement to eShopOnWeb as an **additive** capability on `src/PublicApi`, with
**PayPal** as the processor: a shopper places an order, holds the money at checkout (authorize),
an operator captures it at fulfilment, and cancels/refunds follow. A shopper can also save a card
and reuse it. The existing catalog/basket/order flow is untouched — the order/order-item model is
reused, not duplicated.

All PayPal interaction goes through the PayPal REST API (Orders v2, Payments v2, Vault v3,
Transaction Search v1). Card data is sent straight to PayPal and is **never** stored in this app's
database or written to logs — only a vault token plus safe display details (brand, last four,
expiry) are kept.

## Endpoints

Shopper-scoped (JWT; acts only on the caller's own data):

| Method & route | Purpose |
| --- | --- |
| `POST /api/orders` | Place an order from catalog items → returns `orderId`. Starts *AwaitingPayment*. |
| `POST /api/orders/{orderId}/pay` | Authorize (hold) the order total with a one-off card **or** a saved card. |
| `POST /api/orders/{orderId}/refunds` | Refund the captured payment, full or partial → returns `refundId`. |
| `GET /api/my-orders` | The caller's orders with their payment state. |
| `POST /api/payment-methods` | Save (vault) a card → returns `paymentMethodId` + safe details. |
| `GET /api/payment-methods` | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | Remove a saved card. |

Operator-only (Administrators role):

| Method & route | Purpose |
| --- | --- |
| `POST /api/orders/{orderId}/fulfil` | Mark fulfilled and **capture** the held funds. |
| `POST /api/orders/{orderId}/cancel` | Cancel before fulfilment and **void** the hold. |
| `GET /api/reconciliation?from={iso}&to={iso}` | PayPal's transactions for a range, lined up against eShop orders. |

Key behaviours:
- **Amount to the cent.** The hold equals the order total computed from catalog prices; the currency
  comes from `PayPal:Currency`.
- **Idempotent in effect.** A double-click never authorizes or captures twice (our own state guard
  plus a stable `PayPal-Request-Id`). Refunds take a caller idempotency key: repeating it returns the
  same refund; two *different* keys make two legitimate partial refunds.
- **Refund cap.** Total refunds can never exceed the captured amount.
- **Stale holds** are re-authorized automatically at fulfilment; one that can no longer be renewed
  fails with an operator-actionable message.
- **3-D Secure / browser challenges** are not implemented: if PayPal returns one, the pay call fails
  with a clear message (HTTP 422) rather than attempting an approval round-trip.

## Configuration & secrets

Settings bind from the `PayPal:` section — **values are never committed**. Keys:
`PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`, and the optional
`PayPal:BaseUrl` (when set, used verbatim as the base address for *every* PayPal call, including the
token request; otherwise the base URL is derived from `Environment`).

Load the sandbox credentials from the environment into .NET user-secrets (run once, from the repo
root — this writes to your user profile, not the repo):

```bash
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"     --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET" --project src/PublicApi
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   --project src/PublicApi
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      --project src/PublicApi
```

## Run it (this machine)

Only the .NET 10 SDK / ASP.NET Core 10 runtime are installed and there is no SQL LocalDB, so roll
the SDK forward and use the in-memory database. `global.json` is already set to
`rollForward: latestMajor`.

```bash
export DOTNET_ROLL_FORWARD=Major
dotnet build src/PublicApi/PublicApi.csproj

ASPNETCORE_ENVIRONMENT=Development \
UseOnlyInMemoryDatabase=true \
ASPNETCORE_URLS="https://localhost:30143;http://localhost:30144" \
dotnet run --project src/PublicApi --no-build --no-launch-profile
```

> The in-memory store is per-host and is lost on restart, so place/pay/fulfil/refund the orders you
> create **within the same run**. Swagger is at `https://localhost:30143/swagger`.

## Verify end to end (no browser)

All commands use `curl -sk` (`-k` because the dev HTTPS cert may be untrusted). PayPal's sandbox
test card is Visa `4111 1111 1111 1111`, any future expiry, any CVC.

```bash
BASE=https://localhost:30143

# 1. Get bearer tokens (seeded users; password Pass@word1)
SHOP=$(curl -sk -X POST $BASE/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
ADMIN=$(curl -sk -X POST $BASE/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")

# 2. Place an order (catalog items 5 @ 8.50 x2 and 4 @ 12.00 = 29.00)
curl -sk -X POST $BASE/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":5,"quantity":2},{"catalogItemId":4,"quantity":1}]}'
# -> {"orderId":1, ...}

# 3. Pay (authorize / hold) with the sandbox card
curl -sk -X POST $BASE/api/orders/1/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" -d '{
  "card":{"cardNumber":"4111111111111111","expiry":"2029-01","securityCode":"123",
          "cardholderName":"Test Shopper",
          "billingAddress":{"addressLine1":"1 Market St","city":"San Jose","state":"CA","postalCode":"95131","countryCode":"US"}}}'
# -> payment.status = Authorized, authorizedAmount = 29.00, authorizationId set

# 4. Fulfil (admin) -> capture. Note capturedAmount, payPalFee, netAmount from PayPal.
curl -sk -X POST $BASE/api/orders/1/fulfil -H "Authorization: Bearer $ADMIN"
# -> payment.status = Captured, capturedAmount 29.00, payPalFee, netAmount

# 5. Partial refund (idempotency key rk-1). Repeat with the same key -> same refundId (no double refund).
curl -sk -X POST $BASE/api/orders/1/refunds -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"amount":9.00,"idempotencyKey":"rk-1"}'
# -> {"refundId":"...","amount":9.0,"status":"COMPLETED"}

# 6. Save a card, then reuse it to pay a second order
PMID=$(curl -sk -X POST $BASE/api/payment-methods -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"card":{"cardNumber":"4111111111111111","expiry":"2029-01","securityCode":"123","cardholderName":"Test Shopper","billingAddress":{"countryCode":"US"}},"alias":"My Visa"}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['paymentMethodId'])")
O2=$(curl -sk -X POST $BASE/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":4,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
curl -sk -X POST $BASE/api/orders/$O2/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d "{\"savedCardId\":$PMID}"
# -> payment.status = Authorized (paid with the saved card)

# 7. Cancel-before-fulfil (void) on a fresh order
O3=$(curl -sk -X POST $BASE/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":3,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
curl -sk -X POST $BASE/api/orders/$O3/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"card":{"cardNumber":"4111111111111111","expiry":"2029-01","securityCode":"123","billingAddress":{"countryCode":"US"}}}'
curl -sk -X POST $BASE/api/orders/$O3/cancel -H "Authorization: Bearer $ADMIN"
# -> orderStatus Cancelled, payment.status Voided

# 8. See it all
curl -sk $BASE/api/my-orders -H "Authorization: Bearer $SHOP"

# 9. Remove the saved card (afterwards it no longer lists and can't be used to pay)
curl -sk -X DELETE $BASE/api/payment-methods/$PMID -H "Authorization: Bearer $SHOP" -o /dev/null -w "%{http_code}\n"  # 204
curl -sk $BASE/api/payment-methods -H "Authorization: Bearer $SHOP"                                                    # empty

# 10. Reconciliation (admin) over a range
FROM=$(python -c "import datetime;print((datetime.datetime.now(datetime.UTC)-datetime.timedelta(days=1)).strftime('%Y-%m-%dT%H:%M:%SZ'))")
TO=$(python -c "import datetime;print(datetime.datetime.now(datetime.UTC).strftime('%Y-%m-%dT%H:%M:%SZ'))")
curl -sk "$BASE/api/reconciliation?from=$FROM&to=$TO" -H "Authorization: Bearer $ADMIN"
```

> **Reconciliation and the sandbox:** PayPal's transaction reporting lags live activity, so a range
> covering payments you just made can legitimately return `payPalTransactionCount: 0`. That is an
> expected sandbox result, not a missing capability — orders eShop has captured but PayPal has not yet
> reported appear as `InEShopOnly` lines, and over a range that PayPal *has* indexed you'll see
> `Matched` lines (joined by the `ESHOP-<orderId>-…` invoice id) and any `InPayPalOnly` transactions.

## Tests

Domain rules (refund cap, refund idempotency lookup, capture fee/net, order/payment state
transitions, invoice-id round-trip) are covered by unit tests:

```bash
export DOTNET_ROLL_FORWARD=Major
dotnet test tests/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~PaymentTests|FullyQualifiedName~OrderStatusTransitions|FullyQualifiedName~OrderInvoiceReference"
```
