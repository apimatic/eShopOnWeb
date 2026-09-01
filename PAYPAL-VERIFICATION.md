# PayPal Payments — Verification Guide

This guide walks through verifying the PayPal payment integration end-to-end against the
PayPal **sandbox** using the test Visa card `4111 1111 1111 1111`.

## 1. Setup

### 1.1 Credentials (never committed)

The API reads PayPal settings from the `PayPal:` configuration section:

| Key | Source | Purpose |
|---|---|---|
| `PayPal:ClientId` | env `PAYPAL_CLIENT_ID` | OAuth client id |
| `PayPal:ClientSecret` | env `PAYPAL_CLIENT_SECRET` | OAuth client secret |
| `PayPal:Environment` | env `PAYPAL_ENVIRONMENT` | `sandbox` or `live` |
| `PayPal:Currency` | env `PAYPAL_CURRENCY` | e.g. `USD` |
| `PayPal:BaseUrl` | optional | overrides the environment's API base URL |

Load them into .NET user-secrets (stored outside the repo):

```powershell
dotnet user-secrets set "PayPal:ClientId" "$env:PAYPAL_CLIENT_ID" --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" "$env:PAYPAL_CLIENT_SECRET" --project src/PublicApi
dotnet user-secrets set "PayPal:Environment" "$env:PAYPAL_ENVIRONMENT" --project src/PublicApi
dotnet user-secrets set "PayPal:Currency" "$env:PAYPAL_CURRENCY" --project src/PublicApi
```

### 1.2 Run

```powershell
$env:UseOnlyInMemoryDatabase = "true"   # no LocalDB needed
dotnet run --project src/PublicApi --launch-profile PublicApi
```

The API listens on `https://localhost:21463`. Swagger UI: `https://localhost:21463/swagger`.

### 1.3 Get tokens

```powershell
$base = "https://localhost:21463"
# Shopper
$shopperToken = (curl.exe -sk -X POST "$base/api/authenticate" -H "Content-Type: application/json" `
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | ConvertFrom-Json).token
# Operator (admin role)
$adminToken = (curl.exe -sk -X POST "$base/api/authenticate" -H "Content-Type: application/json" `
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | ConvertFrom-Json).token
```

## 2. Flow 1 — Pay for an order

### 2.1 Place an order (shopper)

```powershell
curl.exe -sk -X POST "$base/api/orders" -H "Content-Type: application/json" `
  -H "Authorization: Bearer $shopperToken" `
  -d '{"items":[{"catalogItemId":3,"quantity":2}]}'
```

Expected: `201` with `{"orderId":1,"paymentStatus":"AwaitingPayment","total":24,"currency":"USD",...}`.

### 2.2 Authorize payment (shopper)

```powershell
curl.exe -sk -X POST "$base/api/orders/1/pay" -H "Content-Type: application/json" `
  -H "Authorization: Bearer $shopperToken" `
  -d '{"card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","name":"Demo Buyer"}}'
```

Expected: `200` with `"paymentStatus":"Authorized"`, a `payPalOrderId`, an `authorizationId`,
`"authorizationStatus":"CREATED"` and `authorizationExpiresAt`. **Repeat the call** — the response
returns the same `authorizationId` and no second hold is placed (idempotent).

`GET /api/my-orders` (shopper token) now lists the order with its payment state.

### 2.3 Fulfil — capture the hold (operator)

```powershell
curl.exe -sk -X POST "$base/api/orders/1/fulfil" -H "Authorization: Bearer $adminToken"
```

Expected: `200` with `"paymentStatus":"Captured"`, `captureId`, and PayPal-reported
`capturedAmount`, `payPalFee`, `netAmount`. **Repeat the call** — same `captureId`, no double charge.

If the authorization has expired (sandbox authorizations last ~30 days), fulfil automatically
re-authorizes and captures the new authorization. Authorizations older than 30 days cannot be
renewed — fulfil then returns `409` saying so.

### 2.4 Refund (operator)

```powershell
# Partial refund of $10
curl.exe -sk -X POST "$base/api/orders/1/refunds" -H "Content-Type: application/json" `
  -H "Authorization: Bearer $adminToken" -d '{"amount":10.00,"idempotencyKey":"my-refund-1"}'
```

Expected: `200` with a PayPal `refundId`, `"paymentStatus":"PartiallyRefunded"`,
`totalRefunded` and `remainingRefundable`. **Repeat with the same `idempotencyKey`** — same
`refundId`, `"replayed":true`, no second refund. A refund larger than the remaining amount
returns `409`. Omitting `amount` refunds the full remainder and moves the order to `Refunded`.

### 2.5 Cancel before fulfilment (operator)

Create and pay a second order, then:

```powershell
curl.exe -sk -X POST "$base/api/orders/2/cancel" -H "Authorization: Bearer $adminToken"
```

Expected: `200` with `"paymentStatus":"Cancelled"` and `"authorizationStatus":"VOIDED"` — the
hold is released at PayPal. Cancelling a captured order returns `409` ("issue a refund instead").

### 2.6 Reconciliation (operator)

```powershell
curl.exe -sk "$base/api/reconciliation?from=2026-09-01T00:00:00Z&to=2026-09-01T23:59:59Z" `
  -H "Authorization: Bearer $adminToken"
```

Expected: `200` with `transactions` (PayPal transactions matched to eShop orders, with amounts,
fees, statuses and the local `orderId`), `unmatchedTransactions` (PayPal activity no eShop order
claims), and `ordersMissingFromPayPalReport` (local orders with payment state that PayPal's report
does not show). The report pages through the whole date range.

> **Sandbox note:** PayPal's Transaction Search indexes new activity with a delay (often
> 15–60 minutes). Right after testing, today's transactions may appear under
> `ordersMissingFromPayPalReport`; re-run the report later to see them matched.

## 3. Flow 2 — Saved cards

```powershell
# Save (shopper)
curl.exe -sk -X POST "$base/api/payment-methods" -H "Content-Type: application/json" `
  -H "Authorization: Bearer $shopperToken" `
  -d '{"card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","name":"Demo Buyer"}}'
# -> {"paymentMethodId":1,"brand":"VISA","lastDigits":"1111","expiry":"2030-01"}  (never full PAN)

# List (shopper)
curl.exe -sk "$base/api/payment-methods" -H "Authorization: Bearer $shopperToken"

# Pay a new order with the saved card (shopper)
curl.exe -sk -X POST "$base/api/orders" -H "Content-Type: application/json" `
  -H "Authorization: Bearer $shopperToken" -d '{"items":[{"catalogItemId":5,"quantity":1}]}'
curl.exe -sk -X POST "$base/api/orders/3/pay" -H "Content-Type: application/json" `
  -H "Authorization: Bearer $shopperToken" -d '{"savedCardId":1}'

# Delete (shopper)
curl.exe -sk -X DELETE "$base/api/payment-methods/1" -H "Authorization: Bearer $shopperToken"  # 204
```

After deletion the card no longer lists, and paying with it returns `404`. The vault token is also
deleted at PayPal.

## 4. Cross-cutting checks

| Check | Expected |
|---|---|
| Any payment endpoint without a token | `401` |
| Operator endpoint (`fulfil`/`cancel`/`refunds`/`reconciliation`) with a shopper token | `403` |
| Pay/fulfil/cancel/refund for a non-existent order | `404` |
| Pay another shopper's order / use another shopper's saved card | `404` (no existence leak) |
| Fulfil an unpaid or cancelled order | `409` with reason |
| Malformed card (`expiry` not `YYYY-MM`, bad number) | `400` with reason |
| PayPal decline / rejection | `4xx`/`502` with PayPal's issue list in `issues` |

Error responses share one shape: `{"statusCode":..., "message":"...", "issues":["NAME: detail", ...]}`.

## 5. Tests

```powershell
dotnet test tests/UnitTests/UnitTests.csproj                       # incl. payment service tests
dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj
dotnet test tests/FunctionalTests/FunctionalTests.csproj
```

## 6. Implementation notes

- **SDK:** all PayPal traffic goes through `AsadAli.Checkout.Sdk` (Orders v2, Vault v3,
  Transaction Search v1) in `src/Infrastructure/PayPal/PayPalGateway.cs`.
- **Idempotency:** every PayPal write sends a `PayPal-Request-Id` derived from the order id, the
  operation, a persisted per-order attempt counter, and a per-process run component (PayPal stores
  keys for weeks and replays stored responses, so keys must be unique across app runs when the
  in-memory store resets ids). Refunds use the caller-supplied key. In-process per-order locking
  serializes concurrent mutations of the same order.
- **Invoice ids:** `order-{localOrderId}-{runId}` — traceable to the local order and unique per
  run (the sandbox account blocks duplicate invoice ids).
- **State:** orders persist PayPal order/authorization/capture ids, statuses, expiry, captured
  gross/fee/net, and a row per refund (`OrderRefund`); saved cards persist only the PayPal customer
  id, payment token id, brand, last digits and expiry (`SavedCard`).
- **Headless:** cards are charged directly via the Orders API `payment_source.card`; if PayPal ever
  demands a browser approval (3DS/SCA), the API returns `422 PAYER_ACTION_REQUIRED` instead of
  attempting a redirect flow.
