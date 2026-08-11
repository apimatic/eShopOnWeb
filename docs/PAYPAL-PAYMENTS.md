# PayPal payments & saved cards (eShopOnWeb PublicApi)

This adds real money movement to eShopOnWeb as an **additive** capability on `src/PublicApi`:
a shopper places an order, **authorizes** (holds) the total by card, an operator **fulfils**
(captures) it, and it can be **cancelled** (void) before fulfilment or **refunded** (full/part)
after. A shopper can also **vault a card** and reuse it for a later order. PayPal is the
processor, driven entirely through the **paypal-sdk** plugin (`AsadAli.Checkout.Sdk`).

## What was added

| Endpoint | Who | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog items (reuses the existing `Order` model). Returns **`orderId`**. Starts *AwaitingPayment*. |
| `POST /api/orders/{orderId}/pay` | shopper | Authorize the total — hold, don't take. Body carries card details **or** `savedPaymentMethodId`. |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Capture the money. Shows captured amount, PayPal fee and net proceeds. Renews a stale hold before capturing. |
| `POST /api/orders/{orderId}/cancel` | **admin** | Void the hold before fulfilment — no money moves. |
| `POST /api/orders/{orderId}/refunds` | shopper | Refund a capture, full or partial. Returns **`refundId`**. Carries a caller `idempotencyKey`. |
| `GET /api/my-orders` | shopper | The caller's own orders with payment state. |
| `GET /api/reconciliation?from=&to=` | **admin** | PayPal's transaction report (all pages) lined up against eShop payments. |
| `POST /api/payment-methods` | shopper | Vault a card. Returns **`paymentMethodId`** + a safe descriptor (brand, last 4, expiry). |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card (also deletes the vault token). |

Design notes:
- The existing `Order`/`OrderItem` aggregate is reused unchanged. A new **`Payment`** aggregate
  (1:1 with an order) holds all PayPal-owned state — the hold (`AuthorizationId`), the capture
  (`CaptureId`, captured amount, fee, net) and the `Refunds` — so any later request can act on it.
  Saved cards are a new **`SavedPaymentMethod`** aggregate.
- **Amounts** come from catalog prices; **currency** from `PayPal:Currency`. The held amount
  equals the order total to the cent.
- **Idempotency**: pay/fulfil pass a stable `PayPal-Request-Id` and are guarded in-app, so a
  double-click never authorizes/captures twice. Refunds are keyed by the caller's
  `idempotencyKey`; the same key never refunds twice, while two distinct partial refunds are
  allowed and can never exceed the captured amount.
- **Ownership**: every shopper endpoint acts only on the caller's own data (a missing *or*
  another user's resource both return 404). Fulfil/cancel/reconciliation require the
  `Administrators` role.
- **PCI**: full card details flow straight to PayPal and are **never** stored in the app database
  or written to logs. Only the vault token + brand/last-4/expiry are kept.

## Prerequisites (this machine)

- .NET 10 SDK present; `global.json` is set to `rollForward: latestMajor`. Run with
  `DOTNET_ROLL_FORWARD=Major`. (ASP.NET Core 8 runtime is installed here.)
- The PayPal sandbox credentials are read from the environment and loaded into **user-secrets**
  under the `PayPal:` section (their values never enter the repo). To (re)load them:

  ```bash
  cd src/PublicApi
  for kv in "PayPal:ClientId=PAYPAL_CLIENT_ID" "PayPal:ClientSecret=PAYPAL_CLIENT_SECRET" \
            "PayPal:Environment=PAYPAL_ENVIRONMENT" "PayPal:Currency=PAYPAL_CURRENCY"; do
    dotnet user-secrets set "${kv%%=*}" "$(printenv ${kv##*=})" >/dev/null
  done
  ```
  Configuration under `PayPal:*` wins; if unset, the app also falls back to the `PAYPAL_*`
  environment variables. `PayPal:BaseUrl` is an optional verbatim override used for **every**
  PayPal call (including the OAuth token request); leave it unset to target the sandbox.

## Run it

In-memory database (no LocalDB needed). Bind only to your port block (9083/9084 here):

```bash
DOTNET_ROLL_FORWARD=Major UseOnlyInMemoryDatabase=true \
  dotnet run --project src/PublicApi/PublicApi.csproj
```
Swagger: <https://localhost:9083/swagger>. (In-memory data lives only for one run — pay, fulfil
and refund the orders you create within the same run.)

## Verify end-to-end (automated)

With the app running:

```bash
python scripts/verify_paypal.py
```
It drives the whole surface against the sandbox test card and prints PASS/FAIL for each rule
(authorize→fulfil→partial refund, pay/refund idempotency, over-refund rejection, cancel/void,
save-card → reuse on a second order → delete → can-no-longer-pay, ownership isolation,
my-orders, reconciliation). Expected result: **ALL CHECKS PASSED**.

## Verify end-to-end (manual, curl)

`-k` accepts the local dev cert. Sandbox test card: Visa `4111 1111 1111 1111`, any future expiry.

```bash
B=https://localhost:9083/api
SHOP=$(curl -sk -X POST $B/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
ADMIN=$(curl -sk -X POST $B/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")

# 1) Place an order (returns orderId). $12 + $12 + $8.50 = $32.50
curl -sk -X POST $B/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":3,"quantity":2},{"catalogItemId":5,"quantity":1}]}'

# 2) Authorize (hold) with the test card  — use the orderId from step 1
curl -sk -X POST $B/orders/1/pay -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"card":{"number":"4111111111111111","expiryMonth":"12","expiryYear":"2030","securityCode":"123","cardholderName":"Test Shopper","billingLine1":"1 Market St","billingCity":"San Jose","billingState":"CA","billingPostalCode":"95131","billingCountryCode":"US"}}'

# 3) Fulfil (capture) — operator; response shows capturedAmount, payPalFee, netAmount
curl -sk -X POST $B/orders/1/fulfil -H "Authorization: Bearer $ADMIN"

# 4) Partial refund (returns refundId); repeat with same idempotencyKey → no double refund
curl -sk -X POST $B/orders/1/refunds -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"amount":10.00,"idempotencyKey":"refund-1"}'

# 5) Save a card (returns paymentMethodId + safe descriptor)
curl -sk -X POST $B/payment-methods -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"card":{"number":"4111111111111111","expiryMonth":"12","expiryYear":"2030","securityCode":"123","cardholderName":"Test Shopper"},"label":"my visa"}'

# 6) New order paid with the SAVED card (reuse) — put the paymentMethodId from step 5
curl -sk -X POST $B/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d '{"items":[{"catalogItemId":5,"quantity":4}]}'
curl -sk -X POST $B/orders/2/pay -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d '{"savedPaymentMethodId":1}'
curl -sk -X POST $B/orders/2/fulfil -H "Authorization: Bearer $ADMIN"

# 7) Cancel-before-fulfilment (void) on a fresh authorized order
curl -sk -X POST $B/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d '{"items":[{"catalogItemId":4,"quantity":1}]}'
curl -sk -X POST $B/orders/3/pay -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d '{"card":{"number":"4111111111111111","expiryMonth":"12","expiryYear":"2030","securityCode":"123"}}'
curl -sk -X POST $B/orders/3/cancel -H "Authorization: Bearer $ADMIN"

# 8) Views
curl -sk $B/my-orders -H "Authorization: Bearer $SHOP"
curl -sk "$B/reconciliation?from=2026-08-01T00:00:00Z&to=2026-08-31T23:59:59Z" -H "Authorization: Bearer $ADMIN"

# 9) Delete the saved card — afterwards it is gone and can no longer pay
curl -sk -X DELETE $B/payment-methods/1 -H "Authorization: Bearer $SHOP" -o /dev/null -w '%{http_code}\n'  # 204
curl -sk $B/payment-methods -H "Authorization: Bearer $SHOP"
```

### Reconciliation and the sandbox lag

PayPal's transaction reporting lags live activity, so a range covering payments you just created
may come back with your orders under `eShopOnly` and no `Matched` rows yet — that is the expected
sandbox result, not a bug. The report pages through the **whole** range (it lists hundreds of the
account's historical transactions), and matching by transaction/capture id lines both sides up
once PayPal's report catches up. Use a range that already has settled data to see matches.

## Tests

Service-layer rules (ownership, pay/refund idempotency, refund bounds, stale-hold renewal, saved
-card ownership, no-PAN storage) are covered by unit tests:

```bash
DOTNET_ROLL_FORWARD=Major dotnet test tests/UnitTests/UnitTests.csproj
```
