# PayPal payments & saved cards (PublicApi)

This adds **real money movement** to eShopOnWeb using **PayPal** as the processor, exposed as
JWT-authenticated HTTP endpoints on the **`src/PublicApi`** project. It is additive — the existing
catalog/basket/order flow is untouched.

- **Flow 1 — pay for an order:** place an order, **authorize** (hold) the total, **fulfil**
  (capture/take the money), **cancel** (release before fulfilment) or **refund** (after fulfilment).
- **Flow 2 — saved cards:** save a card once (vaulted at PayPal), list them, reuse one to pay a later
  order, and delete one.

The PayPal integration is a hand-written client built directly against the OpenAPI specs in
`api-specs/` (Checkout Orders v2, Payments v2, Vault Payment Tokens v3, Transaction Search v1). No
third-party PayPal SDK is used.

## Endpoints

| Method & route | Role | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog item ids + quantities (starts awaiting payment). Returns `orderId`. |
| `POST /api/orders/{orderId}/pay` | shopper | Authorize (hold) the total via a one-off card **or** a saved card. |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Mark fulfilled and capture; renews a stale hold automatically. |
| `POST /api/orders/{orderId}/cancel` | **admin** | Cancel before fulfilment; releases the hold (void). |
| `POST /api/orders/{orderId}/refunds` | shopper | Full/partial refund under a caller idempotency key. Returns `refundId`. |
| `GET  /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET  /api/reconciliation?from={iso}&to={iso}` | **admin** | PayPal transactions vs eShop orders for a range (all pages). |
| `POST /api/payment-methods` | shopper | Save a card. Returns `paymentMethodId` + safe description. |
| `GET  /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card. |

Shopper-scoped endpoints act only on the caller's own data; one shopper can never see, use or delete
another's order or card. Full card numbers are never stored in this app's database or written to logs.

## Configuration

Settings bind from the `PayPal:` section (never hard-coded):
`PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`, and the optional
`PayPal:BaseUrl` (when set, used verbatim as the API base for every call, including the token request;
otherwise the base is derived from `PayPal:Environment`).

Secrets are read from the environment variables `PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET`,
`PAYPAL_ENVIRONMENT`, `PAYPAL_CURRENCY` and loaded into **.NET user-secrets** for the PublicApi
project — their values are never committed to the repository.

## Prerequisites on this machine

- Only the .NET 10 SDK is installed and `global.json` pins 8.0.x, so `rollForward` is set to
  `latestMajor`; run with `DOTNET_ROLL_FORWARD=Major`. The ASP.NET Core 8.0 runtime is present.
- No SQL Server LocalDB: run with `UseOnlyInMemoryDatabase=true` (already stored in user-secrets).
  The in-memory store is per-process and resets on restart — create, pay, fulfil and refund within a
  single run.
- HTTPS dev cert should be trusted: `dotnet dev-certs https --check` (add `--trust` if needed).

### One-time: load the credentials into user-secrets

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"      "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret"  "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"   "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"      "$PAYPAL_CURRENCY"
dotnet user-secrets set "UseOnlyInMemoryDatabase" "true"
```

## Run

```bash
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="https://localhost:9643;http://localhost:9644"
dotnet run --project src/PublicApi --no-launch-profile
```

Swagger UI: <https://localhost:9643/swagger>.

## Verify it yourself (curl)

Seeded users (password `Pass@word1`): shopper `demouser@microsoft.com`, admin `admin@microsoft.com`.
Sandbox test card: Visa `4111 1111 1111 1111`, any future expiry, any CVC.

```bash
BASE=https://localhost:9643/api
tok() { python -c "import sys,json;print(json.load(sys.stdin)['token'])"; }

# 1) Tokens
SHOP=$(curl -sk -X POST "$BASE/authenticate"  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | tok)
ADMIN=$(curl -sk -X POST "$BASE/authenticate" -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | tok)

# 2) Place an order (2x item 5 + 1x item 4)  -> note the orderId
curl -sk -X POST "$BASE/orders" -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":5,"quantity":2},{"catalogItemId":4,"quantity":1}]}'

# 3) Pay / authorize (hold) with the sandbox card  (replace 1 with your orderId)
curl -sk -X POST "$BASE/orders/1/pay" -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","name":"Demo User",
       "billingAddress":{"addressLine1":"123 Main St","adminArea2":"Kent","adminArea1":"OH","postalCode":"44240","countryCode":"US"}}}'

# 4) Fulfil / capture (admin) — shows captured amount, PayPal fee and net proceeds
curl -sk -X POST "$BASE/orders/1/fulfil" -H "Authorization: Bearer $ADMIN"

# 5) Partial refund (shopper) under an idempotency key; repeat with the same key = no double refund
curl -sk -X POST "$BASE/orders/1/refunds" -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"amount":10.00,"idempotencyKey":"demo-refund-1"}'

# 6) See your orders with payment state
curl -sk "$BASE/my-orders" -H "Authorization: Bearer $SHOP"
```

### Saved cards (Flow 2)

```bash
# Save a card -> note paymentMethodId
curl -sk -X POST "$BASE/payment-methods" -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","name":"Demo User","alias":"My Visa",
       "billingAddress":{"addressLine1":"123 Main St","adminArea2":"Kent","adminArea1":"OH","postalCode":"44240","countryCode":"US"}}'

curl -sk "$BASE/payment-methods" -H "Authorization: Bearer $SHOP"          # list

# Place a second order, then pay it with the saved card (replace ids)
curl -sk -X POST "$BASE/orders" -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":3,"quantity":1}]}'
curl -sk -X POST "$BASE/orders/2/pay" -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"savedPaymentMethodId":1}'

curl -sk -X DELETE "$BASE/payment-methods/1" -H "Authorization: Bearer $SHOP"   # remove (then it can't pay)
```

### Cancel (instead of fulfil)

```bash
curl -sk -X POST "$BASE/orders/3/cancel" -H "Authorization: Bearer $ADMIN"   # releases the hold; no money moved
```

### Reconciliation (admin)

```bash
curl -sk -G "$BASE/reconciliation" -H "Authorization: Bearer $ADMIN" \
  --data-urlencode "from=2026-08-01T00:00:00Z" --data-urlencode "to=2026-08-16T23:59:59Z"
```

The report buckets PayPal transactions vs eShop orders into `matched`, `inPayPalOnly` and
`inEShopOnly`, covering the whole range (it pages through every page, and splits ranges longer than
PayPal's 31-day per-request limit into windows). **Note:** PayPal's transaction reporting lags live
activity, so a range covering payments you have *just* created may legitimately come back with those
orders in `inEShopOnly` (or the range empty) — this is expected sandbox behaviour, not a defect.

## Notes on correctness

- **Amounts** come from catalog prices; the held amount equals the order total to the cent; the
  currency comes from `PayPal:Currency`.
- **Idempotency:** pay and fulfil are effect-idempotent (a double-click never authorizes or captures
  twice); refunds de-duplicate on the caller-supplied idempotency key, while two *distinct* partial
  refunds remain separate. A partly-refunded order never becomes refundable beyond what was captured.
- **Stale holds:** if an authorization has expired by fulfilment time it is renewed (reauthorized)
  and then captured; if it can no longer be renewed, the error tells the operator to ask the shopper
  to pay again.
- **Challenges:** if PayPal answered a card with a browser-approval challenge, the pay call fails with
  a clear message rather than attempting an approval round-trip (none occurred with the sandbox card).
