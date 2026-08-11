# PayPal payments & saved cards (PublicApi)

An **additive** capability on top of eShopOnWeb: collect money for an order through **PayPal**
(authorize → capture → refund), and let a shopper **save a card** to pay a later order without
re-entering it. It does not replace the existing catalog/basket/order flow; it extends the
`Order` aggregate and adds new JWT endpoints on **`src/PublicApi`**.

Everything is drivable through the PublicApi HTTP surface alone, with a **direct card payment**
(no browser step) using PayPal's sandbox test card.

---

## What was added

### Endpoints (all under `/api`, JWT-authenticated)

| Method & route | Who | What |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog item ids + quantities. Starts **AwaitingPayment**. Returns top-level **`orderId`**. |
| `POST /api/orders/{orderId}/pay` | shopper (own order) | **Authorize** the total — a hold, not a capture. Body carries card details **or** a `savedPaymentMethodId`. |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Mark fulfilled and **capture** the money. Renews a stale authorization first; reports if it can no longer be renewed. |
| `POST /api/orders/{orderId}/cancel` | **admin** | Cancel before fulfilment — **void** the hold, no money moves. |
| `POST /api/orders/{orderId}/refunds` | shopper (own order) | **Refund** a capture, full or partial. Caller-supplied idempotency key. Returns top-level **`refundId`**. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from=&to=` | **admin** | PayPal's transactions for a date range, lined up against eShop orders. Whole range, all pages. |
| `POST /api/payment-methods` | shopper | Save (vault) a card. Returns top-level **`paymentMethodId`** and a safe descriptor (brand/last4/expiry). |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper (own card) | Remove a saved card; afterwards it can no longer be listed or used to pay. |

Shopper endpoints act only on the caller's own data (identity comes from the JWT). Fulfil,
cancel and reconciliation require the existing **Administrators** role.

### Architecture

- **ApplicationCore** — `Order` gains `Status` and a `Payment` child (the money side: PayPal
  order/authorization/capture ids + statuses, captured amount, fee, net, and a `PaymentRefund`
  collection). New `PaymentMethod` aggregate holds a vault token + safe descriptor. `IPaymentGateway`
  abstracts PayPal; `PaymentService` / `PaymentMethodService` orchestrate domain + gateway with
  idempotency and ownership checks.
- **Infrastructure** — `PayPalPaymentGateway` (plain HTTP over Orders v2, Payments v2,
  Payment-Method-Tokens v3, Transaction-Search v1), token caching, `PayPalSettings`.
- **PublicApi** — one endpoint class per route (following the project's `IEndpoint` convention),
  DI wired in `Configuration/PaymentServiceCollectionExtensions.cs`.

Full card details are only ever forwarded to PayPal — never stored in the app's database and
never written to logs.

---

## Configuration & secrets

Settings bind from the **`PayPal:`** section (never hard-coded). Load them into user-secrets
from the provided environment variables:

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   # sandbox
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      # USD
# Optional: dotnet user-secrets set "PayPal:BaseUrl" "https://api-m.sandbox.paypal.com"
```

- `PayPal:BaseUrl` is optional. When set it is used **verbatim** for every PayPal call
  (including the token request); otherwise the base is derived from `PayPal:Environment`.
- The same build runs against a different PayPal account by changing these values only.

---

## Run it

```bash
export DOTNET_ROLL_FORWARD=Major
cd <repo root>
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
# Binds to https://localhost:8443 (and http://localhost:8444)
```

Environment notes for this machine: only the .NET 10 SDK is present (roll forward with
`DOTNET_ROLL_FORWARD=Major`), and there is no LocalDB — run with **`UseOnlyInMemoryDatabase=true`**
(set as an environment variable or in configuration). The in-memory store is **per host and
per run**: place, pay, fulfil and refund the orders you create **within the same run**.

---

## Step-by-step verification (curl)

Uses `demouser@microsoft.com` (shopper) and `admin@microsoft.com` (operator), password
`Pass@word1`. `-k` skips dev-cert validation. Sandbox test card: Visa `4111 1111 1111 1111`,
any future expiry, any CVC.

```bash
B=https://localhost:8443/api
tok() { python -c "import sys,json;print(json.load(sys.stdin)['token'])"; }

# 1) Tokens
DTOK=$(curl -sk -X POST $B/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | tok)
ATOK=$(curl -sk -X POST $B/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | tok)

# 2) Place an order  -> note orderId
curl -sk -X POST $B/orders -H "Authorization: Bearer $DTOK" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":2,"quantity":1}]}'

# 3) Pay (authorize/hold) with a one-off card
curl -sk -X POST $B/orders/1/pay -H "Authorization: Bearer $DTOK" -H "Content-Type: application/json" \
  -d '{"card":{"number":"4111111111111111","expiry":"2027-01","securityCode":"123",
       "cardholderName":"Demo User",
       "billingAddress":{"line1":"1 Test St","city":"San Jose","state":"CA","postalCode":"95131","countryCode":"US"}}}'
#   -> order.payment.status = Authorized, authorizationId set, hold == order total to the cent

# 4) Fulfil (capture) — operator. Shows PayPal's captured amount, fee, net.
curl -sk -X POST $B/orders/1/fulfil -H "Authorization: Bearer $ATOK"
#   -> payment.status = Captured, capturedAmount / payPalFee / netAmount populated

# 5) Refund part of it (idempotency key required). Repeat with the same key -> same refundId.
curl -sk -X POST $B/orders/1/refunds -H "Authorization: Bearer $DTOK" -H "Content-Type: application/json" \
  -d '{"amount":10.00,"idempotencyKey":"refund-key-001"}'      # -> refundId
#   A second refund with a DIFFERENT key is a legitimate second partial refund;
#   the total refunded can never exceed the captured amount (else 422).

# 6) Save a card -> note paymentMethodId, brand, last4 (never full number)
curl -sk -X POST $B/payment-methods -H "Authorization: Bearer $DTOK" -H "Content-Type: application/json" \
  -d '{"card":{"number":"4111111111111111","expiry":"2027-01","securityCode":"123","cardholderName":"Demo User"}}'

# 7) Reuse the saved card to pay a SECOND order
curl -sk -X POST $B/orders -H "Authorization: Bearer $DTOK" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":3,"quantity":1}]}'            # -> orderId 2
curl -sk -X POST $B/orders/2/pay -H "Authorization: Bearer $DTOK" -H "Content-Type: application/json" \
  -d '{"savedPaymentMethodId":1}'                              # -> Authorized with the saved card
curl -sk -X POST $B/orders/2/fulfil -H "Authorization: Bearer $ATOK"   # capture

# 8) Cancel-before-fulfil (void): pay a new order, then cancel it
curl -sk -X POST $B/orders -H "Authorization: Bearer $DTOK" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":4,"quantity":1}]}'            # -> orderId 3
curl -sk -X POST $B/orders/3/pay -H "Authorization: Bearer $DTOK" -H "Content-Type: application/json" \
  -d '{"card":{"number":"4111111111111111","expiry":"2027-01","securityCode":"123","cardholderName":"Demo"}}'
curl -sk -X POST $B/orders/3/cancel -H "Authorization: Bearer $ATOK"   # -> Cancelled, payment Voided

# 9) The caller's orders with payment state
curl -sk $B/my-orders -H "Authorization: Bearer $DTOK"

# 10) Saved cards; delete one (then it no longer lists or pays)
curl -sk $B/payment-methods -H "Authorization: Bearer $DTOK"
curl -sk -X DELETE $B/payment-methods/1 -H "Authorization: Bearer $DTOK"   # -> 204

# 11) Reconciliation (operator). ISO-8601 date-times.
curl -sk "$B/reconciliation?from=2026-08-01T00:00:00Z&to=2026-08-12T00:00:00Z" -H "Authorization: Bearer $ATOK"
```

### What to expect on reconciliation

The report lists every PayPal transaction in the range and lines each up against eShop orders,
classifying each entry **Matched**, **PayPalOnly**, or **EShopOnly**. It chunks ranges longer
than PayPal's 31-day limit and pages through all results.

PayPal's transaction reporting **lags live activity by hours**, so payments you have *just*
created legitimately appear as `EShopOnly` (eShop has the capture; PayPal's report doesn't show
it yet) and become `Matched` once the report catches up — matched by the invoice reference eShop
stamps on each capture, or by the capture id. A range covering *just-created* payments can even
come back empty; that is an expected sandbox result, not a gap.

---

## Guarantees exercised

- **Amounts to the cent** — the hold and capture equal the order total (from catalog prices);
  currency comes from `PayPal:Currency`.
- **Idempotent in effect** — a double `pay` returns the same authorization (never a second
  hold); a double `fulfil` returns the same capture; a repeated `refund` under the same key
  returns the original refund. Operations also send a deterministic `PayPal-Request-Id` so PayPal
  itself de-duplicates.
- **Refund safety** — a partly-refunded order is never refundable beyond what was captured.
- **Ownership** — one shopper can never see, pay, refund, or delete another's order or card
  (cross-shopper access returns 404); operator-only actions return 403 for shoppers.
- **No browser step** — direct card payments and card vaulting complete server-to-server. If
  PayPal ever answers with a 3-D Secure challenge that needs browser approval, the API reports
  it as an unprocessable error rather than attempting an approval round-trip.
- **Card data** — full card details are only forwarded to PayPal; they are never persisted or
  logged.
