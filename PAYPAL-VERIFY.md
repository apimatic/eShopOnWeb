# PayPal payments — how to verify

This adds PayPal card payments and saved cards to eShopOnWeb as **additive** HTTP endpoints on
`src/PublicApi` (JWT auth). It does not change the existing catalog/basket/order flow.

## Endpoints (all under `/api`)

| Method & route | Who | What |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog items → returns top-level `orderId`. Starts *AwaitingPayment*. |
| `POST /api/orders/{orderId}/pay` | shopper | **Authorize** (hold) the order total; body carries `card` **or** `savedPaymentMethodId`. |
| `POST /api/orders/{orderId}/fulfil` | **admin** | **Capture** the hold; records captured amount, PayPal fee, net. Renews a stale hold first. |
| `POST /api/orders/{orderId}/cancel` | **admin** | **Void** the hold before fulfilment (no money moved). |
| `POST /api/orders/{orderId}/refunds` | shopper | **Refund** a capture (full/partial); body `amount?` + `idempotencyKey` → returns top-level `refundId`. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from={iso}&to={iso}` | **admin** | PayPal's transactions for a range vs eShop orders (paged over the whole range). |
| `POST /api/payment-methods` | shopper | Save (vault) a card → returns top-level `paymentMethodId` + safe descriptor (brand/last4/expiry). |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card. |

Shopper endpoints act only on the caller's own data (others → 404). Fulfil/cancel/reconciliation
require the Administrators role (others → 403).

## Prerequisites (this machine)

- Credentials are already loaded into **.NET user-secrets** for `src/PublicApi` under keys
  `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency` (optional
  `PayPal:BaseUrl`). No secret values live in the repo. To reload them from the environment:
  ```bash
  cd src/PublicApi
  dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
  dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
  dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
  dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
  ```
- Only the .NET 10 SDK is installed and there is no LocalDB, so run with roll-forward + in-memory.

## 1. Run PublicApi

```bash
export DOTNET_ROLL_FORWARD=Major
export UseOnlyInMemoryDatabase=true
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="https://localhost:9843;http://localhost:9844"
cd src/PublicApi
dotnet run
```
Swagger: <https://localhost:9843/swagger>. (Dev cert is trusted; `curl -k` also works.)

> In-memory data is per-process and lost on restart, and Web/PublicApi have separate stores — so
> place, pay, fulfil and refund the orders you create **within the same PublicApi run**.

## 2. Drive the flows

A ready-made script exercises every flow end to end against the sandbox card
`4111 1111 1111 1111`:

```bash
bash tools/verify-flows.sh
```

It: authenticates a shopper (`demouser@microsoft.com`) and an admin (`admin@microsoft.com`, both
password `Pass@word1`); places an order and **pays** it (hold equal to the order total); shows the
**double-click is idempotent**; **fulfils** it (capture — you'll see captured amount, PayPal fee,
net); does a **partial refund** and proves the **same idempotency key doesn't refund twice**; runs
a **pay → cancel (void)**; **saves a card, reuses it to pay a second order, then deletes it**; lists
**my-orders**; and runs **reconciliation**.

### Minimal manual walk-through

```bash
B=https://localhost:9843
TOK=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
ADM=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")

# place
curl -sk -X POST $B/api/orders -H "Authorization: Bearer $TOK" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":1,"quantity":2}]}'
# pay (authorize/hold) — use the orderId returned above
curl -sk -X POST $B/api/orders/1/pay -H "Authorization: Bearer $TOK" -H "Content-Type: application/json" \
  -d '{"card":{"number":"4111111111111111","expiryMonth":12,"expiryYear":2030,"securityCode":"123","billingCountryCode":"US"}}'
# fulfil (capture) — admin
curl -sk -X POST $B/api/orders/1/fulfil -H "Authorization: Bearer $ADM"
# partial refund — shopper
curl -sk -X POST $B/api/orders/1/refunds -H "Authorization: Bearer $TOK" -H "Content-Type: application/json" \
  -d '{"amount":10.00,"idempotencyKey":"ref-1"}'
# reconciliation — admin
curl -sk "$B/api/reconciliation?from=2026-08-01T00:00:00Z&to=2026-08-31T23:59:59Z" -H "Authorization: Bearer $ADM"
```

## Notes

- **Reconciliation lag:** PayPal's transaction reporting lags live activity, so payments you just
  created may show as *eShop-only* (or the range may be empty). Reconcile over an older range to see
  matched rows — this is expected sandbox behaviour, not a missing capability.
- **Stale authorizations** (renew-before-capture) can't be forced in a quick test — a hold stays
  honoured for days. The fulfil path checks the hold, reauthorizes a stale one, and reports one that
  can no longer be renewed (HTTP 409 with an operator-actionable message).
- Full card details are never stored in this app's database and never logged.
