# PayPal payments & saved cards (PublicApi)

This adds real money movement to eShopOnWeb as an **additive** capability on the JWT-authenticated
`src/PublicApi` project. It does not change the existing catalog/basket/order flow — it reuses the
existing `Order`/`OrderItem` model and layers a PayPal-backed payment on top of it.

* **Flow 1 — pay for an order:** authorize (hold) at pay time, capture (take the money) at fulfilment,
  release on cancel, return on refund.
* **Flow 2 — saved cards:** vault a card once with PayPal and reuse it to pay later.

Every PayPal interaction goes through the **paypal-docs** MCP-documented REST API
(`IPayPalClient` → `PayPalClient`). No PayPal detail comes from anywhere else.

---

## Endpoints

| Method & route | Who | What it does |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog item ids + quantities. Starts *awaiting payment*. Returns **`orderId`**. |
| `POST /api/orders/{orderId}/pay` | shopper | **Authorize** the order total (hold, not take). Body carries `card` **or** `paymentMethodId`. |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Mark fulfilled — **capture** the money. Reports captured amount, PayPal fee, net proceeds. Renews a stale hold automatically. |
| `POST /api/orders/{orderId}/cancel` | **admin** | Cancel before fulfilment — **void** the hold. No money moves. |
| `POST /api/orders/{orderId}/refunds` | shopper | **Refund** a captured payment, full or partial. Carries an `idempotencyKey`. Returns **`refundId`**. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from=&to=` | **admin** | PayPal's transactions for a date range, lined up against eShop orders. |
| `POST /api/payment-methods` | shopper | Save a card. Returns **`paymentMethodId`** + a safe description (never full card details). |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card (also deletes the PayPal vault token). |

`fulfil`, `cancel` and `reconciliation` require the **Administrators** role. Everything else is
shopper-scoped: a shopper can only ever see or act on their own orders and saved cards (cross-user
access returns `404`).

The caller's identity always comes from the JWT (the name claim), never from the request body.

---

## Configuration & secrets

Settings bind from the `PayPal:` configuration section — nothing is hard-coded:

| Key | Env var it comes from | Notes |
| --- | --- | --- |
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` | REST app client id (sandbox business account). |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` | REST app secret. |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` | `sandbox` or `live`. |
| `PayPal:Currency` | `PAYPAL_CURRENCY` | ISO-4217 currency the order total is charged in. |
| `PayPal:BaseUrl` | *(optional)* | If set, used verbatim as the API base for **every** call (incl. token). Otherwise derived from `Environment`. |

**Secrets never live in the repo.** Load them into .NET user-secrets from the environment
(PowerShell, from `src/PublicApi`):

```powershell
dotnet user-secrets set "PayPal:ClientId"     $env:PAYPAL_CLIENT_ID
dotnet user-secrets set "PayPal:ClientSecret" $env:PAYPAL_CLIENT_SECRET
dotnet user-secrets set "PayPal:Environment"  $env:PAYPAL_ENVIRONMENT
dotnet user-secrets set "PayPal:Currency"     $env:PAYPAL_CURRENCY
# PayPal:BaseUrl is optional; leave unset to target the sandbox by environment.
```

---

## Run it (this machine)

The SDK/runtime and database gotchas from the task apply:

```bash
# from the repo root
export DOTNET_ROLL_FORWARD=Major          # .NET 10 SDK rolls forward past the pinned 8.0.x
export ASPNETCORE_ENVIRONMENT=Development  # loads user-secrets
export UseOnlyInMemoryDatabase=true        # no LocalDB on this box
export ASPNETCORE_URLS="https://localhost:8983;http://localhost:8984"

dotnet run --project src/PublicApi/PublicApi.csproj
```

> The in-memory store is **per host and lost on restart** and ignores migrations. Place, pay,
> fulfil and refund orders **within the same run**. Swagger UI is at `https://localhost:8983/swagger`.
> If the dev cert isn't trusted, run `dotnet dev-certs https --trust` (or use `curl -k`).

---

## Verify it yourself (curl)

Sandbox test card: **Visa `4111 1111 1111 1111`**, any future expiry, any CVC. No browser step.
All requests below use `-k` to skip dev-cert validation.

### 1. Get bearer tokens

```bash
API=https://localhost:8983
SHOPPER=$(curl -sk -X POST $API/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)
ADMIN=$(curl -sk -X POST $API/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | jq -r .token)
```

### 2. Flow 1 — place → pay → fulfil → refund

```bash
# Place an order (returns orderId). Item id 5 costs 8.50; two of them = 17.00.
ORDER=$(curl -sk -X POST $API/api/orders -H "Authorization: Bearer $SHOPPER" \
  -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":5,"quantity":2}]}' | jq -r .orderId)

# Pay: authorize the 17.00 hold with the test card (money is NOT taken yet).
curl -sk -X POST $API/api/orders/$ORDER/pay -H "Authorization: Bearer $SHOPPER" \
  -H 'Content-Type: application/json' \
  -d '{"card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123",
               "name":"Test Shopper","billingAddress":{"country":"US","zipCode":"94107"}}}' | jq
# -> status "Authorized", authorizationId set, amount 17.00

# Fulfil (admin): capture the money. Reports capturedAmount, payPalFee, netAmount.
curl -sk -X POST $API/api/orders/$ORDER/fulfil -H "Authorization: Bearer $ADMIN" | jq
# -> status "Captured", captureId set, payPalFee + netAmount populated

# Partial refund (idempotencyKey required). Repeat with the same key -> same refundId (no double refund).
curl -sk -X POST $API/api/orders/$ORDER/refunds -H "Authorization: Bearer $SHOPPER" \
  -H 'Content-Type: application/json' -d '{"amount":5.00,"idempotencyKey":"refund-1"}' | jq
# -> refundId set, payment status "PartiallyRefunded"

# Refund the remainder (omit amount for a full remaining refund).
curl -sk -X POST $API/api/orders/$ORDER/refunds -H "Authorization: Bearer $SHOPPER" \
  -H 'Content-Type: application/json' -d '{"idempotencyKey":"refund-2"}' | jq
# A refund larger than the refundable remainder is rejected with 400.

# See your orders with payment state.
curl -sk $API/api/my-orders -H "Authorization: Bearer $SHOPPER" | jq
```

### 3. Flow 2 — save a card and reuse it

```bash
# Save a card -> paymentMethodId + safe description (brand + last4 only).
PM=$(curl -sk -X POST $API/api/payment-methods -H "Authorization: Bearer $SHOPPER" \
  -H 'Content-Type: application/json' \
  -d '{"card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123",
               "name":"Test Shopper","billingAddress":{"country":"US","zipCode":"94107"}}}' \
  | jq -r .paymentMethodId)

curl -sk $API/api/payment-methods -H "Authorization: Bearer $SHOPPER" | jq

# Place a second order and pay it with the saved card (no card details re-entered).
ORDER2=$(curl -sk -X POST $API/api/orders -H "Authorization: Bearer $SHOPPER" \
  -H 'Content-Type: application/json' -d '{"items":[{"catalogItemId":4,"quantity":1}]}' | jq -r .orderId)
curl -sk -X POST $API/api/orders/$ORDER2/pay -H "Authorization: Bearer $SHOPPER" \
  -H 'Content-Type: application/json' -d "{\"paymentMethodId\":$PM}" | jq
curl -sk -X POST $API/api/orders/$ORDER2/fulfil -H "Authorization: Bearer $ADMIN" | jq

# Delete the saved card; afterwards it is gone from the list and can no longer pay.
curl -sk -X DELETE $API/api/payment-methods/$PM -H "Authorization: Bearer $SHOPPER" -w '%{http_code}\n'
```

### 4. Cancel (void) before fulfilment

```bash
ORDER3=$(curl -sk -X POST $API/api/orders -H "Authorization: Bearer $SHOPPER" \
  -H 'Content-Type: application/json' -d '{"items":[{"catalogItemId":3,"quantity":1}]}' | jq -r .orderId)
curl -sk -X POST $API/api/orders/$ORDER3/pay -H "Authorization: Bearer $SHOPPER" \
  -H 'Content-Type: application/json' \
  -d '{"card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","billingAddress":{"country":"US"}}}' | jq
curl -sk -X POST $API/api/orders/$ORDER3/cancel -H "Authorization: Bearer $ADMIN" | jq
# -> status "Voided" (the hold is released; no money moved)
```

### 5. Reconciliation (admin)

```bash
FROM=$(date -u -d '-2 days' +%Y-%m-%dT%H:%M:%SZ)
TO=$(date -u -d '+5 minutes' +%Y-%m-%dT%H:%M:%SZ)
curl -sk "$API/api/reconciliation?from=$FROM&to=$TO" -H "Authorization: Bearer $ADMIN" | jq
```

The report chunks the range into ≤31-day windows and pages each window to the end, so it covers the
**whole** range. It returns `matched`, `inPayPalNotInEShop`, and `inEShopNotInPayPal` lists.

> **Sandbox note:** PayPal's transaction reporting lags live activity, so a range covering payments
> you just created may legitimately come back with `matched: []` and your fresh orders under
> `inEShopNotInPayPal`. That is expected — the report is correct over a range that already has data.

---

## Design notes

* **State machine.** A `Payment` (one per order) carries the state PayPal owns — the PayPal order id,
  authorization id + status + expiry, capture id + status, captured amount, fee, net — plus the
  refunds — so any later request can act on it. Status flows
  `PendingAuthorization → Authorized → Captured → (Partially)Refunded`, or `Authorized → Voided`.
* **Idempotency.** Pay and fulfil are idempotent in effect: a double-click never authorizes or captures
  twice (guarded by state, and by a stable `PayPal-Request-Id` derived from the globally-unique PayPal
  ids). Refunds dedupe on the caller's `idempotencyKey`; a partial refund can never take the total past
  the captured amount.
* **Stale holds.** At fulfilment, an authorization that has expired is **reauthorized** and then
  captured. One that can no longer be renewed (PayPal only allows it within 30 days) fails with a clear,
  operator-actionable message rather than a silent error.
* **Card safety.** Full card details are only ever sent to PayPal. They are **never** stored in the
  application database (a saved card keeps only the PayPal vault token + brand/last4/expiry) and
  **never** written to logs.
* **3-D Secure.** If PayPal answers a card payment with a browser challenge, the pay call fails with a
  clear message instead of building an approval round-trip. The sandbox test card does not trigger one.
