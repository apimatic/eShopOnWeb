# PayPal payments & saved cards (PublicApi)

This adds real money movement to eShopOnWeb as an **additive** capability on top of the existing
catalog/basket/order flow. PayPal is the processor; a shopper can save a card and reuse it. All
capabilities are HTTP endpoints on **`src/PublicApi`** (JWT-authenticated).

## What was added

**Flow 1 — pay for an order**

| Endpoint | Who | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog item ids + quantities (reuses the existing Order/OrderItem model). Starts `AwaitingPayment`. Returns `orderId`. |
| `POST /api/orders/{orderId}/pay` | shopper (own order) | **Authorize** (hold) the order total — money not taken. Body carries card details **or** `savedPaymentMethodId`. |
| `POST /api/orders/{orderId}/fulfil` | operator | Mark fulfilled → **capture** (take the money). Response shows PayPal's captured amount, fee, net. Renews a stale hold first. |
| `POST /api/orders/{orderId}/cancel` | operator | Cancel before fulfilment → **void** the hold (funds released). |
| `POST /api/orders/{orderId}/refunds` | shopper (own order) | **Refund** a capture, full or partial, under a caller `idempotencyKey`. Returns `refundId`. Never refundable beyond what was captured. |
| `GET /api/my-orders` | shopper | The caller's own orders with payment state. |
| `GET /api/reconciliation?from={iso}&to={iso}` | operator | PayPal's transactions for a date range lined up against eShop orders. Covers the whole range (chunked into ≤31-day windows, all pages). |

**Flow 2 — saved cards**

| Endpoint | Who | Purpose |
|---|---|---|
| `POST /api/payment-methods` | shopper | Vault a card with PayPal; store only the vault token + safe metadata. Returns `paymentMethodId`. |
| `GET /api/payment-methods` | shopper | The caller's own saved cards (brand / last four only). |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper (own) | Remove a saved card; afterwards it is neither listed nor usable to pay. |

Operator endpoints (`fulfil`, `cancel`, `reconciliation`) require the **Administrators** role.
Every other endpoint is shopper-scoped and acts only on the caller's own data. Full card details
are never stored in this app's database and never logged.

## Design

- **Domain (ApplicationCore):** `Order` gained an `OrderStatus` and an owned `Payment` (the PayPal
  state — hold/capture/refund ids + statuses, captured/fee/net). `Refund` is a child of `Payment`.
  Saved cards are a separate aggregate `CustomerPaymentMethod` scoped by buyer.
- **Gateway abstraction:** `IPayPalPaymentGateway` (ApplicationCore) keeps the SDK out of the core;
  `PayPalPaymentGateway` (Infrastructure) implements it with the **`AsadAli.Checkout.Sdk`** PayPal SDK.
- **Idempotency:** every `PayPal-Request-Id` sent is a stored, globally-unique GUID
  (`Payment.AuthorizeRequestId`/`CaptureRequestId`, `Refund.GatewayRequestId`). A double-click never
  authorizes/captures twice; a repeated refund under the same caller key returns the first refund.
- **Config:** bound from the `PayPal:` section — `PayPal:ClientId`, `PayPal:ClientSecret`,
  `PayPal:Environment`, `PayPal:Currency`, and optional `PayPal:BaseUrl` (used verbatim for every
  call, including the OAuth token request, when set). No values are hard-coded or committed.

## Running it (this machine)

Credentials are already loaded into **.NET user-secrets** for the PublicApi project. To reproduce
from the environment variables (values are read from the env, never written to a repo file):

```bash
cd <repo>
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"     --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET" --project src/PublicApi
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   --project src/PublicApi
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      --project src/PublicApi
# PayPal:BaseUrl is optional; omit it to target the sandbox default.
```

The machine has only the .NET 10 SDK and no LocalDB, so run with roll-forward and the in-memory store:

```bash
export DOTNET_ROLL_FORWARD=Major
UseOnlyInMemoryDatabase=true ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="https://localhost:9823;http://localhost:9824" \
  dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

> In-memory data lives only for one run, and Web/PublicApi have separate stores — drive the whole
> flow through PublicApi (that is why `POST /api/orders` exists). Swagger UI: `https://localhost:9823/swagger`.

## Verify by hand (curl)

```bash
B=https://localhost:9823/api
SHOP=$(curl -sk -X POST $B/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)
ADMIN=$(curl -sk -X POST $B/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | jq -r .token)
CARD='{"number":"4111111111111111","expiryMonth":"12","expiryYear":"2030","securityCode":"123"}'

# 1. place an order -> orderId
OID=$(curl -sk -X POST $B/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":5,"quantity":2},{"catalogItemId":3,"quantity":1}]}' | jq -r .orderId)

# 2. authorize (hold) with the sandbox Visa
curl -sk -X POST $B/orders/$OID/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d "{\"card\":$CARD}" | jq '{status, auth: .payment.authorizationStatus, instr: .payment.instrumentDescription}'

# 3. fulfil (operator) -> capture with fee + net
curl -sk -X POST $B/orders/$OID/fulfil -H "Authorization: Bearer $ADMIN" \
  | jq '{status, captured: .payment.capturedAmount, fee: .payment.payPalFee, net: .payment.netAmount}'

# 4. partial refund with an idempotency key -> refundId (repeat same key = same refund)
curl -sk -X POST $B/orders/$OID/refunds -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"amount":10.00,"idempotencyKey":"my-key-1"}' | jq '{refundId, status}'

# 5. saved card -> reuse it to pay a second order
PMID=$(curl -sk -X POST $B/payment-methods -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d "{\"card\":$CARD,\"alias\":\"My Visa\"}" | jq -r .paymentMethodId)
OID2=$(curl -sk -X POST $B/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":4,"quantity":1}]}' | jq -r .orderId)
curl -sk -X POST $B/orders/$OID2/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d "{\"savedPaymentMethodId\":$PMID}" | jq '{status, instr: .payment.instrumentDescription}'

# 6. reconciliation (operator) — a range covering older activity has data; a range covering only
#    payments you just made may legitimately be empty (PayPal reporting lags).
curl -sk "$B/reconciliation?from=2026-07-10T00:00:00Z&to=2026-08-09T00:00:00Z" \
  -H "Authorization: Bearer $ADMIN" | jq '.lines | length'
```

Seeded users: `demouser@microsoft.com` (shopper) and `admin@microsoft.com` (operator), password
`Pass@word1`. Sandbox test card: Visa `4111 1111 1111 1111`, any future expiry, any CVC.
