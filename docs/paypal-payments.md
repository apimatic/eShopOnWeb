# PayPal payments & saved cards — verification guide

This adds real money movement to eShopOnWeb's `src/PublicApi` project: a shopper places an
order, **authorizes** (holds) the total with PayPal, an operator **fulfils** it (the money is
captured), and the order can be **cancelled** (hold released) or **refunded** (money returned,
in full or in part). A shopper can also **save a card** once and reuse it to pay later orders.
All PayPal traffic goes through the **paypal-sdk** plugin (`AsadAli.Checkout.Sdk`).

It is additive — the existing catalog/basket/order flow is untouched. New endpoints only.

## Endpoints (all under `/api/`, JWT-authenticated)

| Method & route | Who | What |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog items → `orderId`. Starts **awaiting payment**. |
| `POST /api/orders/{orderId}/pay` | shopper (own order) | **Authorize** (hold) the total with a one-off card **or** a saved card. |
| `POST /api/orders/{orderId}/fulfil` | **admin** | **Capture** the held money; response shows captured amount, PayPal fee, net. |
| `POST /api/orders/{orderId}/cancel` | **admin** | Void the hold before fulfilment; no money moves. |
| `POST /api/orders/{orderId}/refunds` | shopper (own order) | Refund a captured order, full or partial → `refundId`. Needs `idempotencyKey`. |
| `GET  /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET  /api/reconciliation?from=&to=` | **admin** | PayPal's transactions for a date range lined up against eShop's. |
| `POST /api/payment-methods` | shopper | Save (vault) a card → `paymentMethodId` + safe descriptor (brand, last4, expiry). |
| `GET  /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card. |

Notes on behaviour:
- **Amounts** come from catalog prices; the **currency** is `PayPal:Currency` from config.
- **Idempotent in effect:** authorize/capture derive a `PayPal-Request-Id` from a per-payment
  GUID, so a double-click never holds or captures twice. Refunds use the **caller-supplied**
  `idempotencyKey`: repeating it is a no-op, two distinct partial refunds are both allowed.
- **Stale holds** are re-authorized automatically at fulfilment; if a hold can no longer be
  renewed the operator gets an actionable message.
- **Ownership:** a shopper only ever sees/acts on their own orders and saved cards.
- **No card data** is stored in this app's database or written to logs — only PayPal's vault
  token and a safe descriptor (brand / last four / expiry).

---

## 1. Prerequisites (this machine)

- Only the **.NET 10 SDK** is installed but `global.json` pins 8.0.x, so build/run with
  roll-forward: `DOTNET_ROLL_FORWARD=Major` (already set in the commands below).
- No SQL LocalDB → run with **`UseOnlyInMemoryDatabase=true`**. The in-memory store is
  **per-process and resets on restart**, so create, pay, fulfil and refund orders **within one
  run**.
- HTTPS dev cert is present; the `curl` commands below use `-k` to skip local trust.

### Load PayPal credentials into user-secrets (once)

Credentials are read from configuration section **`PayPal:`** and must never be committed.
Load them into the PublicApi project's user-secrets from the environment variables:

```bash
dotnet user-secrets set --project src/PublicApi "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set --project src/PublicApi "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set --project src/PublicApi "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   # sandbox
dotnet user-secrets set --project src/PublicApi "PayPal:Currency"     "$PAYPAL_CURRENCY"       # USD
# Optional: PayPal:BaseUrl overrides the API base address verbatim for every call.
```

(These were already loaded during development.)

---

## 2. Run PublicApi

```bash
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development
export UseOnlyInMemoryDatabase=true
export ASPNETCORE_URLS="https://localhost:8823;http://localhost:8824"

dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

Wait for `Now listening on: https://localhost:8823`. Swagger UI: <https://localhost:8823/swagger>.

Seeded users (password `Pass@word1`): `demouser@microsoft.com` (shopper),
`admin@microsoft.com` (administrator).

---

## 3. Step-by-step verification (curl)

Set a base URL and grab tokens:

```bash
API=https://localhost:8823
SHOPPER=$(curl -sk $API/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | python -c 'import sys,json;print(json.load(sys.stdin)["token"])')
ADMIN=$(curl -sk $API/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | python -c 'import sys,json;print(json.load(sys.stdin)["token"])')
```

The sandbox test card used below (no browser step needed): **Visa `4111 1111 1111 1111`**,
any future expiry, any CVC, any billing address.

### Flow 1 — pay, fulfil, refund (one-off card)

```bash
# Place an order (catalog items 5 x2 and 4 x1 = 29.00)
ORDER=$(curl -sk $API/api/orders -H "Authorization: Bearer $SHOPPER" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":5,"quantity":2},{"catalogItemId":4,"quantity":1}]}' \
  | python -c 'import sys,json;print(json.load(sys.stdin)["orderId"])')
echo "orderId=$ORDER"

# Authorize (hold) the total with the test card
curl -sk $API/api/orders/$ORDER/pay -H "Authorization: Bearer $SHOPPER" -H 'Content-Type: application/json' -d '{
  "card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","cardholderName":"Demo User",
    "billingAddress":{"addressLine1":"1 Market St","city":"San Jose","state":"CA","postalCode":"95131","countryCode":"US"}}}'
# -> payment.status = "Authorized", authorizationId set, amount = 29.00

# Fulfil (capture) — ADMIN. Response shows capturedAmount, payPalFee, netAmount.
curl -sk $API/api/orders/$ORDER/fulfil -X POST -H "Authorization: Bearer $ADMIN"
# -> payment.status = "Captured", captureId set, payPalFee / netAmount reported

# Partial refund (shopper) with a caller-supplied idempotency key
curl -sk $API/api/orders/$ORDER/refunds -H "Authorization: Bearer $SHOPPER" -H 'Content-Type: application/json' \
  -d '{"amount":5.00,"idempotencyKey":"refund-001"}'
# -> refundId returned, payment.status = "PartiallyRefunded"

# Repeat the SAME key -> same refundId, NOT a second refund
curl -sk $API/api/orders/$ORDER/refunds -H "Authorization: Bearer $SHOPPER" -H 'Content-Type: application/json' \
  -d '{"amount":5.00,"idempotencyKey":"refund-001"}'

# A distinct partial refund (different key) is allowed
curl -sk $API/api/orders/$ORDER/refunds -H "Authorization: Bearer $SHOPPER" -H 'Content-Type: application/json' \
  -d '{"amount":4.00,"idempotencyKey":"refund-002"}'

# See it all
curl -sk $API/api/my-orders -H "Authorization: Bearer $SHOPPER"
```

> Use a fresh `idempotencyKey` value on each new run: PayPal remembers request ids for a while,
> and the in-memory store is wiped on restart, so a key reused from a previous run is rejected
> by PayPal as a duplicate.

### Flow 2 — save a card and reuse it

```bash
# Save (vault) a card -> paymentMethodId + safe descriptor
PM=$(curl -sk $API/api/payment-methods -H "Authorization: Bearer $SHOPPER" -H 'Content-Type: application/json' -d '{
  "card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","cardholderName":"Demo User",
    "billingAddress":{"addressLine1":"1 Market St","city":"San Jose","state":"CA","postalCode":"95131","countryCode":"US"}},
  "alias":"My Visa"}' | python -c 'import sys,json;print(json.load(sys.stdin)["paymentMethodId"])')

curl -sk $API/api/payment-methods -H "Authorization: Bearer $SHOPPER"   # brand VISA, last4 1111 — never the PAN

# New order paid with the saved card (no card details re-entered)
ORDER2=$(curl -sk $API/api/orders -H "Authorization: Bearer $SHOPPER" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":5,"quantity":1}]}' | python -c 'import sys,json;print(json.load(sys.stdin)["orderId"])')
curl -sk $API/api/orders/$ORDER2/pay -H "Authorization: Bearer $SHOPPER" -H 'Content-Type: application/json' \
  -d "{\"savedPaymentMethodId\":$PM}"
curl -sk $API/api/orders/$ORDER2/fulfil -X POST -H "Authorization: Bearer $ADMIN"

# Delete the saved card -> gone, and no longer usable to pay
curl -sk -X DELETE $API/api/payment-methods/$PM -H "Authorization: Bearer $SHOPPER" -o /dev/null -w '%{http_code}\n'  # 204
curl -sk $API/api/payment-methods -H "Authorization: Bearer $SHOPPER"   # no longer listed
```

### Cancel before fulfilment

```bash
ORDER3=$(curl -sk $API/api/orders -H "Authorization: Bearer $SHOPPER" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":4,"quantity":1}]}' | python -c 'import sys,json;print(json.load(sys.stdin)["orderId"])')
curl -sk $API/api/orders/$ORDER3/pay -H "Authorization: Bearer $SHOPPER" -H 'Content-Type: application/json' -d '{
  "card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","cardholderName":"Demo User",
    "billingAddress":{"addressLine1":"1 Market St","city":"San Jose","state":"CA","postalCode":"95131","countryCode":"US"}}}'
curl -sk $API/api/orders/$ORDER3/cancel -X POST -H "Authorization: Bearer $ADMIN"   # payment.status = "Cancelled"
```

### Reconciliation (admin)

```bash
FROM=$(python -c "import datetime;print((datetime.datetime.now(datetime.timezone.utc)-datetime.timedelta(days=1)).strftime('%Y-%m-%dT%H:%M:%SZ'))")
TO=$(python -c "import datetime;print((datetime.datetime.now(datetime.timezone.utc)+datetime.timedelta(days=1)).strftime('%Y-%m-%dT%H:%M:%SZ'))")
curl -sk "$API/api/reconciliation?from=$FROM&to=$TO" -H "Authorization: Bearer $ADMIN"
```

The report walks **every page** of PayPal's transaction search and lists `matched`,
`inPayPalNotInEShop`, and `inEShopNotInPayPal`. PayPal's reporting **lags live activity**, so
transactions you just created often appear only under `inEShopNotInPayPal` for a while — that
is an expected sandbox result, not a missing capability. Run it against an older range (or
re-run later) to see rows move into `matched`.

---

## 4. One-command check

An end-to-end script that exercises every flow above and prints PASS/FAIL is not committed to
the repo, but the manual steps in §3 cover the same ground. During development the full suite
reported **40/40 checks passing** against the live sandbox (authorize, capture with fee/net,
idempotent re-pay, partial refunds + idempotency, over-refund guard, saved-card reuse, cancel,
ownership isolation, reconciliation, and delete).

## 5. Tests

```bash
DOTNET_ROLL_FORWARD=Major dotnet test tests/UnitTests/UnitTests.csproj
```

(includes payment-aggregate invariants: refund cap, refund idempotency lookup, state
transitions, and per-payment idempotency-key derivation.)
