# PayPal payments & saved cards (PublicApi)

This adds real money movement to eShopOnWeb as an **additive** capability on the
`src/PublicApi` project. A shopper places an order and pays by card (PayPal **authorization** =
hold); an operator fulfils it (**capture** = take), cancels it (**void** = release) or the
shopper refunds it after fulfilment. A shopper can also **save a card** once (PayPal vault) and
reuse it for later orders. PayPal is driven entirely through the OpenAPI specifications in
[`api-specs/paypal`](../api-specs/paypal) — no PayPal SDK is used; the client in
`src/Infrastructure/PayPal` is hand-written against those specs.

## Endpoints

All are under `/api/` on PublicApi and require a JWT bearer token. The caller's identity comes
from the token.

| Method & route | Who | What |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog items. Returns `orderId`. Starts awaiting payment. |
| `POST /api/orders/{orderId}/pay` | shopper | Authorize (hold) the order total with card details **or** a saved card (`paymentMethodId`). |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Capture (take) the money. Renews a stale hold before capturing. |
| `POST /api/orders/{orderId}/cancel` | **admin** | Void the hold before fulfilment; no money moves. |
| `POST /api/orders/{orderId}/refunds` | shopper | Refund a fulfilled order, full or partial. Returns `refundId`. Idempotent by key. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from=&to=` | **admin** | PayPal's transactions for a date range lined up against eShop orders. |
| `POST /api/payment-methods` | shopper | Save (vault) a card. Returns `paymentMethodId` and a safe description. |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card. |

Notes:
- The amount PayPal holds equals the order total to the cent.
- Payment operations are idempotent in effect: a double-click never authorizes or captures twice.
- Refunds carry a caller-supplied `idempotencyKey`; repeating the same key does not refund twice,
  while two distinct partial refunds (two keys) are both honoured, never exceeding the captured amount.
- Full card details are never stored in the app database and never logged; only brand / last-4 / expiry.
- Fulfil, cancel and reconciliation are administrator-only; every other endpoint acts only on the
  caller's own data.

## Configuration

Settings bind from the `PayPal:` configuration section (no values are hard-coded):

| Key | From env var |
| --- | --- |
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` (`sandbox` / `live`) |
| `PayPal:Currency` | `PAYPAL_CURRENCY` |
| `PayPal:BaseUrl` | optional override; when set it is used verbatim for every PayPal call including the token request |

Load the sandbox credentials into .NET user-secrets (values never go into the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

## Run it (this machine)

Only the .NET 10 SDK is present and the app targets net8.0, so let it roll forward. The default
DB is LocalDB, which isn't installed, so run in-memory (already set in
`appsettings.Development.json`).

```bash
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="https://localhost:30803;http://localhost:30804"
dotnet run --project src/PublicApi/PublicApi.csproj
```

Swagger UI: <https://localhost:30803/swagger>. The in-memory store is per-process and resets on
restart — place, pay, fulfil and refund within one run.

## Verify end to end (no browser)

Seeded users: shopper `demouser@microsoft.com` and admin `admin@microsoft.com`, password
`Pass@word1`. Sandbox test card: Visa `4111 1111 1111 1111`, any future expiry, any CVC.

```bash
BASE=https://localhost:30803

# 1) Tokens
SHOP=$(curl -sk -X POST $BASE/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)
ADMIN=$(curl -sk -X POST $BASE/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | jq -r .token)

# 2) Place an order -> note orderId
curl -sk -X POST $BASE/api/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":2,"quantity":1}]}'

# 3) Pay (authorize/hold) with the test card
curl -sk -X POST $BASE/api/orders/1/pay -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","name":"Demo Shopper",
        "billingAddress":{"addressLine1":"123 Main St","adminArea2":"Kent","adminArea1":"OH","postalCode":"44240","countryCode":"US"}}}'

# 4) Fulfil (capture/take) as admin -> shows capturedAmount, payPalFee, netAmount
curl -sk -X POST $BASE/api/orders/1/fulfil -H "Authorization: Bearer $ADMIN"

# 5) Partial refund (shopper), idempotency key required
curl -sk -X POST $BASE/api/orders/1/refunds -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"amount":8.50,"idempotencyKey":"refund-1-a"}'

# 6) Saved card: save, then pay a new order with it
PM=$(curl -sk -X POST $BASE/api/payment-methods -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","name":"Demo Shopper"}}' | jq -r .paymentMethodId)
curl -sk -X POST $BASE/api/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":3,"quantity":1}]}'
curl -sk -X POST $BASE/api/orders/2/pay -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d "{\"paymentMethodId\":$PM}"

# 7) Cancel flow (before fulfilment): place, pay, cancel as admin
# 8) Reconciliation (admin) over a range that has data
curl -sk "$BASE/api/reconciliation?from=2026-09-01T00:00:00Z&to=2026-09-30T23:59:59Z" -H "Authorization: Bearer $ADMIN"

# 9) My orders
curl -sk $BASE/api/my-orders -H "Authorization: Bearer $SHOP"
```

An automated end-to-end script exercising every flow and assertion lives at
[`docs/verify-paypal-e2e.py`](verify-paypal-e2e.py) (run with `python docs/verify-paypal-e2e.py`
while the API is running on `https://localhost:30803`). It drives place → pay → fulfil →
partial refund, saved-card save/reuse/delete, cancel, my-orders, reconciliation, idempotency and
cross-shopper isolation, and prints a pass/fail summary (37 checks).

Reconciliation note: PayPal's transaction reporting lags live activity, so a range covering
payments you just created may legitimately come back empty (or list your payments under
`missingInPayPal` until reporting catches up). Use a range that already has data to see matches.
