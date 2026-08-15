# PayPal payments & saved cards (PublicApi)

This adds real money movement to eShopOnWeb via **PayPal** (direct-card / server-side, no browser
step) plus **saved cards**. It is additive — the existing catalog/basket/order flow is untouched.

Everything is driven through the **`src/PublicApi`** JWT API. All PayPal calls go through the
`paypal-sdk` plugin's .NET SDK (`AsadAli.Checkout.Sdk`).

## Endpoints

| Method & route | Role | Purpose |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog items → `{ orderId }`. Starts `AwaitingPayment`. |
| `POST /api/orders/{orderId}/pay` | shopper | **Authorize** (hold) the order total, by raw card **or** a saved card. |
| `POST /api/orders/{orderId}/fulfil` | admin | Mark fulfilled → **capture** the funds; records captured amount, PayPal fee, net. |
| `POST /api/orders/{orderId}/cancel` | admin | Cancel before fulfilment → **void** the hold. |
| `POST /api/orders/{orderId}/refunds` | shopper | **Refund** (full/partial) → `{ refundId }`. Carries an idempotency key. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from=&to=` | admin | PayPal's transactions for a range vs eShop orders (ISO-8601 date-times). |
| `POST /api/payment-methods` | shopper | Save (vault) a card → `{ paymentMethodId }` + safe descriptor. |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card. |

Shopper endpoints act only on the caller's own data (identity from the JWT). Full card details are
never stored in the app database and never logged; only PayPal's vault token + a safe descriptor
(brand, last-4, expiry) are kept.

## Configuration

Settings bind from the `PayPal:` section (no values are hard-coded):

| Key | From env var |
| --- | --- |
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` |
| `PayPal:Currency` | `PAYPAL_CURRENCY` |
| `PayPal:BaseUrl` | *(optional override; used verbatim for every call incl. the OAuth token request)* |

Load the secrets into **.NET user-secrets** for the `PublicApi` project (they never go into the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

## Running (this machine)

The SDK is pinned to 8.0.x but only .NET 10 is installed, and there is no LocalDB — so roll the SDK
forward and use the in-memory database (already the default in `appsettings.Development.json`):

```bash
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="https://localhost:9223;http://localhost:9224"
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

> In-memory data is per-process and lost on restart, and Web/PublicApi hold separate stores — so
> place, pay, fulfil and refund an order within the **same** PublicApi run (that is why
> `POST /api/orders` is part of the API).

## Verify it end-to-end (no browser)

Uses PayPal's sandbox test card `4111 1111 1111 1111`, any future expiry / CVC / name.

```bash
API=https://localhost:9223/api
tok(){ curl -sk -X POST "$API/authenticate" -H "Content-Type: application/json" \
  -d "{\"username\":\"$1\",\"password\":\"Pass@word1\"}" | python -c "import sys,json;print(json.load(sys.stdin)['token'])"; }
T=$(tok demouser@microsoft.com)     # shopper
ADM=$(tok admin@microsoft.com)      # operator/admin
CARD='{"number":"4111111111111111","expiryMonth":"09","expiryYear":"2030","securityCode":"123","cardholderName":"Demo Shopper","billingAddress":{"street":"1 Microsoft Way","city":"Redmond","state":"WA","country":"US","zipCode":"98052"}}'

# 1) place an order (total 47.50)
OID=$(curl -sk -X POST "$API/orders" -H "Authorization: Bearer $T" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":2,"quantity":1}]}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")

# 2) authorize (places a hold; no money taken)
curl -sk -X POST "$API/orders/$OID/pay" -H "Authorization: Bearer $T" -H "Content-Type: application/json" -d "{\"card\":$CARD}"

# 3) fulfil = capture (money taken; response shows capturedAmount, payPalFee, netAmount)
curl -sk -X POST "$API/orders/$OID/fulfil" -H "Authorization: Bearer $ADM"

# 4) refund part of it (repeat with same idempotencyKey -> no double refund)
curl -sk -X POST "$API/orders/$OID/refunds" -H "Authorization: Bearer $T" -H "Content-Type: application/json" \
  -d '{"amount":12.50,"idempotencyKey":"RF-1"}'

# 5) save a card, reuse it to pay a second order
PMID=$(curl -sk -X POST "$API/payment-methods" -H "Authorization: Bearer $T" -H "Content-Type: application/json" \
  -d "{\"card\":$CARD,\"label\":\"my visa\"}" | python -c "import sys,json;print(json.load(sys.stdin)['paymentMethodId'])")
OID2=$(curl -sk -X POST "$API/orders" -H "Authorization: Bearer $T" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":3,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
curl -sk -X POST "$API/orders/$OID2/pay" -H "Authorization: Bearer $T" -H "Content-Type: application/json" -d "{\"savedPaymentMethodId\":$PMID}"
curl -sk -X POST "$API/orders/$OID2/fulfil" -H "Authorization: Bearer $ADM"

# 6) list your orders and saved cards
curl -sk "$API/my-orders" -H "Authorization: Bearer $T"
curl -sk "$API/payment-methods" -H "Authorization: Bearer $T"

# 7) operator reconciliation (empty for a range you just created — reporting lags; correct over older ranges)
curl -sk "$API/reconciliation?from=2026-07-01T00:00:00Z&to=2026-08-15T00:00:00Z" -H "Authorization: Bearer $ADM"

# cancel (void) — do this on an order you authorized but have NOT fulfilled:
# curl -sk -X POST "$API/orders/$OID3/cancel" -H "Authorization: Bearer $ADM"
```

You can also drive it from Swagger UI at `https://localhost:9223/swagger` (click **Authorize**, paste
`Bearer <token>` from `/api/authenticate`).

## Design notes

- **Domain**: an additive `Payment` aggregate (holds PayPal's ids/status for the authorization,
  capture and refunds), a `SavedPaymentMethod` aggregate (vault token + safe descriptor), and an
  additive `Order.Status` lifecycle. The existing `Order`/`OrderItem` model is reused.
- **Boundary**: `IPayPalPaymentGateway` (ApplicationCore) is the only seam; `PayPalPaymentGateway`
  (Infrastructure) is the sole place the SDK is touched, translating SDK errors into caller-safe,
  status-coded failures.
- **Idempotency**: pay/capture use stable `PayPal-Request-Id`s plus a per-order in-process lock and an
  existing-payment check, so a double-click never authorizes/captures twice; refunds dedupe on the
  caller's idempotency key. Refunds can never exceed the captured amount.
- **Stale holds**: fulfilment renews an expired authorization (reauthorize) before capturing; one that
  can no longer be honored surfaces as a `409` an operator can act on.

## Known limitation (SDK)

The bundled SDK exposes only a **Sandbox** `ServerEnvironment`. To target a different PayPal account
(including live), set `PayPal:BaseUrl` to that host — it is used verbatim for every call, including the
OAuth token request. There is no separate "Production" toggle.

For a SQL Server deployment (not this machine's setup), add an EF Core migration for the new `Payment`,
`PaymentRefund`, `SavedPaymentMethod` tables and the `Order.Status` column; the in-memory provider used
here ignores migrations.
