# PayPal payments & saved cards (PublicApi)

An additive capability on top of eShopOnWeb's existing catalog/basket/order model: a shopper
places an order and pays by card via **PayPal** (authorize → capture → refund), and can save a
card to reuse on a later order. All of it is drivable through the **`src/PublicApi`** JWT API.

## Endpoints

| Method & route | Who | What |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog items (`items:[{catalogItemId,quantity}]`, optional `shipToAddress`). Returns **`orderId`**. Starts *AwaitingPayment*. |
| `POST /api/orders/{orderId}/pay` | shopper (own) | **Authorize** (hold) the order total. Body: `{card:{…}}` **or** `{savedPaymentMethodId}`. No capture yet. |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Capture the held funds. Response carries PayPal's captured amount, fee and net proceeds. Renews a stale hold automatically. |
| `POST /api/orders/{orderId}/cancel` | **admin** | Void the hold before fulfilment (funds released). |
| `POST /api/orders/{orderId}/refunds` | shopper (own) | Refund the capture, full or partial. Body: `{amount?, idempotencyKey}`. Returns **`refundId`**. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from=&to=` | **admin** | PayPal's transaction record lined up against eShop payments over an ISO-8601 range. |
| `POST /api/payment-methods` | shopper | Save a card. Returns **`paymentMethodId`** + safe descriptor (brand/last4/expiry). |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper (own) | Remove a saved card. |

Design notes: PayPal is reached only through the `paypal-sdk` (`AsadAli.Checkout.Sdk`) behind
`IPaymentGateway` (impl in `src/Infrastructure/PayPal`). Full card numbers are never stored in
the app database and never logged. Payment operations are idempotent in effect (a per-order
token anchors PayPal's `PayPal-Request-Id`; refunds use the caller's idempotency key). A
partly-refunded order can never be refunded beyond what was captured. If PayPal ever answered a
card with a 3-D Secure/payer-action challenge, the API stops and reports it rather than building
a browser round-trip.

## Prerequisites (this machine)

The credentials come from environment variables and are loaded into **.NET user-secrets** (never
written into the repo):

```bash
dotnet user-secrets --project src/PublicApi set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets --project src/PublicApi set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets --project src/PublicApi set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets --project src/PublicApi set "PayPal:Currency"     "$PAYPAL_CURRENCY"
# PayPal:BaseUrl is an optional override; leave unset to use the sandbox default.
```

The SDK is pinned to .NET 8 while only the .NET 10 SDK/runtime is installed, and there is no
LocalDB — so run with roll-forward and the in-memory store:

```bash
export DOTNET_ROLL_FORWARD=Major
export UseOnlyInMemoryDatabase=true
export ASPNETCORE_ENVIRONMENT=Development           # so user-secrets load
export ASPNETCORE_URLS="https://localhost:13523;http://localhost:13524"
dotnet run --project src/PublicApi --no-launch-profile
```

The in-memory store is per-process and resets on restart, so create/pay/fulfil/refund the orders
you make **within one run**.

## Step-by-step verification (curl)

```bash
B=https://localhost:13523
tok(){ curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d "{\"username\":\"$1\",\"password\":\"Pass@word1\"}" | python -c "import sys,json;print(json.load(sys.stdin)['token'])"; }
DEMO=$(tok demouser@microsoft.com); ADMIN=$(tok admin@microsoft.com)
CARD='{"card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","countryCode":"US","billingPostalCode":"95131"}}'

# 1) Place an order (returns orderId)
O=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $DEMO" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":5,"quantity":2}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")

# 2) Authorize (hold) with the sandbox Visa
curl -sk -X POST $B/api/orders/$O/pay -H "Authorization: Bearer $DEMO" -H "Content-Type: application/json" -d "$CARD"

# 3) Fulfil = capture (admin). Response shows capturedAmount, payPalFee, netAmount
curl -sk -X POST $B/api/orders/$O/fulfil -H "Authorization: Bearer $ADMIN"

# 4) Partial refund (idempotent by key; repeating the same key never refunds twice)
curl -sk -X POST $B/api/orders/$O/refunds -H "Authorization: Bearer $DEMO" -H "Content-Type: application/json" \
  -d '{"amount":4.00,"idempotencyKey":"demo-refund-1"}'

# 5) The caller's orders with payment state
curl -sk $B/api/my-orders -H "Authorization: Bearer $DEMO"

# 6) Save a card, then reuse it to pay a NEW order
PM=$(curl -sk -X POST $B/api/payment-methods -H "Authorization: Bearer $DEMO" -H "Content-Type: application/json" -d "$CARD" \
  | python -c "import sys,json;print(json.load(sys.stdin)['paymentMethodId'])")
O2=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $DEMO" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":4,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
curl -sk -X POST $B/api/orders/$O2/pay -H "Authorization: Bearer $DEMO" -H "Content-Type: application/json" -d "{\"savedPaymentMethodId\":$PM}"
curl -sk -X POST $B/api/orders/$O2/fulfil -H "Authorization: Bearer $ADMIN"

# 7) Delete the saved card; afterwards it is gone and unusable
curl -sk -X DELETE $B/api/payment-methods/$PM -H "Authorization: Bearer $DEMO" -w " -> %{http_code}\n"

# 8) Cancel-before-fulfil (void): pay another order then cancel it as admin
O3=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $DEMO" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":5,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
curl -sk -X POST $B/api/orders/$O3/pay -H "Authorization: Bearer $DEMO" -H "Content-Type: application/json" -d "$CARD" >/dev/null
curl -sk -X POST $B/api/orders/$O3/cancel -H "Authorization: Bearer $ADMIN"

# 9) Reconciliation (admin). A range covering just-created payments may come back empty because
#    PayPal's reporting lags live activity — that is expected in sandbox, not a gap.
curl -sk "$B/api/reconciliation?from=2026-08-01T00:00:00Z&to=2026-08-31T23:59:59Z" -H "Authorization: Bearer $ADMIN"
```

Authorization checks worth confirming: the operator routes reject a shopper token with **403**
(`fulfil`, `cancel`, `reconciliation`), and one shopper acting on another's order or saved card
gets **404**.

## Automated tests

```bash
export DOTNET_ROLL_FORWARD=Major
dotnet test tests/UnitTests/UnitTests.csproj                             # domain rules (refund cap, idempotency, order state)
dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj   # API surface with an in-memory gateway fake
```
