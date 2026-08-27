# Verifying the PayPal integration

All flows run through `src/PublicApi` (JWT auth) against the PayPal **sandbox**.
Credentials come from .NET user-secrets (already seeded from the `PAYPAL_*` env
vars); nothing secret is in the repo.

## 0. Start the API

```powershell
cd <repo root>
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:UseOnlyInMemoryDatabase = "true"          # no LocalDB on this machine
dotnet run --project src/PublicApi --urls "https://localhost:17843;http://localhost:17844"
```

Wait for `Now listening on: https://localhost:17843`. Keep this process running
for the whole session — the in-memory store resets on restart, so create and
operate on orders within one run.

## 1. Get bearer tokens

```powershell
$shopper = (curl.exe -s -X POST https://localhost:17843/api/authenticate `
  -H "Content-Type: application/json" `
  -d '{"email":"demouser@microsoft.com","password":"Pass@word1"}' | ConvertFrom-Json).token

$admin = (curl.exe -s -X POST https://localhost:17843/api/authenticate `
  -H "Content-Type: application/json" `
  -d '{"email":"admin@microsoft.com","password":"Pass@word1"}' | ConvertFrom-Json).token
```

## 2. Save a card (Flow 2)

Sandbox test card: Visa `4111111111111111`, any future expiry / any CVC.

```powershell
curl.exe -s -X POST https://localhost:17843/api/payment-methods `
  -H "Content-Type: application/json" -H "Authorization: Bearer $shopper" `
  -d '{"number":"4111111111111111","expiry":"2028-12","securityCode":"123","holderName":"Demo User"}'
```

Response: `{"paymentMethodId":1,"brand":"VISA","lastDigits":"1111","expiry":"2028-12"}` —
safe display fields only, never the full number.

```powershell
curl.exe -s https://localhost:17843/api/payment-methods -H "Authorization: Bearer $shopper"
```

## 3. Place an order

```powershell
curl.exe -s -X POST https://localhost:17843/api/orders `
  -H "Content-Type: application/json" -H "Authorization: Bearer $shopper" `
  -d '{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":3,"quantity":1}]}'
```

Response carries `orderId` and starts in `AwaitingPayment`.

## 4. Authorize (hold the money, don't take it)

With the saved card:

```powershell
curl.exe -s -X POST https://localhost:17843/api/orders/1/pay `
  -H "Content-Type: application/json" -H "Authorization: Bearer $shopper" `
  -d '{"paymentMethodId":1}'
```

Or with raw card details instead:

```powershell
curl.exe -s -X POST https://localhost:17843/api/orders/1/pay `
  -H "Content-Type: application/json" -H "Authorization: Bearer $shopper" `
  -d '{"card":{"number":"4111111111111111","expiry":"2028-12","securityCode":"123","holderName":"Demo User"}}'
```

Response: `status: PaymentAuthorized` with `payPalOrderId`, `authorizationId`,
`authorizationStatus: CREATED` and an expiry ~30 days out. The held amount equals
the order total to the cent.

## 5. Fulfil (operator — takes the money)

```powershell
curl.exe -s -X POST https://localhost:17843/api/orders/1/fulfil `
  -H "Content-Type: application/json" -H "Authorization: Bearer $admin" -d '{}'
```

Response: `status: Fulfilled` with `captureId`, `capturedAmount`, `payPalFee`,
`netAmount` as reported by PayPal. A stale hold is reauthorized automatically;
one that cannot be renewed returns an actionable error. A shopper token here
gets `403`.

## 6. Refund (operator — full or partial, idempotent)

```powershell
curl.exe -s -X POST https://localhost:17843/api/orders/1/refunds `
  -H "Content-Type: application/json" -H "Authorization: Bearer $admin" `
  -d '{"amount":10.00,"idempotencyKey":"my-refund-1"}'
```

Response carries `refundId`. Repeating the **same** `idempotencyKey` returns the
same `refundId` without refunding twice; a different key performs a second
partial refund. Refunding beyond the captured remainder is rejected with `422`.

## 7. Cancel before fulfilment (operator — releases the hold)

On a different order that is authorized but not yet fulfilled:

```powershell
curl.exe -s -X POST https://localhost:17843/api/orders/2/cancel `
  -H "Content-Type: application/json" -H "Authorization: Bearer $admin" -d '{}'
```

Response: `status: Cancelled`, `authorizationStatus: VOIDED` — no money moved.

## 8. Shopper views their own orders

```powershell
curl.exe -s https://localhost:17843/api/my-orders -H "Authorization: Bearer $shopper"
```

Shows each order with full payment state (authorization, capture, fee/net,
refunds, refundable remainder). Other users' orders are invisible (`404`).

## 9. Delete a saved card

```powershell
curl.exe -s -X DELETE https://localhost:17843/api/payment-methods/1 -H "Authorization: Bearer $shopper"
curl.exe -s https://localhost:17843/api/payment-methods -H "Authorization: Bearer $shopper"   # now empty
```

Paying with the deleted id afterwards fails with `404`.

## 10. Reconciliation (operator)

```powershell
curl.exe -s "https://localhost:17843/api/reconciliation?from=2026-08-01T00:00:00Z&to=2026-08-31T23:59:59Z" `
  -H "Authorization: Bearer $admin"
```

Lists PayPal's own transaction records for the range (all pages), each with
`matchedOrderId` when it lines up with an eShop order (by invoice id / custom
field / PayPal ids), plus `unmatchedPayPalTransactionCount` and
`ordersMissingFromPayPal`. Note: sandbox transaction reporting lags live
activity, so a range covering payments created minutes ago can legitimately
come back empty — query a wider/older range to see data.

## What was verified against the live sandbox

- Real authorization on the test card (`CREATED`, 30-day honor period)
- Real capture at fulfilment: $51.00 captured, $1.81 PayPal fee, $49.19 net
- Saved card vaulted, reused to pay a second order, listed, deleted, and
  confirmed unusable after deletion
- Partial refunds ($10 + $5), repeat under the same idempotency key returning
  the same `refundId`, over-refund rejected with `422`
- Cancel voiding the authorization (`VOIDED`)
- Shopper isolation: admin token sees no shopper orders (`404`/empty), shopper
  token gets `403` on operator endpoints
- Reconciliation over a 30-day range returning 1,408 PayPal transactions fully
  paged, with match/unmatch reporting
