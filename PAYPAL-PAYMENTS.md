# PayPal payments & saved cards (PublicApi)

An additive capability on `src/PublicApi`: take money for orders with **PayPal** (authorize at
checkout, capture at fulfilment, void on cancel, refund after fulfilment) and let a shopper **save a
card** and reuse it. It does not change the existing catalog/basket/order flow.

All PayPal access goes through the **PayPal Server SDK .NET** (vendored at `src/PayPalServerSdk`, built
from source because it is not on NuGet) via the `IPayPalGateway` port (`src/Infrastructure/PayPal`).

## Endpoints (all under `/api/`, JWT-authenticated)

| Method & route | Who | Purpose |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog items → `orderId`. Starts *AwaitingPayment*. |
| `POST /api/orders/{orderId}/pay` | shopper (own) | Authorize (hold) the total. Body: one-off `card`, **or** `savedPaymentMethodId`. |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Capture (take the money); renews a stale hold automatically. |
| `POST /api/orders/{orderId}/cancel` | **admin** | Void the hold before fulfilment. |
| `POST /api/orders/{orderId}/refunds` | shopper (own) | Refund full/partial → `refundId`. Body: optional `amount`, required `idempotencyKey`. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from={iso}&to={iso}` | **admin** | PayPal transactions vs eShop orders for the range (all pages). |
| `POST /api/payment-methods` | shopper | Save (vault) a card → `paymentMethodId` + safe descriptor. |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper (own) | Remove a saved card. |

Operator (admin) actions use the existing `Administrators` role. Every other endpoint is scoped to the
caller's own data. Full card numbers/CVV are never stored or logged; only the PayPal vault id plus
brand/last-four/expiry are kept for saved cards.

## Configuration

Settings bind from the `PayPal:` section (keys `PayPal:ClientId`, `PayPal:ClientSecret`,
`PayPal:Environment`, `PayPal:Currency`, optional `PayPal:BaseUrl`). Load the sandbox credentials from
the environment variables into user-secrets for the PublicApi project (values never go in the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

The host fails fast at startup if any credential is missing. `PayPal:BaseUrl`, when set, is used verbatim
as the base address for **every** PayPal call including the OAuth token request.

## Run it (this machine)

```bash
export DOTNET_ROLL_FORWARD=Major            # global.json pins 8.0.x; only .NET 10 SDK is installed
export ASPNETCORE_ENVIRONMENT=Development    # so user-secrets load
export UseOnlyInMemoryDatabase=true          # no LocalDB here; data lives for one run only
export ASPNETCORE_URLS="https://localhost:36683;http://localhost:36684"
dotnet run --project src/PublicApi --no-launch-profile
```

> In-memory means orders/payments/saved cards survive only within a single run — pay, fulfil and refund
> orders you created in that same run. Swagger UI: `https://localhost:36683/swagger`.

## Verify the working integration (no browser needed)

`curl -k` is used because it is the HTTPS dev cert. Sandbox test card: Visa `4111 1111 1111 1111`, any
future expiry, any CVC, any billing address.

```bash
B=https://localhost:36683
CARD='{"number":"4111111111111111","expiry":"2027-12","securityCode":"123","cardHolderName":"Demo Shopper","billingAddress":{"addressLine1":"1 Market St","adminArea2":"San Jose","adminArea1":"CA","postalCode":"95131","countryCode":"US"}}'

# 1. Get tokens (shopper + admin). Password for both seed users: Pass@word1
SHOP=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | python -c 'import sys,json;print(json.load(sys.stdin)["token"])')
ADMIN=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | python -c 'import sys,json;print(json.load(sys.stdin)["token"])')

# 2. Place an order (returns orderId; note the total)
curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":1,"quantity":1},{"catalogItemId":3,"quantity":1}]}'

# 3. Authorize (hold). The held amount equals the order total to the cent. Re-run → idempotent (same hold).
curl -sk -X POST $B/api/orders/1/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" -d "{\"card\":$CARD}"

# 4. Fulfil (admin) → capture. Response shows captured amount, PayPal fee, and net proceeds.
curl -sk -X POST $B/api/orders/1/fulfil -H "Authorization: Bearer $ADMIN"

# 5. Refunds: partial, idempotent repeat (same refundId), over-cap rejected (409), then the remainder.
curl -sk -X POST $B/api/orders/1/refunds -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" -d '{"amount":10.00,"idempotencyKey":"k1"}'
curl -sk -X POST $B/api/orders/1/refunds -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" -d '{"amount":10.00,"idempotencyKey":"k1"}'   # same refundId
curl -sk -X POST $B/api/orders/1/refunds -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" -d '{"amount":999,"idempotencyKey":"k2"}'      # 409 exceeds remaining
curl -sk -X POST $B/api/orders/1/refunds -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" -d '{"idempotencyKey":"k3"}'                   # full remainder → Refunded

# 6. Cancel flow: place → pay → cancel (admin) → status Cancelled, hold VOIDED.
curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":2,"quantity":1}]}'
curl -sk -X POST $B/api/orders/2/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" -d "{\"card\":$CARD}"
curl -sk -X POST $B/api/orders/2/cancel -H "Authorization: Bearer $ADMIN"

# 7. Saved card: save → list → place order → pay with savedPaymentMethodId → delete → gone/unusable.
curl -sk -X POST $B/api/payment-methods -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" -d "{\"card\":$CARD}"   # returns paymentMethodId (e.g. 1)
curl -sk $B/api/payment-methods -H "Authorization: Bearer $SHOP"
curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":4,"quantity":1}]}'
curl -sk -X POST $B/api/orders/3/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" -d '{"savedPaymentMethodId":1}'
curl -sk -X DELETE $B/api/payment-methods/1 -H "Authorization: Bearer $SHOP"                 # 204
curl -sk -X POST $B/api/orders/3/pay -H "Authorization: Bearer $SHOP" -d '{"savedPaymentMethodId":1}'   # 404 after delete

# 8. Reconciliation (admin). A range covering the last few days has settled data; a range in the last
#    hour legitimately comes back rangeEmpty:true (PayPal reporting lag) — that is expected, not a gap.
curl -sk "$B/api/reconciliation?from=$(date -u -d '-5 days' +%Y-%m-%dT%H:%M:%SZ)&to=$(date -u +%Y-%m-%dT%H:%M:%SZ)" -H "Authorization: Bearer $ADMIN"

# 9. See your orders with payment state.
curl -sk $B/api/my-orders -H "Authorization: Bearer $SHOP"
```

### What to expect
- **pay** → `status: Authorized`, `payment.authorizedAmount == total`, an `authorizationId`.
- **fulfil** → `status: Fulfilled`, a `captureId`, and `capturedAmount` / `payPalFee` / `netAmount`.
- **refunds** → `refundId`; a repeated `idempotencyKey` returns the same `refundId`; an over-cap amount is
  rejected with 409; the order becomes `PartiallyRefunded` then `Refunded`.
- **cancel** → `status: Cancelled`, `payment.authorizationStatus: VOIDED`.
- **saved card** → `paymentMethodId` + brand/last-four/expiry (no full number); reusable to pay; after
  delete it is gone from the list and returns 404 when used.
- **authorization** → a shopper calling fulfil/cancel/reconciliation gets 403; acting on another
  shopper's order gets 404; no token gets 401.

If PayPal ever answers a card payment or card-save with a browser approval challenge (3-D Secure /
`PAYER_ACTION_REQUIRED`), the API stops and reports it rather than building an approval round-trip.
