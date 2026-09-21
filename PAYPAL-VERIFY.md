# Verifying the PayPal payments integration

This adds **PayPal card payments** and **saved cards** to `src/PublicApi`, additively — the existing
catalog/basket/order flow is unchanged. All new capabilities are JWT-authenticated HTTP endpoints under
`/api/`.

## 1. One-time setup

**Credentials** — load the sandbox REST credentials into .NET user-secrets for the PublicApi project
(values come from your environment variables; they are never written into the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   # sandbox
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      # USD
# Optional override: dotnet user-secrets set "PayPal:BaseUrl" "https://api-m.sandbox.paypal.com"
```

The host **refuses to start** if any of ClientId/ClientSecret/Environment/Currency is missing or blank.

## 2. Run PublicApi

Only the .NET 10 SDK is installed (global.json rolls forward), and there is no LocalDB, so run with the
in-memory store (already set in `appsettings.Development.json`) and roll-forward enabled:

```bash
cd <repo root>
export DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src/PublicApi --no-launch-profile --urls "http://localhost:36564"
```

> In-memory data lives only for the run — create, pay, fulfil and refund within the **same** run.
> Swagger UI: `http://localhost:36564/swagger`.

## 3. Get bearer tokens (seeded users, password `Pass@word1`)

```bash
B=http://localhost:36564
DEMO=$(curl -s -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | grep -oE '"token":"[^"]+"' | cut -d'"' -f4)
ADMIN=$(curl -s -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | grep -oE '"token":"[^"]+"' | cut -d'"' -f4)
```

`demouser` is the shopper; `admin` is the operator (Administrators role). Sandbox test card:
**Visa `4111 1111 1111 1111`**, any future expiry (`YYYY-MM`), any CVC.

> **Shared sandbox note:** this business account is used by many runs at once, so an authorize call may
> intermittently come back `TRANSACTION_REFUSED` or `DUPLICATE_INVOICE_ID`. A refused authorize places no
> hold and leaves the order retryable — just call `.../pay` again. This is environmental, not a code fault.

## 4. Flow 1 — pay for an order

```bash
# Place an order (shopper). Returns { "orderId": N }.
ORDER=$(curl -s -X POST $B/api/orders -H "Authorization: Bearer $DEMO" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":5,"quantity":2}]}')
OID=$(echo "$ORDER" | grep -oE '"orderId":[0-9]+' | grep -oE '[0-9]+')

# Authorize (hold the money). Retry if refused (see shared-sandbox note).
curl -s -X POST $B/api/orders/$OID/pay -H "Authorization: Bearer $DEMO" -H "Content-Type: application/json" \
  -d '{"card":{"number":"4111111111111111","expiry":"2030-12","securityCode":"123","cardholderName":"Test Shopper"}}'
# → status "Authorized", plus payPalOrderId / authorizationId / authorizationExpiresAt.
# Calling it again returns the same authorization (idempotent — no double hold).

# See your orders and their payment state.
curl -s $B/api/my-orders -H "Authorization: Bearer $DEMO"

# Fulfil = capture (operator). Shows capturedAmount, payPalFee, netAmount.
curl -s -X POST $B/api/orders/$OID/fulfil -H "Authorization: Bearer $ADMIN"

# Refund (full or partial) under a caller idempotency key.
curl -s -X POST $B/api/orders/$OID/refunds -H "Authorization: Bearer $DEMO" -H "Content-Type: application/json" \
  -d '{"amount":5.00,"idempotencyKey":"refund-1","note":"partial"}'         # → { "refundId": ... }
# Same key again → returns the SAME refund (never refunds twice).
# A different key with a distinct amount → a second partial refund (allowed).
# An amount beyond the refundable remaining → 400.
```

**Cancel instead of fulfil** (operator, before fulfilment — releases the hold, no money moves):

```bash
curl -s -X POST $B/api/orders/$OID/cancel -H "Authorization: Bearer $ADMIN"   # → status "Canceled"
```

## 5. Flow 2 — saved cards

```bash
# Save a card (shopper). Returns { "paymentMethodId": N, "brand": "VISA", "last4": "1111", ... }.
SAVED=$(curl -s -X POST $B/api/payment-methods -H "Authorization: Bearer $DEMO" -H "Content-Type: application/json" \
  -d '{"card":{"number":"4111111111111111","expiry":"2030-12","securityCode":"123","cardholderName":"Test Shopper"}}')
PMID=$(echo "$SAVED" | grep -oE '"paymentMethodId":[0-9]+' | grep -oE '[0-9]+')

# List the caller's saved cards (safe description only — never full card details).
curl -s $B/api/payment-methods -H "Authorization: Bearer $DEMO"

# Reuse the saved card to pay a NEW order (no card re-entry).
ORDER2=$(curl -s -X POST $B/api/orders -H "Authorization: Bearer $DEMO" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":4,"quantity":1}]}')
OID2=$(echo "$ORDER2" | grep -oE '"orderId":[0-9]+' | grep -oE '[0-9]+')
curl -s -X POST $B/api/orders/$OID2/pay -H "Authorization: Bearer $DEMO" -H "Content-Type: application/json" \
  -d "{\"savedPaymentMethodId\":$PMID}"          # → "Authorized"

# Delete the saved card; afterwards it no longer lists and can no longer pay.
curl -s -X DELETE $B/api/payment-methods/$PMID -H "Authorization: Bearer $DEMO"   # → 204
curl -s $B/api/payment-methods -H "Authorization: Bearer $DEMO"                   # → []
```

## 6. Reconciliation (operator)

Lists PayPal's own transaction records for a date range and lines them up against eShop orders, across
**all** pages of the range (`from`/`to` are ISO-8601 date-times):

```bash
curl -s "$B/api/reconciliation?from=2026-08-21T00:00:00Z&to=2026-09-21T23:59:59Z" -H "Authorization: Bearer $ADMIN"
# → { payPalTransactionCount, matched[], inPayPalNotInEShop[], inEShopNotInPayPal[] }
```

> PayPal's transaction reporting lags live activity, so a range covering payments you just made can come
> back with `matched: []` — that is expected, not a gap. Use a range that already has settled data to see
> populated results.

## 7. Access control (try these to confirm the boundaries)

- `fulfil`, `cancel`, `reconciliation` require the **Administrators** role → `$DEMO` gets **403**.
- Every other endpoint is shopper-scoped: acting on another shopper's order/card → **404**.
- No token → **401**.

## 8. Automated tests

```bash
export DOTNET_ROLL_FORWARD=Major
dotnet test tests/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~PaymentTests|FullyQualifiedName~OrderPaymentServiceTests"
```

Covers the payment lifecycle, authorize/refund idempotency, the refund-never-exceeds-captured guard,
cross-shopper isolation, and stale-authorization renewal — all at the faked-gateway seam.
