# PayPal payments & saved cards (PublicApi)

This adds real money movement to eShopOnWeb as an **additive** capability on the
`src/PublicApi` project (JWT-authenticated). A shopper places an order, authorizes it (a hold),
an operator fulfils it (the money is captured), and returns are refunded. Shoppers can also save
a card once and reuse it. PayPal is the processor; **every** PayPal interaction goes through the
PayPal REST API exactly as documented by the `paypal` plugin (Orders v2, Payments v2, Vault v3,
Transaction Search v1). No card number is ever stored in this app's database or written to logs.

## Endpoints

| Method & route | Who | What |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog item ids + quantities (prices come from the catalog). Starts **AwaitingPayment**. Returns `orderId`. |
| `POST /api/orders/{orderId}/pay` | shopper (own order) | **Authorize** (hold) the order total. Body carries `card{…}` **or** `savedCardId`. Optional `saveCard:true` vaults a one-off card. |
| `POST /api/orders/{orderId}/fulfil` | **admin** | **Capture** the held money. Response shows PayPal's captured amount, fee and net. Renews a stale hold first; if it can't be renewed, returns an actionable error. |
| `POST /api/orders/{orderId}/cancel` | **admin** | Void the hold before fulfilment — no money moves. |
| `POST /api/orders/{orderId}/refunds` | shopper (own order) | Refund the capture, full or partial. Body: `{ amount?, idempotencyKey }`. Returns `refundId`. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from={iso}&to={iso}` | **admin** | PayPal's transaction record for the range lined up against eShop orders. |
| `POST /api/payment-methods` | shopper | Save (vault) a card. Returns `paymentMethodId` + safe descriptor (brand, last4, expiry). |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{id}` | shopper (own card) | Remove a saved card (also removed from PayPal's vault; no longer usable to pay). |

Shopper endpoints act only on the caller's own data; `fulfil`, `cancel` and `reconciliation`
require the `Administrators` role. Payment operations are idempotent in effect — a double-click
never authorizes or captures twice; a refund repeated under the same `idempotencyKey` never
refunds twice, while two distinct keys are two legitimate partial refunds.

## Configuration

Settings are bound from the `PayPal:` section (never hard-coded):

| Key | From env var | Notes |
|---|---|---|
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` | |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` | |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` | `sandbox` (default) or `live`/`production` |
| `PayPal:Currency` | `PAYPAL_CURRENCY` | order/hold/capture currency |
| `PayPal:BaseUrl` | — | optional; when set it is used verbatim for **every** call (incl. the OAuth token request) instead of deriving one from `Environment` |

Load the credentials into **.NET user-secrets** (kept outside the repo). From `src/PublicApi`:

```bash
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

## Run (this machine)

The SDK is .NET 10 but the app targets net8.0, and there is no SQL LocalDB — so roll forward and
use the in-memory store. The in-memory provider is **per-host** and resets on restart, so place,
pay, fulfil and refund within the **same run**.

```bash
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development           # so user-secrets load
export ASPNETCORE_URLS="https://localhost:9923;http://localhost:9924"
export UseOnlyInMemoryDatabase=true
dotnet dev-certs https --check                       # ensure the dev cert is trusted
dotnet run --project src/PublicApi/PublicApi.csproj
```

Swagger: `https://localhost:9923/swagger`.

## Verify end to end (no browser)

Uses PayPal's sandbox test card Visa `4111 1111 1111 1111`, any future expiry / CVC.
`-k` skips local cert validation for curl.

```bash
BASE=https://localhost:9923

# 1) Tokens (admin = operator, demo = shopper). Default password: Pass@word1
AT=$(curl -sk -X POST $BASE/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | jq -r .token)
DT=$(curl -sk -X POST $BASE/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)

CARD='{"card":{"number":"4111111111111111","expiry":"2027-01","securityCode":"123","name":"John Doe",
       "billingAddress":{"addressLine1":"123 Main St","adminArea2":"San Jose","adminArea1":"CA",
       "postalCode":"95131","countryCode":"US"}}}'

# 2) Place an order (shopper) -> orderId
OID=$(curl -sk -X POST $BASE/api/orders -H "Authorization: Bearer $DT" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":5,"quantity":2},{"catalogItemId":4,"quantity":1}]}' | jq -r .orderId)

# 3) Authorize a hold for the exact order total (shopper)
curl -sk -X POST $BASE/api/orders/$OID/pay -H "Authorization: Bearer $DT" -H "Content-Type: application/json" -d "$CARD" | jq

# 4) See it as the shopper
curl -sk $BASE/api/my-orders -H "Authorization: Bearer $DT" | jq

# 5) Fulfil = capture (operator). Response shows capturedAmount, payPalFee, netAmount.
curl -sk -X POST $BASE/api/orders/$OID/fulfil -H "Authorization: Bearer $AT" | jq

# 6) Partial refund (shopper). Repeat with the SAME key -> same refund, no double refund.
curl -sk -X POST $BASE/api/orders/$OID/refunds -H "Authorization: Bearer $DT" -H "Content-Type: application/json" \
  -d '{"amount":9.00,"idempotencyKey":"refund-1"}' | jq

# 7) Saved card: save it, then reuse it to pay a second order
PMID=$(curl -sk -X POST $BASE/api/payment-methods -H "Authorization: Bearer $DT" -H "Content-Type: application/json" \
  -d "$CARD" | jq -r .paymentMethodId)
O2=$(curl -sk -X POST $BASE/api/orders -H "Authorization: Bearer $DT" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":4,"quantity":2}]}' | jq -r .orderId)
curl -sk -X POST $BASE/api/orders/$O2/pay -H "Authorization: Bearer $DT" -H "Content-Type: application/json" \
  -d "{\"savedCardId\":$PMID}" | jq
curl -sk -X POST $BASE/api/orders/$O2/fulfil -H "Authorization: Bearer $AT" | jq

# 8) Cancel-before-fulfil (operator) releases a hold; no money moves
O3=$(curl -sk -X POST $BASE/api/orders -H "Authorization: Bearer $DT" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":3,"quantity":1}]}' | jq -r .orderId)
curl -sk -X POST $BASE/api/orders/$O3/pay    -H "Authorization: Bearer $DT" -H "Content-Type: application/json" -d "$CARD" >/dev/null
curl -sk -X POST $BASE/api/orders/$O3/cancel -H "Authorization: Bearer $AT" | jq

# 9) Reconciliation over a range that has data (operator)
curl -sk "$BASE/api/reconciliation?from=2026-08-01T00:00:00Z&to=2026-08-16T23:59:59Z" -H "Authorization: Bearer $AT" | jq '{payPalTransactionCount,matchedCount,inPayPalNotInEShopCount,inEShopNotInPayPalCount}'

# 10) Delete a saved card -> gone, and no longer usable to pay
curl -sk -X DELETE $BASE/api/payment-methods/$PMID -H "Authorization: Bearer $DT" -w "HTTP %{http_code}\n"
```

Expected: step 3 holds the exact order total; step 5 shows fee/net from PayPal; step 6 refunds
and is idempotent per key; step 7 pays a second order with the saved card; step 8 releases the
hold; step 10 makes the card unusable (a later pay with it returns 404).

## Notes

- **Reconciliation coverage.** The report pages through PayPal's transaction search and splits the
  requested range into ≤31-day windows, so it covers the *whole* range, not just the first page.
  It matches on the unique reference this app sends as `custom_id`/`invoice_id` plus the PayPal
  ids it stores. PayPal's transaction reporting **lags** live activity, so a range covering
  payments you just created may legitimately come back empty (or show them as *eShop-only* until
  reporting catches up) — that is expected, not a gap.
- **Stale holds.** At fulfilment, an authorization that has gone stale is reauthorized before the
  capture; if it can no longer be renewed, the operator gets an actionable message
  ("… ask the shopper to pay for the order again …").
- **Security.** JWT identity comes from the token, never the request body. Full card details flow
  straight to PayPal and are never persisted or logged; only a safe descriptor (brand + last4) is
  kept. Secrets live in user-secrets, never in the repo.
