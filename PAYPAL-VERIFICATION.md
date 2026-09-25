# Verifying the PayPal payments + saved-cards integration

This guide drives the whole integration through the **PublicApi** alone (no browser step). It matches
what I verified live on the PayPal sandbox with the test Visa `4111 1111 1111 1111`.

## 0. Prerequisites (one-time)

Credentials are read from the environment and loaded into **.NET user-secrets** (never into the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   # sandbox
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      # USD
# PayPal:BaseUrl is optional; leave unset to use the SDK's sandbox base URL.
```

Ensure the HTTPS dev cert is trusted: `dotnet dev-certs https --check`.

## 1. Run PublicApi (in-memory DB, SDK roll-forward)

```bash
cd <repo root>
export DOTNET_ROLL_FORWARD=Major
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="https://localhost:37363;http://localhost:37364" \
UseOnlyInMemoryDatabase=true \
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

> The host **refuses to start** if any `PayPal:` credential is missing/blank (fail-fast).
> The in-memory store loses data on restart, so run one session end-to-end: pay/fulfil/refund the
> orders you create in the same run.

Swagger: `https://localhost:37363/swagger`.

## 2. Get bearer tokens

```bash
B=https://localhost:37363
# Shopper
curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}'   # -> .token  (SHOP)
# Operator (administrator role)
curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}'      # -> .token  (ADMIN)
```

Use `-H "Authorization: Bearer <token>"` below. (`SHOP` = shopper, `ADMIN` = operator.)

## 3. Flow 1 — pay for an order

```bash
# Place an order (shopper). Response has a top-level orderId.
curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":1,"quantity":1},{"catalogItemId":2,"quantity":2}]}'
#   -> {"orderId":1,"total":36.50,"currency":"USD"}

# Authorize (HOLD) the total with the sandbox card (shopper).
curl -sk -X POST $B/api/orders/1/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"card":{"number":"4111111111111111","expiry":"2027-01","securityCode":"123","cardholderName":"Demo Shopper"}}'
#   -> paymentStatus:"Authorized", authorizationId, authorizedAmount == order total (to the cent)

# Fulfil = CAPTURE the held money (operator). Shows PayPal's captured amount, fee, and net proceeds.
curl -sk -X POST $B/api/orders/1/fulfil -H "Authorization: Bearer $ADMIN"
#   -> paymentStatus:"Fulfilled", captureId, capturedAmount, payPalFee, netAmount

# The caller's orders with payment state (shopper).
curl -sk $B/api/my-orders -H "Authorization: Bearer $SHOP"

# Refund part of the capture (shopper) under a caller idempotency key. Top-level refundId.
curl -sk -X POST $B/api/orders/1/refunds -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"amount":10.00,"idempotencyKey":"refund-A","note":"partial"}'
#   -> {"refundId":"...","status":"COMPLETED","amount":10.00, "payment":{...}}

# Repeat the SAME idempotencyKey -> returns the SAME refundId (never refunds twice).
curl -sk -X POST $B/api/orders/1/refunds -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"amount":10.00,"idempotencyKey":"refund-A"}'

# Over-refunding is rejected (422): amount beyond what was captured.
curl -sk -X POST $B/api/orders/1/refunds -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"amount":9999,"idempotencyKey":"refund-B"}'
```

**Cancel (void) instead of fulfil** — on a *different* order, before fulfilment:

```bash
# place + pay a new order, then:
curl -sk -X POST $B/api/orders/<id>/cancel -H "Authorization: Bearer $ADMIN"
#   -> paymentStatus:"Cancelled", authorizationStatus:"VOIDED"  (no money moved)
```

## 4. Flow 2 — saved cards, and reuse to pay

```bash
# Save a card (shopper). Response has a top-level paymentMethodId + a SAFE descriptor (never the PAN).
curl -sk -X POST $B/api/payment-methods -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"card":{"number":"4111111111111111","expiry":"2028-05","securityCode":"123","cardholderName":"Demo Shopper"}}'
#   -> {"paymentMethodId":"<PMID>","brand":"VISA","last4":"1111","expiry":"2028-05",...}

# List saved cards (shopper).
curl -sk $B/api/payment-methods -H "Authorization: Bearer $SHOP"

# Place a SECOND order and pay it with the saved card (no card details re-entered).
curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":3,"quantity":1}]}'                          # -> orderId=2
curl -sk -X POST $B/api/orders/2/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"savedPaymentMethodId":"<PMID>"}'                                     # -> Authorized
curl -sk -X POST $B/api/orders/2/fulfil -H "Authorization: Bearer $ADMIN"    # -> Fulfilled

# Delete the saved card; afterwards it is gone from the list AND unusable to pay.
curl -sk -X DELETE $B/api/payment-methods/<PMID> -H "Authorization: Bearer $SHOP"   # 204
curl -sk $B/api/payment-methods -H "Authorization: Bearer $SHOP"                    # []
curl -sk -X POST $B/api/orders/<newId>/pay -H "Authorization: Bearer $SHOP" \
  -H "Content-Type: application/json" -d '{"savedPaymentMethodId":"<PMID>"}'        # 404 not found
```

## 5. Reconciliation (operator)

```bash
curl -sk "$B/api/reconciliation?from=2026-09-01T00:00:00Z&to=2026-09-30T23:59:59Z" \
  -H "Authorization: Bearer $ADMIN"
```

Returns PayPal's own transaction record for the range **(all pages walked — `complete:true`,
`pagesScanned`, `totalPages`)** lined up against eShop orders: `matched`, `inEShopNotInPayPal`,
`inPayPalNotInEShop`.

> PayPal's transaction reporting lags live activity, so a range covering payments you *just* created
> can legitimately come back with `matched: []` and your fresh orders under `inEShopNotInPayPal`. That
> is the expected sandbox result — pick an older range that already has data to see matches.

## 6. Authorization rules to spot-check

- Operator-only endpoints (`/fulfil`, `/cancel`, `/reconciliation`) return **403** for a shopper token.
- Every endpoint returns **401** with no token.
- One shopper cannot see/pay/refund another's order (**404**), nor see/use/delete another's saved card.

## 7. Automated tests

```bash
export DOTNET_ROLL_FORWARD=Major
dotnet test tests/UnitTests/UnitTests.csproj
```

Includes the PayPal integration-seam tests (error translation, the void-204 success case) and the
order-payment service guards (over-refund, idempotent refund replay, ownership, capture fee/net).
