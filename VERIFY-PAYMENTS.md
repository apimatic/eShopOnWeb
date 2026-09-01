# Verifying the PayPal payment integration

Step-by-step guide to drive every payment flow end to end through `src/PublicApi`
against the PayPal sandbox. No browser step is required.

## 0. Prerequisites

1. PayPal sandbox credentials in the environment:

   ```powershell
   $env:PAYPAL_CLIENT_ID     # sandbox app client id
   $env:PAYPAL_CLIENT_SECRET # sandbox app secret
   $env:PAYPAL_ENVIRONMENT   # "sandbox"
   $env:PAYPAL_CURRENCY      # e.g. "USD"
   ```

2. Load them into user-secrets for PublicApi (values never enter the repo):

   ```powershell
   cd src/PublicApi
   dotnet user-secrets set "PayPal:ClientId" $env:PAYPAL_CLIENT_ID
   dotnet user-secrets set "PayPal:ClientSecret" $env:PAYPAL_CLIENT_SECRET
   dotnet user-secrets set "PayPal:Environment" $env:PAYPAL_ENVIRONMENT
   dotnet user-secrets set "PayPal:Currency" $env:PAYPAL_CURRENCY
   ```

   `PayPal:BaseUrl` is an optional override; when set it is used verbatim as the
   base address for every PayPal call (including the token request).

3. Run PublicApi (in-memory store; keep this single instance running for the
   whole session — orders, payments and saved cards do not survive a restart):

   ```powershell
   $env:DOTNET_ROLL_FORWARD = 'Major'          # only .NET 10 SDK is installed
   $env:UseOnlyInMemoryDatabase = 'true'       # no LocalDB on this machine
   $env:ASPNETCORE_ENVIRONMENT = 'Development' # user-secrets only load in Development
   dotnet run --urls http://localhost:5296
   ```

4. Get bearer tokens (PublicApi uses JWT; the Web storefront cookie does not work here):

   ```powershell
   $base = 'http://localhost:5296'
   $shopper = (Invoke-RestMethod -Method Post -Uri "$base/api/authenticate" -ContentType 'application/json' -Body '{"username":"demouser@microsoft.com","password":"Pass@word1"}').token
   $admin   = (Invoke-RestMethod -Method Post -Uri "$base/api/authenticate" -ContentType 'application/json' -Body '{"username":"admin@microsoft.com","password":"Pass@word1"}').token
   ```

All bodies below are JSON; send header `Authorization: Bearer $shopper` (or `$admin`).

## 1. Place an order — `POST /api/orders` (shopper)

```json
{ "items": [ { "catalogItemId": 2, "quantity": 1 } ] }
```

Response carries `orderId` and `total`; the order starts as `AwaitingPayment`.

## 2. Pay — `POST /api/orders/{orderId}/pay` (shopper)

One-off card (PayPal sandbox test card):

```json
{
  "card": {
    "number": "4111111111111111",
    "expiry": "2031-06",
    "securityCode": "123",
    "cardholderName": "Demo User",
    "billingAddress": { "addressLine1": "1 Main St", "city": "San Jose", "state": "CA", "postalCode": "95131", "countryCode": "US" }
  }
}
```

Or pay with a saved card instead: `{ "paymentMethodId": 1 }`.

The response shows `orderStatus: PaymentAuthorized` and the payment with
`status: Authorized`, the PayPal order/authorization ids, and the hold expiry.
The held amount equals the order total to the cent. Repeating the call returns
the same authorization (no second hold). Full card details are never stored or
logged — only brand and last four digits.

## 3. Fulfil — `POST /api/orders/{orderId}/fulfil` (admin)

Captures the held money. The payment now shows `status: Captured` with
`capturedAmount`, `payPalFee` and `netAmount` exactly as PayPal reported them.
Calling it again is a no-op (idempotent). If the authorization has gone stale it
is renewed automatically; one that can no longer be renewed returns `409` with an
operator-actionable message.

## 4. Cancel — `POST /api/orders/{orderId}/cancel` (admin)

Voids the authorization before fulfilment: the hold is released, no money moves,
payment shows `status: Voided`. Cancelling a fulfilled order returns `409`
(use a refund instead).

## 5. Refund — `POST /api/orders/{orderId}/refunds` (admin)

```json
{ "amount": 5.00, "idempotencyKey": "any-unique-key", "note": "partial refund" }
```

Omit `amount` for a full refund. The response carries `refundId`,
`totalRefunded` and `remainingRefundable`. Repeating with the same
`idempotencyKey` returns the same refund without refunding twice; a different
key issues a distinct partial refund. Refunding beyond the captured amount
returns `409`.

## 6. Saved cards (shopper)

- `POST /api/payment-methods` — body `{ "card": { ...same shape as pay... } }`.
  Response carries `paymentMethodId`, brand, last four digits and expiry only.
- `GET /api/payment-methods` — the caller's saved cards.
- `DELETE /api/payment-methods/{paymentMethodId}` — removes it; afterwards it no
  longer lists and paying with it returns `404`.

A shopper only ever sees/uses/deletes their own cards and orders — another
user's ids return `404`.

## 7. My orders — `GET /api/my-orders` (shopper)

The caller's orders with items, totals and full payment state (authorization,
capture, refunds, remaining refundable amount).

## 8. Reconciliation — `GET /api/reconciliation?from=...&to=...` (admin)

ISO-8601 range, max 31 days, e.g.
`/api/reconciliation?from=2026-08-31T00:00:00Z&to=2026-09-01T00:00:00Z`.
Lists PayPal's own transaction records for the range (all pages) lined up
against eShop payments: `Matched`, `MissingInEShop` (PayPal knows it, eShop
doesn't) or `MissingInPayPal`. Note PayPal's reporting lags live activity, so
just-created payments legitimately appear as `MissingInPayPal` (or the range
comes back empty) in the sandbox — that is expected, not a gap.

## Authorization matrix

| Endpoint | Shopper | Admin |
|---|---|---|
| orders, pay, my-orders, payment-methods | own data only | own data only |
| fulfil, cancel, refunds, reconciliation | `403` | allowed |
| any endpoint without a token | `401` | `401` |
