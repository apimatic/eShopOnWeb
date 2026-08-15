# PayPal payments & saved cards (PublicApi)

This adds real money movement to eShopOnWeb — **additively**, on the `src/PublicApi` (JWT) host —
using **PayPal** (sandbox) via the paypal-sdk plugin. It reuses the existing `Order`/`OrderItem`
model; the classic catalog/basket/checkout flow is untouched.

Money lifecycle: **place → authorize (hold) → fulfil (capture) / cancel (void) / refund**, plus
**vaulted saved cards** and a **reconciliation** report. All PayPal calls go through a single seam,
`IPayPalGateway` (implemented in `src/Infrastructure/PayPal/PayPalGateway.cs`); no card number is
ever stored in this app's database or written to logs.

## Endpoints

| Method & route | Who | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | Place an order (catalog item ids + quantities). Returns `orderId`. Starts *AwaitingPayment*. |
| `POST /api/orders/{orderId}/pay` | shopper (own) | Authorize the total — a hold, not a capture. Body carries `card` **or** `savedPaymentMethodId`. |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Capture the hold. Response shows captured amount, PayPal fee, net. Renews a stale hold first. |
| `POST /api/orders/{orderId}/cancel` | **admin** | Void the hold before fulfilment; funds released. |
| `POST /api/orders/{orderId}/refunds` | shopper (own) | Refund a capture, full or partial. Body: `amount?`, `idempotencyKey`. Returns `refundId`. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from={iso}&to={iso}` | **admin** | PayPal's transactions vs. eShop payments over a range (paged to exhaustion). |
| `POST /api/payment-methods` | shopper | Vault a card. Returns `paymentMethodId` + safe descriptor (brand/last4/expiry). |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{id}` | shopper (own) | Remove a saved card; afterwards it cannot pay. |

Idempotency: authorize and capture are idempotent in effect (a double-click never charges twice);
refunds use the caller's `idempotencyKey` (repeating it returns the original refund; two distinct
partial refunds use two distinct keys and both proceed; refunds can never exceed the captured amount).

## Configuration

Bound from the `PayPal:` section — **never hard-coded, never committed**:

| Key | From env var |
|---|---|
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` |
| `PayPal:Currency` | `PAYPAL_CURRENCY` |
| `PayPal:BaseUrl` | *(optional)* verbatim API base URL used for **every** call incl. the OAuth token request |

Load them into **.NET user-secrets** for the PublicApi project (values stay outside the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

## Run (this machine)

Only the .NET 10 SDK is installed and there's no LocalDB, so roll the SDK forward and use the
in-memory database. Bind only to your assigned port block (PublicApi → `https://localhost:9183`).

```bash
ASPNETCORE_ENVIRONMENT=Development \
DOTNET_ROLL_FORWARD=Major \
UseOnlyInMemoryDatabase=true \
ASPNETCORE_URLS="https://localhost:9183;http://localhost:9184" \
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

> In-memory data lives only for one run and each host has its own store — so place, pay, fulfil and
> refund the orders you create **within the same run**, through PublicApi alone.

## Verify it yourself (curl)

Use `curl -k` (dev cert). Get a token first; the storefront cookie won't work here.

```bash
B=https://localhost:9183
SHOP=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | grep -o '"token":"[^"]*' | cut -d'"' -f4)
ADMIN=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}'   | grep -o '"token":"[^"]*' | cut -d'"' -f4)
CARD='{"number":"4111111111111111","expiryMonth":12,"expiryYear":2030,"securityCode":"123","cardholderName":"Test","countryCode":"US","addressLine1":"1 Market St","adminArea1":"CA","adminArea2":"San Francisco","postalCode":"94105"}'
```

1. **Place an order** (returns `orderId`):
   ```bash
   curl -sk -X POST $B/api/orders -H "Content-Type: application/json" -H "Authorization: Bearer $SHOP" \
     -d '{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":2,"quantity":1}]}'
   ```
2. **Authorize** the hold with the sandbox card (`payment.authorizationStatus` → `CREATED`, `authorizedAmount` = order total):
   ```bash
   curl -sk -X POST $B/api/orders/1/pay -H "Content-Type: application/json" -H "Authorization: Bearer $SHOP" \
     -d "{\"card\":$CARD}"
   ```
3. **Fulfil** as admin — the capture happens here (`captureStatus` → `COMPLETED`, with `payPalFee` and `netAmount`):
   ```bash
   curl -sk -X POST $B/api/orders/1/fulfil -H "Authorization: Bearer $ADMIN"
   ```
4. **Refund** part of it (returns `refundId`); repeat with the same key to see it *not* refund twice:
   ```bash
   curl -sk -X POST $B/api/orders/1/refunds -H "Content-Type: application/json" -H "Authorization: Bearer $SHOP" \
     -d '{"amount":5.00,"idempotencyKey":"k1"}'
   ```
5. **Saved card, reused:** vault a card, then pay a *new* order with it:
   ```bash
   PM=$(curl -sk -X POST $B/api/payment-methods -H "Content-Type: application/json" -H "Authorization: Bearer $SHOP" \
        -d "{\"card\":$CARD,\"alias\":\"my card\"}" | grep -o '"paymentMethodId":[0-9]*' | cut -d: -f2)
   OID=$(curl -sk -X POST $B/api/orders -H "Content-Type: application/json" -H "Authorization: Bearer $SHOP" \
        -d '{"items":[{"catalogItemId":3,"quantity":1}]}' | grep -o '"orderId":[0-9]*' | cut -d: -f2)
   curl -sk -X POST $B/api/orders/$OID/pay -H "Content-Type: application/json" -H "Authorization: Bearer $SHOP" \
     -d "{\"savedPaymentMethodId\":$PM}"
   ```
6. **Cancel (void) before fulfil:** place+pay another order, then `POST /api/orders/{id}/cancel` as admin
   (`authorizationStatus` → `VOIDED`).
7. **My orders / reconciliation:**
   ```bash
   curl -sk $B/api/my-orders -H "Authorization: Bearer $SHOP"
   curl -sk "$B/api/reconciliation?from=2026-08-15T00:00:00Z&to=2026-08-16T00:00:00Z" -H "Authorization: Bearer $ADMIN"
   ```

Notes:
- **Reconciliation may be empty for very recent ranges** — PayPal's transaction reporting lags live
  activity. That is expected; the report is correct over a range that already has data, and it pages
  through the whole range. Your just-created payments show as *in eShop only* until PayPal's reporting
  catches up.
- **Stale-hold renewal** (reauthorize before capture, and the "can no longer be renewed" operator
  error) can't be triggered in a short session — a PayPal hold only goes stale after days. The logic is
  implemented in `OrderPaymentService.FulfilAsync` and covered by unit tests
  (`tests/UnitTests/ApplicationCore/Services/OrderPaymentServiceTests.cs`).
- If PayPal ever answers a card payment with a browser/3-D-Secure challenge, the API returns HTTP 409
  with a clear message rather than building an approval round-trip (by design).
