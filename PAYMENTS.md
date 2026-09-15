# PayPal payments & saved cards (PublicApi)

An **additive** capability on top of eShopOnWeb: collect money for an order via **PayPal**
(authorize → capture → refund) and let a shopper **save a card** for reuse. It does not replace
the existing catalog/basket/order flow. All capabilities are HTTP endpoints on **`src/PublicApi`**
(JWT), routed under `/api/`.

PayPal is consumed through a **hand-written client built to the OpenAPI specs** in
`api-specs/paypal` — no third-party PayPal SDK. The specs used:

| Capability | Spec | Endpoints |
|---|---|---|
| Hold (authorize) | `checkout_orders_v2` | `POST /v2/checkout/orders`, `/authorize` |
| Capture / void / reauthorize / refund | `payments_payment_v2` | `/authorizations/{id}/capture`,`/void`,`/reauthorize`, `/captures/{id}/refund` |
| Saved cards | `vault_payment_tokens_v3` | `POST /v3/vault/payment-tokens`, `DELETE …/{id}` |
| Reconciliation | `transaction_search_v1` | `GET /v1/reporting/transactions` |
| OAuth token | (all specs) | `POST /v1/oauth2/token` (client-credentials) |

## Endpoints

Shopper-scoped (any authenticated user; acts only on the caller's own data):

- `POST /api/orders` → `{ orderId }` — place an order from catalog items (awaiting payment).
- `POST /api/orders/{orderId}/pay` — authorize (hold) the order total with `card` **or** `savedPaymentMethodId`.
- `POST /api/orders/{orderId}/refunds` → `{ refundId }` — refund a captured order (full/partial), `idempotencyKey` required.
- `GET /api/my-orders` — the caller's orders with payment state.
- `POST /api/payment-methods` → `{ paymentMethodId }` — save a card (safe descriptor only).
- `GET /api/payment-methods` — the caller's saved cards.
- `DELETE /api/payment-methods/{paymentMethodId}` — remove a saved card.

Operator-only (administrator role):

- `POST /api/orders/{orderId}/fulfil` — mark fulfilled and **capture** the money.
- `POST /api/orders/{orderId}/cancel` — **void** the hold before fulfilment.
- `GET /api/reconciliation?from={iso}&to={iso}` — PayPal transactions vs eShop orders over a range.

## Configuration

Settings bind from the `PayPal:` section (never hard-coded):
`PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`, and the
optional `PayPal:BaseUrl` (used verbatim for **every** call, including the token request, when set;
otherwise the base URL is derived from `Environment` — `sandbox` → `https://api-m.sandbox.paypal.com`).

Load the sandbox credentials from environment variables into **.NET user-secrets** (values never
enter the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   # sandbox
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      # USD
```

## Run (this machine)

Only the .NET 10 SDK is installed and there is no LocalDB, so roll forward and use the in-memory
store. Bind to your assigned port block.

```bash
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development
export UseOnlyInMemoryDatabase=true
export ASPNETCORE_URLS="https://localhost:8423;http://localhost:8424"
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

> In-memory data lives only for one run, and Web/PublicApi have separate stores — drive the whole
> flow through PublicApi (that is why `POST /api/orders` exists). Swagger: `https://localhost:8423/swagger`.

## Verify end-to-end (sandbox test card, no browser)

Test card: Visa `4111 1111 1111 1111`, any future expiry (`YYYY-MM`), any CVC/name/address.

```bash
B=https://localhost:8423
SHOP=$(curl -sk -X POST $B/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)
ADMIN=$(curl -sk -X POST $B/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | jq -r .token)
CARD='{"card":{"number":"4111111111111111","expiry":"2027-12","securityCode":"123","cardholderName":"Test Shopper","billingAddress":{"line1":"1 Market St","city":"San Jose","state":"CA","postalCode":"95131","countryCode":"US"}}}'

# 1) place + pay (authorize/hold) + fulfil (capture) + partial refund
OID=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":2,"quantity":2},{"catalogItemId":5,"quantity":1}]}' | jq .orderId)
curl -sk -X POST $B/api/orders/$OID/pay    -H "Authorization: Bearer $SHOP"  -H 'Content-Type: application/json' -d "$CARD"
curl -sk -X POST $B/api/orders/$OID/fulfil -H "Authorization: Bearer $ADMIN"        # capturedAmount / payPalFee / netAmount
curl -sk -X POST $B/api/orders/$OID/refunds -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"amount":10.00,"idempotencyKey":"refund-1"}'
curl -sk $B/api/my-orders -H "Authorization: Bearer $SHOP"

# 2) save a card, then pay a second order with it
PM=$(curl -sk -X POST $B/api/payment-methods -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d "$CARD" | jq .paymentMethodId)
OID2=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":1,"quantity":1}]}' | jq .orderId)
curl -sk -X POST $B/api/orders/$OID2/pay -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d "{\"savedPaymentMethodId\":$PM}"

# 3) cancel (void) before fulfilment, on a fresh order
OID3=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":3,"quantity":1}]}' | jq .orderId)
curl -sk -X POST $B/api/orders/$OID3/pay    -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' -d "$CARD"
curl -sk -X POST $B/api/orders/$OID3/cancel -H "Authorization: Bearer $ADMIN"

# 4) reconciliation (operator)
curl -sk "$B/api/reconciliation?from=2026-07-01T00:00:00Z&to=2026-08-11T00:00:00Z" -H "Authorization: Bearer $ADMIN"
```

Notes:
- Payment ops are idempotent in effect — a double-click never authorizes/captures twice; a repeated
  refund `idempotencyKey` never refunds twice, while two distinct keys are two legitimate partial refunds.
- A stale hold is **reauthorized** before capture; one that can no longer be renewed returns an actionable message.
- PayPal reporting lags, so a reconciliation range covering just-created payments may return empty — expected.
  Those orders appear as `EShopOnly` until PayPal's report catches up.
- If PayPal ever answers a card payment with a browser challenge (3-D Secure / payer action), the pay
  endpoint returns `422` with a clear message rather than building an approval round-trip.
