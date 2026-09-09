# PayPal payments & saved cards (PublicApi)

This adds real money movement to eShopOnWeb, with **PayPal** as the processor, exposed as
JWT-authenticated HTTP endpoints on **`src/PublicApi`**. It is additive — the existing
catalog/basket/order flow is untouched. A shopper places an order, holds the money at checkout
(authorize), an operator fulfils it (capture), and money is returned on cancel (void) or refund. A
shopper can also save a card once (PayPal Vault) and reuse it to pay a later order.

## Endpoints

| Method & route | Who | What |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog items. Starts *AwaitingPayment*. Returns `orderId`. |
| `POST /api/orders/{orderId}/pay` | shopper (own) | **Authorize** the total (hold, don't take). Card details **or** a saved card id. |
| `POST /api/orders/{orderId}/fulfil` | **admin** | **Capture** the hold. Records captured amount, PayPal fee, net. Renews a stale hold. |
| `POST /api/orders/{orderId}/cancel` | **admin** | **Void** the hold before fulfilment. No money moved. |
| `POST /api/orders/{orderId}/refunds` | shopper (own) | Refund a capture, full or partial. Idempotency key. Returns `refundId`. |
| `GET /api/my-orders` | shopper | The caller's orders + payment state. |
| `GET /api/reconciliation?from=&to=` | **admin** | PayPal's transactions for a date range lined up against eShop orders. |
| `POST /api/payment-methods` | shopper | Save (vault) a card. Returns `paymentMethodId` + safe descriptors. |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper (own) | Remove a saved card (PayPal vault + local). |

Admin endpoints require the existing **Administrators** role; every other endpoint is scoped to the
caller's own data (one shopper never sees/uses/acts on another's orders or cards).

## How it maps to PayPal

- **Authorize:** `POST /v2/checkout/orders` with `intent=AUTHORIZE` and an inline card payment source
  (raw card, or `card.vault_id` for a saved card). The card is authorized in one call — the
  authorization is returned under `purchase_units[0].payments.authorizations[0]`.
- **Fulfil:** `POST /v2/payments/authorizations/{id}/capture` (`seller_receivable_breakdown` →
  gross / paypal_fee / net). A stale hold is renewed via `.../reauthorize` first.
- **Cancel:** `POST /v2/payments/authorizations/{id}/void`.
- **Refund:** `POST /v2/payments/captures/{captureId}/refund` (full = empty amount; partial = amount).
- **Save card:** Vault v3 — `POST /v3/vault/setup-tokens` (card) → `POST /v3/vault/payment-tokens`
  (durable token). **Delete:** `DELETE /v3/vault/payment-tokens/{id}`.
- **Reconciliation:** Transaction Search v1 — `GET /v1/reporting/transactions`, chunked into ≤31-day
  windows and fully paged, matched to eShop orders by the merchant `invoice_id` eShop sends.

Key design points: payment operations are **idempotent in effect** (an already-paid order is not
re-authorized; an already-captured order is not re-captured; a refund key is never applied twice).
Because the PayPal **sandbox does not enforce over-refund limits**, the app enforces
"refund ≤ captured − already-refunded" itself. Raw card details are **never** stored in the app
database or written to logs — only PayPal's ids/tokens and safe descriptors (brand, last four).

## Configuration & secrets

Settings bind from the **`PayPal:`** section (no values are hard-coded):
`PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`,
`PayPal:BaseUrl` (optional — when set, used verbatim for every call incl. the token request).

Load the sandbox credentials from the environment into **.NET user-secrets** (values never enter the
repo). From `src/PublicApi`:

```bash
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   # sandbox
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      # USD
```

## Run it (this machine)

`global.json` pins the SDK to 8.0.x but only the .NET 10 SDK is installed and the ASP.NET 8.0
runtime is missing, so roll forward. There is no LocalDB, so use the in-memory database. From the
repo root:

```bash
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development       # loads user-secrets
export UseOnlyInMemoryDatabase=true
export ASPNETCORE_URLS="https://localhost:30123;http://localhost:30124"
dotnet run --project src/PublicApi --no-launch-profile
```

Swagger: `https://localhost:30123/swagger`. In-memory data lives only for one run — pay/fulfil/refund
the orders you create **in the same run**. Use `-k`/`--insecure` with curl for the dev cert.

## Verify end to end (curl)

```bash
B=https://localhost:30123
CARD='{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","name":"John Doe","billingAddress":{"countryCode":"US","addressLine1":"1 Main St","adminArea2":"San Jose","adminArea1":"CA","postalCode":"95131"}}'

# 1) Tokens (seeded users, password Pass@word1)
DEMO=$(curl -sk -X POST $B/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)
ADMIN=$(curl -sk -X POST $B/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | jq -r .token)

# 2) Place an order (catalog item 5 x2 = 17.00) -> orderId
OID=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $DEMO" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":5,"quantity":2}]}' | jq -r .orderId)

# 3) Pay (authorize / hold). Double-clicking returns the same authorization.
curl -sk -X POST $B/api/orders/$OID/pay -H "Authorization: Bearer $DEMO" -H 'Content-Type: application/json' \
  -d "{\"card\":$CARD}" | jq '.status, .payment.status, .payment.authorizationId, .payment.amount'

# 4) See it
curl -sk $B/api/my-orders -H "Authorization: Bearer $DEMO" | jq '.orders[] | {orderId,status,pay:.payment.status}'

# 5) Fulfil (admin) -> capture; shows captured/fee/net
curl -sk -X POST $B/api/orders/$OID/fulfil -H "Authorization: Bearer $ADMIN" \
  | jq '.status, .payment.captureId, .payment.capturedAmount, .payment.payPalFee, .payment.netAmount'

# 6) Refund: partial, idempotent repeat (same refundId), a second distinct partial, over-refund (409)
curl -sk -X POST $B/api/orders/$OID/refunds -H "Authorization: Bearer $DEMO" -H 'Content-Type: application/json' \
  -d '{"amount":5.00,"idempotencyKey":"r1"}' | jq '.refundId, .order.payment.refundableRemaining'
curl -sk -X POST $B/api/orders/$OID/refunds -H "Authorization: Bearer $DEMO" -H 'Content-Type: application/json' \
  -d '{"amount":5.00,"idempotencyKey":"r1"}' | jq '.refundId'          # same id, no double
curl -sk -X POST $B/api/orders/$OID/refunds -H "Authorization: Bearer $DEMO" -H 'Content-Type: application/json' \
  -d '{"amount":100,"idempotencyKey":"r2"}' -w '\n%{http_code}\n'      # 409, over-refund blocked

# 7) Cancel path on a fresh order: place + pay, then admin cancel (void)
O2=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $DEMO" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":3,"quantity":1}]}' | jq -r .orderId)
curl -sk -X POST $B/api/orders/$O2/pay -H "Authorization: Bearer $DEMO" -H 'Content-Type: application/json' -d "{\"card\":$CARD}" >/dev/null
curl -sk -X POST $B/api/orders/$O2/cancel -H "Authorization: Bearer $ADMIN" | jq '.status, .payment.status'

# 8) Saved cards: save -> list -> pay a NEW order with it -> delete -> unusable
PMID=$(curl -sk -X POST $B/api/payment-methods -H "Authorization: Bearer $DEMO" -H 'Content-Type: application/json' \
  -d "{\"card\":$CARD,\"alias\":\"my visa\"}" | jq -r .paymentMethodId)
curl -sk $B/api/payment-methods -H "Authorization: Bearer $DEMO" | jq '.paymentMethods'
O3=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $DEMO" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":4,"quantity":1}]}' | jq -r .orderId)
curl -sk -X POST $B/api/orders/$O3/pay -H "Authorization: Bearer $DEMO" -H 'Content-Type: application/json' \
  -d "{\"savedPaymentMethodId\":$PMID}" | jq '.payment.status, .payment.sourceType, .payment.cardLast4'
curl -sk -X DELETE $B/api/payment-methods/$PMID -H "Authorization: Bearer $DEMO" -w '%{http_code}\n'   # 204
curl -sk $B/api/payment-methods -H "Authorization: Bearer $DEMO" | jq '.paymentMethods | length'       # 0

# 9) Reconciliation (admin), ISO-8601 date-times
curl -sk "$B/api/reconciliation?from=2026-09-01T00:00:00Z&to=2026-09-10T00:00:00Z" \
  -H "Authorization: Bearer $ADMIN" | jq '{matchedCount,inPayPalNotInEShopCount,inEShopNotInPayPalCount}'
```

Authorization/ownership can be spot-checked: a non-admin calling `/fulfil`, `/cancel` or
`/reconciliation` gets **403**; a shopper acting on another shopper's order gets **404**; using
another shopper's saved card gets **400/404**.

## Sandbox notes (not gaps)

- **Reporting lag:** PayPal's transaction report lags live activity by up to ~3 hours, so a
  reconciliation range covering payments you just made can legitimately come back with those on the
  eShop side only. The report is correct over ranges that already have data.
- **Stale-authorization renewal** at fulfilment is implemented (reauthorize, then capture; an
  actionable error if it can no longer be renewed), but a card hold's 3-day honor period means this
  branch cannot be exercised within a single sandbox run.
- Verified with PayPal's sandbox test card **Visa 4111 1111 1111 1111** (any future expiry, any CVC).
