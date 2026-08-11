# PayPal payments & saved cards — eShopOnWeb

This adds real money movement to eShopOnWeb via **PayPal** (direct card payments, no browser
step) plus **saved (vaulted) cards**, exposed as JWT-authenticated HTTP endpoints on
`src/PublicApi`. It is **additive** — the existing catalog/basket/order flow is untouched.

## What was added

**Domain (`src/ApplicationCore`)** — additive to the `Order` aggregate:
- `Order.Status` (`AwaitingPayment → PaymentAuthorized → Fulfilled | Cancelled`), `Order.PaymentReference`
  (a GUID that seeds idempotency keys), and behaviour methods (`RecordAuthorization`, `RecordFulfilment`,
  `RecordCancellation`).
- `Payment` (PayPal order/authorization/capture ids + statuses, captured amount, PayPal fee, net
  proceeds) and `Refund` children; a `SavedCard` aggregate (vault id + safe description only).
- `IPayPalGateway` abstraction + DTOs, and `OrderPaymentService`, `SavedCardService`,
  `ReconciliationService`. Card numbers/CVV are never stored in the app DB and never logged.

**Infrastructure (`src/Infrastructure/Payments`)** — the *only* place that talks to PayPal, via the
`paypal-sdk` plugin (`AsadAli.Checkout.Sdk`): `PayPalGateway` + `AddPayPalIntegration` DI wiring.

**API (`src/PublicApi/PaymentEndpoints`)** — the endpoints below.

| Method & route | Who | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | place an order from catalog items → returns `orderId` |
| `POST /api/orders/{id}/pay` | shopper (own order) | **authorize** (hold) the total, by card or a saved card |
| `POST /api/orders/{id}/fulfil` | **admin** | fulfil → **capture** the money (shows captured/fee/net) |
| `POST /api/orders/{id}/cancel` | **admin** | cancel before fulfilment → void the hold |
| `POST /api/orders/{id}/refunds` | shopper (own order) | refund full/partial → returns `refundId` |
| `GET /api/my-orders` | shopper | the caller's orders with payment state |
| `GET /api/reconciliation?from=&to=` | **admin** | PayPal transactions lined up against eShop orders |
| `POST /api/payment-methods` | shopper | save a card → returns `paymentMethodId` |
| `GET /api/payment-methods` | shopper | the caller's saved cards |
| `DELETE /api/payment-methods/{id}` | shopper (own card) | remove a saved card |

## Configuration & secrets

Settings bind from the `PayPal:` section — `PayPal:ClientId`, `PayPal:ClientSecret`,
`PayPal:Environment`, `PayPal:Currency`, and optional `PayPal:BaseUrl` (verbatim override for **all**
calls incl. the token request). **No secret values live in the repo.** Load them into .NET user-secrets
for `src/PublicApi` (from the environment variables provided):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

## Run the API (this machine)

Only the .NET 10 SDK is installed and there's no LocalDB, so roll forward and use the in-memory store.
The in-memory store is **per-process and resets on restart** — do a full pay→fulfil→refund within one run.

```bash
cd <repo root>
DOTNET_ROLL_FORWARD=Major \
UseOnlyInMemoryDatabase=true \
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="https://localhost:8723;http://localhost:8724" \
dotnet run --no-launch-profile --project src/PublicApi/PublicApi.csproj
```

Swagger: <https://localhost:8723/swagger>. Seeded users (password `Pass@word1`):
`demouser@microsoft.com` (shopper) and `admin@microsoft.com` (administrator/operator).
Use `curl -k` (dev cert). The sandbox test card is Visa `4111 1111 1111 1111`, any future expiry, any CVC.

## Step-by-step verification (curl)

```bash
B=https://localhost:8723/api
CARD='{"number":"4111111111111111","expiryMonth":"11","expiryYear":"2027","securityCode":"123","cardholderName":"Demo User","billingAddress":{"addressLine1":"1 Market St","city":"San Jose","state":"CA","countryCode":"US","postalCode":"95131"}}'

# 1. Tokens
SHOP=$(curl -sk -X POST $B/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
ADMIN=$(curl -sk -X POST $B/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")

# 2. Place an order (amounts come from catalog prices)
OID=$(curl -sk -X POST $B/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":5,"quantity":1},{"catalogItemId":3,"quantity":2}]}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")

# 3. Pay = AUTHORIZE (hold, not captured). Status -> PaymentAuthorized, authorization CREATED
curl -sk -X POST $B/orders/$OID/pay -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d "{\"card\":$CARD}"

# 4. Fulfil = CAPTURE (admin). Payment now shows capturedAmount, payPalFee, netAmount
curl -sk -X POST $B/orders/$OID/fulfil -H "Authorization: Bearer $ADMIN"

# 5. Refund partial (own order). Repeat with the SAME idempotencyKey => same refundId, no double refund
curl -sk -X POST $B/orders/$OID/refunds -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"amount":10.00,"idempotencyKey":"demo-key-1"}'
# A second DISTINCT key makes a second partial refund; refunding beyond the capture returns HTTP 400.

# 6. Saved cards: save, then pay a second order with it, then fulfil it
PMID=$(curl -sk -X POST $B/payment-methods -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d "{\"card\":$CARD}" | python -c "import sys,json;print(json.load(sys.stdin)['paymentMethodId'])")
OID2=$(curl -sk -X POST $B/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":4,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
curl -sk -X POST $B/orders/$OID2/pay -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d "{\"savedCardId\":$PMID}"
curl -sk -X POST $B/orders/$OID2/fulfil -H "Authorization: Bearer $ADMIN"

# 7. Cancel (void) before fulfilment: place + pay a third order, then cancel
OID3=$(curl -sk -X POST $B/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":5,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
curl -sk -X POST $B/orders/$OID3/pay -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d "{\"card\":$CARD}"
curl -sk -X POST $B/orders/$OID3/cancel -H "Authorization: Bearer $ADMIN"

# 8. My orders (with payment state)
curl -sk $B/my-orders -H "Authorization: Bearer $SHOP"

# 9. Delete a saved card (afterwards it no longer lists and can't be used to pay)
curl -sk -X DELETE $B/payment-methods/$PMID -H "Authorization: Bearer $SHOP"

# 10. Reconciliation (admin), whole range, all pages. from/to are ISO-8601 date-times.
FROM=$(date -u -d '30 days ago' +%Y-%m-%dT%H:%M:%SZ); TO=$(date -u +%Y-%m-%dT%H:%M:%SZ)
curl -sk "$B/reconciliation?from=$FROM&to=$TO" -H "Authorization: Bearer $ADMIN"
```

### What to expect
- **Authorize** holds the exact order total; `payment.authorizationStatus` is `CREATED`, nothing captured.
- **Fulfil** captures and fills in `capturedAmount` / `payPalFee` / `netAmount` (e.g. `32.50 / 1.33 / 31.17`).
- **Refund** idempotency: same key ⇒ identical `refundId` and unchanged `refundableRemaining`; a refund
  beyond the captured amount is rejected (HTTP 400).
- **Cancel** voids the hold (status `Cancelled`); a fulfilled order can't be cancelled (use a refund).
- **Operator actions** (`fulfil`, `cancel`, `reconciliation`) require the administrator role — a shopper
  token gets HTTP 403. Every other endpoint is scoped to the caller (another shopper's order/card ⇒ 404).
- **Reconciliation** pages through the *whole* range. It lists PayPal-only transactions and eShop-only
  captures/refunds. **PayPal's reporting lags**, so transactions you just created may be absent (they show
  up under `eShopOnly` until PayPal's report catches up) — an empty recent range is expected, not a bug.

## Notes
- If PayPal ever answers a card with a browser-approval challenge (3-D Secure), the API returns HTTP 409
  `PAYER_ACTION_REQUIRED` rather than building an approval round-trip (per the task's STOP rule).
- `paypal-plan.md` (repo root) is the SDK contract sheet the integration was built against.
- An EF migration (`AddPaymentsAndSavedCards`) is included for the SQL Server path; the in-memory provider
  used here ignores migrations.
