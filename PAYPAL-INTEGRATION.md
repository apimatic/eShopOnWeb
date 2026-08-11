# PayPal payments & saved cards (PublicApi)

This adds real money movement to eShopOnWeb, with **PayPal** as the processor and **saved
cards** for reuse. It is additive: the existing catalog/basket/order model is reused, with a
new `Payment` record attached to each order plus a `SavedCard` per shopper.

Everything is exposed on **`src/PublicApi`** (JWT auth), routed under `/api/`. The caller's
identity comes from the token (the JWT `Name` claim, which is the eShop `BuyerId`).

## What was built

### Endpoints
| Method & route | Who | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog items. Returns `orderId`. Starts *AwaitingPayment*. |
| `POST /api/orders/{orderId}/pay` | shopper | **Authorize** the total (hold funds) with a one-off card **or** a `savedCardId`. |
| `POST /api/orders/{orderId}/fulfil` | admin | Mark fulfilled and **capture** the money; reports captured amount, PayPal fee, net. |
| `POST /api/orders/{orderId}/cancel` | admin | Cancel before fulfilment: **void** the hold, funds released. |
| `POST /api/orders/{orderId}/refunds` | shopper | **Refund** a captured payment, full or partial. Returns `refundId`. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from=&to=` | admin | PayPal's transactions for a date range, lined up against eShop orders. |
| `POST /api/payment-methods` | shopper | Save a card. Returns `paymentMethodId` + a safe descriptor. |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card (and its PayPal vault token). |

### PayPal APIs used (from `api-specs/paypal`, the authoritative contract)
- **Checkout Orders v2** — create order (`intent=AUTHORIZE`) + `/authorize` with the card or vault id.
- **Payments v2** — `/capture` (fulfil), `/reauthorize` (renew a stale hold), `/void` (cancel), `/captures/{id}/refund`.
- **Vault Payment Tokens v3** — save / delete a card.
- **Transaction Search v1** — reconciliation (paged, chunked into ≤31-day windows).
- **OAuth2 client-credentials** (`/v1/oauth2/token`) — token acquisition/caching.

No third-party PayPal SDK is used; the typed client (`src/Infrastructure/PayPal/PayPalGateway.cs`)
is hand-written against the spec.

### Key design points
- **Money**: amounts come from catalog prices; currency from `PayPal:Currency`. The hold equals the order total to the cent.
- **Idempotency**: every payment gets a per-payment GUID (`IdempotencyToken`); all `PayPal-Request-Id`
  headers derive from it, so a double-click never authorizes/captures twice and keys stay unique on a
  shared PayPal account. Refunds also take a caller-supplied idempotency key (repeat = no second refund;
  two distinct partial refunds are allowed).
- **Stale holds**: at fulfilment an expired authorization is renewed (`/reauthorize`) before capture; if it
  can no longer be renewed the operator gets an actionable message.
- **Ownership**: saved cards and orders are scoped to the caller; one shopper can never see/use/act on another's.
- **No card storage**: full PAN/CVV are never persisted or logged — only PayPal's vault token and a safe
  descriptor (brand, last 4, expiry).

---

## Prerequisites (this machine)

Credentials are read from environment variables and loaded into **.NET user-secrets** (never written
into the repo). From `src/PublicApi`:

```bash
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   # sandbox
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      # USD
# PayPal:BaseUrl is optional; leave unset to derive from Environment.
```

`global.json` uses `rollForward: latestMajor`; run with `DOTNET_ROLL_FORWARD=Major` (the .NET 10 SDK
builds the net8.0 projects and the ASP.NET Core 8 runtime runs them).

## Run

```bash
cd src/PublicApi
DOTNET_ROLL_FORWARD=Major \
ASPNETCORE_ENVIRONMENT=Development \
UseOnlyInMemoryDatabase=true \
ASPNETCORE_URLS="https://localhost:8503;http://localhost:8504" \
dotnet run --no-launch-profile
```

Swagger: <https://localhost:8503/swagger>. In-memory data lives for one run, so pay/fulfil/refund the
orders you create in the same run.

> Seeded users: `demouser@microsoft.com` (shopper) and `admin@microsoft.com` (operator), password `Pass@word1`.
> Sandbox test card: `4111 1111 1111 1111`, any future expiry, any CVC.

---

## Verify it yourself (curl)

```bash
B=https://localhost:8503/api

# 1) Tokens
SHOP=$(curl -sk -X POST $B/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
ADMIN=$(curl -sk -X POST $B/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")

# ---------- Flow 1: pay for an order ----------
# 2) Place an order (catalog items 1 x1 + 2 x2 = 36.50)
OID=$(curl -sk -X POST $B/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":1,"quantity":1},{"catalogItemId":2,"quantity":2}]}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")

# 3) Pay (authorize / hold) with the sandbox test card
curl -sk -X POST $B/orders/$OID/pay -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d '{
  "card":{"number":"4111111111111111","expiry":"2027-01","securityCode":"123","cardholderName":"Demo User",
    "billingAddress":{"addressLine1":"123 Main St","city":"Kent","state":"OH","postalCode":"44240","countryCode":"US"}}}'

# 4) See it (shopper): status Authorized
curl -sk $B/my-orders -H "Authorization: Bearer $SHOP"

# 5) Fulfil (admin): capture — response shows capturedAmount, payPalFee, netAmount
curl -sk -X POST $B/orders/$OID/fulfil -H "Authorization: Bearer $ADMIN"

# 6) Refund part of it (shopper); repeat with the same key → same refundId (no double refund)
curl -sk -X POST $B/orders/$OID/refunds -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"amount":10.00,"idempotencyKey":"ref-A"}'
# Full remaining refund (omit amount)
curl -sk -X POST $B/orders/$OID/refunds -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"idempotencyKey":"ref-B"}'

# ---------- Flow 2: saved cards ----------
# 7) Save a card → paymentMethodId
PMID=$(curl -sk -X POST $B/payment-methods -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d '{
  "card":{"number":"4111111111111111","expiry":"2029-05","securityCode":"123","cardholderName":"Demo User",
    "billingAddress":{"addressLine1":"123 Main St","city":"Kent","state":"OH","postalCode":"44240","countryCode":"US"}}}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['paymentMethodId'])")
curl -sk $B/payment-methods -H "Authorization: Bearer $SHOP"

# 8) Place a second order and pay it with the saved card
OID2=$(curl -sk -X POST $B/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":5,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
curl -sk -X POST $B/orders/$OID2/pay -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d "{\"savedCardId\":$PMID}"

# 9) Cancel the second order before fulfilment (admin void): funds released
curl -sk -X POST $B/orders/$OID2/cancel -H "Authorization: Bearer $ADMIN"

# 10) Delete the saved card → 204; afterwards it is gone and can no longer pay
curl -sk -o /dev/null -w "%{http_code}\n" -X DELETE $B/payment-methods/$PMID -H "Authorization: Bearer $SHOP"

# ---------- Reconciliation (admin) ----------
# from/to are ISO-8601 date-times. A range covering only just-created payments may return them as
# "eShop-only" because PayPal's transaction reporting lags; run over a wider range that has data.
curl -sk -G $B/reconciliation -H "Authorization: Bearer $ADMIN" \
  --data-urlencode "from=2026-07-01T00:00:00Z" --data-urlencode "to=2026-08-11T23:59:59Z"
```

### What "correct" looks like
- **pay**: `status=Authorized`, `authorizationId` set, `amount` = order total; paying again returns the *same* authorization.
- **fulfil**: `status=Captured`, and `capturedAmount` / `payPalFee` / `netAmount` populated from PayPal.
- **refunds**: partial → `PartiallyRefunded`; full/remaining → `Refunded`; over-refund → `400`; repeat key → same `refundId`.
- **cancel**: `status=Voided`.
- **saved cards**: response shows brand/last4/expiry only; another shopper's list is empty; after delete the card can't pay (`400`).
- **admin-only**: a shopper calling `fulfil` / `cancel` / `reconciliation` gets `403`.
- **reconciliation**: lists PayPal transactions (`PayPalOnly` / `Matched`) and eShop payments PayPal doesn't yet show (`EShopOnly`), across the whole range.
