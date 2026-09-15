# Verifying the PayPal payments + saved-cards integration

Everything below drives **`src/PublicApi`** over JWT — no browser, no storefront. All calls hit the
PayPal **sandbox** with the direct card `4111 1111 1111 1111`.

## 0. One-time setup

Credentials are read from environment variables into **.NET user-secrets** (never written to the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
# PayPal:BaseUrl is optional; set it only to override the API base for every call.
cd ../..
```

Trust the dev cert if needed: `dotnet dev-certs https --check` (else `dotnet dev-certs https --trust`).

## 1. Run PublicApi (in-memory DB, SDK roll-forward)

```bash
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development        # so user-secrets load
export UseOnlyInMemoryDatabase=true
export ASPNETCORE_URLS="https://localhost:13903;http://localhost:13904"
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

> In-memory data lives only for this one run — create, pay, fulfil and refund within the same run.

## 2. Get bearer tokens (shopper + operator)

```bash
SHOPPER=$(curl -sk https://localhost:13903/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)
ADMIN=$(curl -sk https://localhost:13903/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | jq -r .token)
```

## 3. Flow 1 — pay for an order

```bash
# Place an order (catalog items 1..12; item 1 = $19.50, item 2 = $8.50). Returns top-level orderId.
curl -sk https://localhost:13903/api/orders -H "Authorization: Bearer $SHOPPER" \
  -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":2,"quantity":1}]}'      # -> orderId 1, $47.50

# Authorize (hold, not captured) with the sandbox card.
curl -sk https://localhost:13903/api/orders/1/pay -H "Authorization: Bearer $SHOPPER" \
  -H "Content-Type: application/json" \
  -d '{"card":{"number":"4111111111111111","expiryMonth":12,"expiryYear":2030,"securityCode":"123","cardholderName":"Test Buyer"}}'
# -> status Authorized, authorizationId set. Re-run this exact call: same authorizationId (idempotent).

# Fulfil (operator, admin-only) — this is when the money is captured.
curl -sk -X POST https://localhost:13903/api/orders/1/fulfil -H "Authorization: Bearer $ADMIN"
# -> status Captured, capturedGrossAmount 47.50, payPalFee, netProceeds (gross - fee).
#    Re-run: same captureId (idempotent). As the shopper -> 403.

# Refund after fulfilment (full or partial). idempotencyKey makes a repeat a no-op.
curl -sk -X POST https://localhost:13903/api/orders/1/refunds -H "Authorization: Bearer $SHOPPER" \
  -H "Content-Type: application/json" -d '{"amount":10.00,"idempotencyKey":"refund-A"}'   # -> refundId
curl -sk -X POST https://localhost:13903/api/orders/1/refunds -H "Authorization: Bearer $SHOPPER" \
  -H "Content-Type: application/json" -d '{"amount":10.00,"idempotencyKey":"refund-A"}'   # same refundId, no double
curl -sk -X POST https://localhost:13903/api/orders/1/refunds -H "Authorization: Bearer $SHOPPER" \
  -H "Content-Type: application/json" -d '{"amount":9999,"idempotencyKey":"refund-B"}'     # -> 409, never beyond captured

# Cancel is for an order BEFORE fulfilment (operator). Place+authorize a fresh order, then:
curl -sk -X POST https://localhost:13903/api/orders/2/cancel -H "Authorization: Bearer $ADMIN"
# -> status Cancelled (held funds released; no money moved).

# The caller's orders with payment state:
curl -sk https://localhost:13903/api/my-orders -H "Authorization: Bearer $SHOPPER"

# Reconciliation (operator): PayPal's own transactions for a range, matched to eShop orders.
curl -sk "https://localhost:13903/api/reconciliation?from=2026-08-20T00:00:00Z&to=2026-08-22T00:00:00Z" \
  -H "Authorization: Bearer $ADMIN"
# Note: PayPal reporting lags a few hours, so a range covering only just-created payments may return
# empty legitimately — widen the range to a prior day to see real matched/unmatched rows.
```

## 4. Flow 2 — saved cards

```bash
# Save a card (returns top-level paymentMethodId + a SAFE description: brand + last4 + expiry, never the PAN).
curl -sk https://localhost:13903/api/payment-methods -H "Authorization: Bearer $SHOPPER" \
  -H "Content-Type: application/json" \
  -d '{"card":{"number":"4111111111111111","expiryMonth":11,"expiryYear":2031,"securityCode":"123","cardholderName":"Saved Holder"}}'

curl -sk https://localhost:13903/api/payment-methods -H "Authorization: Bearer $SHOPPER"   # list

# Pay a NEW order with the saved card (no card details re-entered):
curl -sk https://localhost:13903/api/orders -H "Authorization: Bearer $SHOPPER" \
  -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":3,"quantity":1}]}'    # -> orderId N
curl -sk https://localhost:13903/api/orders/N/pay -H "Authorization: Bearer $SHOPPER" \
  -H "Content-Type: application/json" -d '{"savedPaymentMethodId":1}'                       # -> Authorized

# Delete the saved card; afterwards it is gone from the list and can no longer pay.
curl -sk -X DELETE https://localhost:13903/api/payment-methods/1 -H "Authorization: Bearer $SHOPPER"
curl -sk https://localhost:13903/api/payment-methods -H "Authorization: Bearer $SHOPPER"   # empty
```

## 5. Automated tests (no network)

```bash
export DOTNET_ROLL_FORWARD=Major
dotnet test tests/UnitTests/UnitTests.csproj                       # Payment state machine + orchestration
dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj   # endpoints, auth, ownership, idempotency
```

The functional tests replace PayPal with an in-process fake, so they run offline and stay green.

## Ownership & roles (spot checks)

- A shopper hitting `fulfil` / `cancel` / `reconciliation` → **403** (operator-only).
- Acting on another shopper's order or saved card → **404** (never revealed).
- Full card numbers are never stored in the app DB and never logged.
