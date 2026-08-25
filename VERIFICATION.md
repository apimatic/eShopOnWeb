# PayPal Integration — End-to-End Verification Guide

Base URL: `http://localhost:16483`  
Run mode: `UseOnlyInMemoryDatabase=true` (in-memory DB, resets on restart)

---

## Prerequisites

1. Set environment variables before starting the app:
   ```
   PAYPAL_CLIENT_ID=<your sandbox client ID>
   PAYPAL_CLIENT_SECRET=<your sandbox client secret>
   PAYPAL_ENVIRONMENT=Sandbox
   PAYPAL_CURRENCY=USD
   ```
2. Load secrets: `dotnet user-secrets set PayPal:ClientId "$PAYPAL_CLIENT_ID"` etc. (or set them directly in the environment; the app reads both).
3. Start the API: `dotnet run --project src/PublicApi --urls http://localhost:16483`
4. Wait for "Application started" in the console.

---

## Step 0 — Get JWT tokens

```bash
# Regular user
USER_TOKEN=$(curl -s -X POST http://localhost:16483/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)

# Administrator (required for fulfil, cancel, reconciliation)
ADMIN_TOKEN=$(curl -s -X POST http://localhost:16483/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | jq -r .token)
```

---

## Flow 1 — Pay for an order (direct card)

### 1.1 Create an order
```bash
curl -s -X POST http://localhost:16483/api/orders \
  -H "Authorization: Bearer $USER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":5,"quantity":1}]}' | jq .
# Expected: {"orderId":1,"total":8.5,"status":"PendingPayment"}
```

### 1.2 Authorize payment (direct card)
```bash
curl -s -X POST http://localhost:16483/api/orders/1/pay \
  -H "Authorization: Bearer $USER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"card":{"number":"4111111111111111","expiry":"2027-02","securityCode":"123","name":"Test User"}}' | jq .
# Expected: {"payPalOrderId":"...","authorizationId":"...","expirationTime":"..."}
```

### 1.3 Verify idempotency (call pay again — must return same authorizationId)
```bash
curl -s -X POST http://localhost:16483/api/orders/1/pay \
  -H "Authorization: Bearer $USER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"card":{"number":"4111111111111111","expiry":"2027-02","securityCode":"123","name":"Test User"}}' | jq .
# Expected: same authorizationId as above (idempotent)
```

### 1.4 Capture (fulfil) — admin only
```bash
curl -s -X POST http://localhost:16483/api/orders/1/fulfil \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" | jq .
# Expected: {"orderId":1,"status":"Fulfilled","captureId":"...","capturedAmount":8.5,"currency":"USD"}
```

### 1.5 Partial refund (idempotent per caller key)
```bash
curl -s -X POST http://localhost:16483/api/orders/1/refunds \
  -H "Authorization: Bearer $USER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"amount":3.00,"idempotencyKey":"refund-001","reason":"Partial return"}' | jq .
# Expected: {"refundId":"...","amount":3.0,"currency":"USD","totalRefunded":3.0}
# Order status: PartiallyRefunded
```

### 1.6 Verify refund idempotency (same key again)
```bash
curl -s -X POST http://localhost:16483/api/orders/1/refunds \
  -H "Authorization: Bearer $USER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"amount":3.00,"idempotencyKey":"refund-001","reason":"Partial return"}' | jq .
# Expected: same refundId — not a double-refund
```

### 1.7 Full refund of remaining amount
```bash
curl -s -X POST http://localhost:16483/api/orders/1/refunds \
  -H "Authorization: Bearer $USER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"amount":5.50,"idempotencyKey":"refund-002","reason":"Full return"}' | jq .
# Expected: {"totalRefunded":8.5}; order status: Refunded
```

---

## Flow 2 — Cancel an authorized order

### 2.1 Create and authorize a second order
```bash
curl -s -X POST http://localhost:16483/api/orders \
  -H "Authorization: Bearer $USER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":4,"quantity":1}]}' | jq .

curl -s -X POST http://localhost:16483/api/orders/2/pay \
  -H "Authorization: Bearer $USER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"card":{"number":"4111111111111111","expiry":"2027-02","securityCode":"123","name":"Test User"}}' | jq .
```

### 2.2 Cancel (void authorization) — admin only
```bash
curl -s -X POST http://localhost:16483/api/orders/2/cancel \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" | jq .
# Expected: {"orderId":2,"status":"Cancelled"}
```

### 2.3 Cancel idempotency (call again on already-cancelled order)
```bash
curl -s -X POST http://localhost:16483/api/orders/2/cancel \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" | jq .
# Expected: {"orderId":2,"status":"Cancelled"} — idempotent 200
```

---

## Flow 3 — Saved cards (vault)

### 3.1 Save a card
```bash
curl -s -X POST http://localhost:16483/api/payment-methods \
  -H "Authorization: Bearer $USER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"number":"4111111111111111","expiry":"2027-02","securityCode":"123","name":"Test User"}' | jq .
# Expected: {"paymentMethodId":1,"last4":"1111","brand":"VISA","expiry":"2027-02"}
```

### 3.2 List saved cards
```bash
curl -s http://localhost:16483/api/payment-methods \
  -H "Authorization: Bearer $USER_TOKEN" | jq .
# Expected: array with one entry showing last4, brand, expiry — no full card number or token ID
```

### 3.3 Pay with saved card (paymentMethodId)
```bash
# Create order first
curl -s -X POST http://localhost:16483/api/orders \
  -H "Authorization: Bearer $USER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":5,"quantity":1}]}' | jq .

curl -s -X POST http://localhost:16483/api/orders/3/pay \
  -H "Authorization: Bearer $USER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"paymentMethodId":1}' | jq .
# Expected: authorizationId returned (or TRANSACTION_REFUSED in sandbox — see note below)
```

> **Note on vault token MIT in sandbox**: PayPal sandbox requires a special "Advanced Credit and Debit Card Payments" (ACDC) capability with MIT (Merchant-Initiated Transaction) enabled for vault token authorization without the cardholder present. If your sandbox account lacks this, the `/pay` endpoint will return 422 `TRANSACTION_REFUSED`. The code is correct; configure the sandbox account's capabilities at developer.paypal.com if needed. **Direct card entry always works** in sandbox.

### 3.4 Delete saved card
```bash
curl -s -X DELETE http://localhost:16483/api/payment-methods/1 \
  -H "Authorization: Bearer $USER_TOKEN"
# Expected: 204 No Content

# Verify gone
curl -s http://localhost:16483/api/payment-methods \
  -H "Authorization: Bearer $USER_TOKEN" | jq .
# Expected: empty array
```

---

## Flow 4 — List my orders

```bash
curl -s http://localhost:16483/api/my-orders \
  -H "Authorization: Bearer $USER_TOKEN" | jq .
# Expected: array of orders with nested payment info (payPalOrderId, authorizationId, captureId, totalRefunded, etc.)
# Only the caller's own orders are returned.
```

---

## Flow 5 — Reconciliation (admin only)

```bash
# Admin: works
curl -s "http://localhost:16483/api/reconciliation?from=2026-08-01T00:00:00Z&to=2026-08-26T00:00:00Z" \
  -H "Authorization: Bearer $ADMIN_TOKEN" | jq '{from,to,totalTransactions,rowCount: (.rows | length)}'
# Expected: PayPal transactions for the period, joined with local eShop orders where matched

# Non-admin: forbidden
curl -s -o /dev/null -w "%{http_code}" \
  "http://localhost:16483/api/reconciliation?from=2026-08-01T00:00:00Z&to=2026-08-26T00:00:00Z" \
  -H "Authorization: Bearer $USER_TOKEN"
# Expected: 403
```

---

## Security checks

| Check | How to verify |
|-------|---------------|
| Full card number never returned | Confirm `GET /api/payment-methods` shows only last4/brand/expiry — no number, no token ID |
| Card never stored in DB | eShop uses in-memory DB; `PaymentMethod` entity stores only `PayPalTokenId`, `Last4`, `Brand`, `Expiry` |
| Other user's order not accessible | Pay for order as user A, then request with user B's token — expect 403 |
| Admin endpoints accessible only by admin | Call `/fulfil`, `/cancel`, `/reconciliation` with user token — expect 403 |
| No credentials in repo | Run `git grep -i "client_id\|client_secret\|PAYPAL" -- "*.json" "*.yaml" "*.cs"` — expect no credential values |

---

## Known sandbox limitations

- **Vault token MIT**: PayPal sandbox returns `TRANSACTION_REFUSED` when using a v3 vault token for merchant-initiated authorization without ACDC MIT capability. This is a sandbox account configuration issue, not a code bug. Use direct card entry for end-to-end testing in default sandbox.
- **Rate limiting**: After multiple failed PayPal API calls, the sandbox may block subsequent calls for a few minutes. Wait 2–5 minutes and retry.
- **Reconciliation join**: The `orderId`/`buyerId` columns in the reconciliation report are null for PayPal transactions that don't match a local eShop order (historic sandbox transactions).
