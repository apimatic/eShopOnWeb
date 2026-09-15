# PayPal payments & saved cards (PublicApi)

Adds money movement to eShopOnWeb: PayPal is the processor. A shopper places and pays an order
(authorize → hold), an operator fulfils (capture), cancels (void) or refunds it, and a shopper can
save a card once and reuse it. Additive — the existing catalog/basket/order flow is untouched.

## Endpoints

Shopper-scoped (JWT, acts only on the caller's own data):

| Method & route | Purpose |
|---|---|
| `POST /api/orders` | Place an order from catalog item ids + quantities. Returns `orderId`. Starts *AwaitingPayment*. |
| `POST /api/orders/{orderId}/pay` | Authorize the total (hold funds). Body carries `card` **or** `savedPaymentMethodId`. Idempotent. |
| `POST /api/orders/{orderId}/refunds` | Refund a captured order, full or partial. Body: `amount?`, `idempotencyKey`. Returns `refundId`. |
| `GET  /api/my-orders` | The caller's orders with payment state. |
| `POST /api/payment-methods` | Save (vault) a card. Returns `paymentMethodId` + safe description (brand, last four). |
| `GET  /api/payment-methods` | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | Remove a saved card (also un-vaulted at PayPal). |

Operator-scoped (JWT + `Administrators` role):

| Method & route | Purpose |
|---|---|
| `POST /api/orders/{orderId}/fulfil` | Mark fulfilled and **capture** the money. Renews a stale authorization; reports if it can't. |
| `POST /api/orders/{orderId}/cancel` | Cancel before fulfilment: **void** the hold (no money moved). |
| `GET  /api/reconciliation?from={iso}&to={iso}` | PayPal's transactions for the range, lined up against eShop orders. |

## Configuration (`PayPal` section — bind, never hard-code)

`PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment` (`sandbox`/`live`), `PayPal:Currency`,
and optional `PayPal:BaseUrl` (used verbatim for **every** call, including the token request, when set).
Load the sandbox values into user-secrets for the PublicApi project:

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

Secret **values** never live in the repo; the base URL is derived from `Environment` unless `BaseUrl` overrides it.

## Run (this machine: .NET 10 SDK only, no LocalDB)

```bash
export DOTNET_ROLL_FORWARD=Major
ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true \
ASPNETCORE_URLS="https://localhost:8763;http://localhost:8764" \
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

The in-memory store is per-process, so pay/fulfil/refund the orders you create **in the same run**.

## Verify (no browser needed — PayPal sandbox test card `4111 1111 1111 1111`)

```bash
B=https://localhost:8763
tok(){ curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d "{\"username\":\"$1\",\"password\":\"Pass@word1\"}" | python -c "import sys,json;print(json.load(sys.stdin)['token'])"; }
S="Authorization: Bearer $(tok demouser@microsoft.com)"   # shopper
A="Authorization: Bearer $(tok admin@microsoft.com)"      # operator
CARD='{"card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","cardholderName":"Test","billingAddress":{"line1":"1 Market St","city":"San Jose","state":"CA","postalCode":"95131","countryCode":"US"}}}'

# Flow 1 — pay for an order
OID=$(curl -sk -X POST $B/api/orders -H "$S" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":2,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
curl -sk -X POST $B/api/orders/$OID/pay     -H "$S" -H "Content-Type: application/json" -d "$CARD"    # -> Authorized
curl -sk -X POST $B/api/orders/$OID/fulfil  -H "$A"                                                    # -> Captured (+fee/net)
curl -sk -X POST $B/api/orders/$OID/refunds -H "$S" -H "Content-Type: application/json" -d '{"amount":10.00,"idempotencyKey":"r1"}'
curl -sk $B/api/my-orders -H "$S"

# Flow 2 — save a card and reuse it
PM=$(curl -sk -X POST $B/api/payment-methods -H "$S" -H "Content-Type: application/json" \
  -d '{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","cardholderName":"Test"}' | python -c "import sys,json;print(json.load(sys.stdin)['paymentMethodId'])")
O2=$(curl -sk -X POST $B/api/orders -H "$S" -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":5,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
curl -sk -X POST $B/api/orders/$O2/pay -H "$S" -H "Content-Type: application/json" -d "{\"savedPaymentMethodId\":$PM}"  # reuse
curl -sk -X POST $B/api/orders/$O2/fulfil -H "$A"
curl -sk -X DELETE $B/api/payment-methods/$PM -H "$S"   # -> 204; card gone and unusable

# Reconciliation (admin). Reporting lags, so a range covering brand-new payments can be empty.
curl -sk "$B/api/reconciliation?from=2026-06-01T00:00:00Z&to=2026-08-12T00:00:00Z" -H "$A"
```

## Notes on design

- **Idempotency.** Each payment gets a stable `IdempotencyToken`; it namespaces the `PayPal-Request-Id`
  of every authorize/capture/refund so double-clicks collapse to one operation at PayPal and never
  collide across orders/runs. Refunds also carry the caller's key; a repeat returns the original refund,
  distinct partial refunds each stand, and the total refunded can never exceed the captured amount.
- **Stale holds.** At fulfilment, if a capture fails on an expired authorization it is reauthorized and
  re-captured; if it can no longer be renewed the operator gets an actionable message.
- **Card safety.** Full card numbers pass through only as transient request bodies to PayPal — never
  stored in the app DB, never logged. Saved cards keep only the PayPal vault token + brand/last-four.
- **PayPal surface used.** OAuth2 client-credentials; Orders v2 create+authorize (raw card or `vault_id`);
  Payments v2 authorization capture/void/reauthorize and capture refund; Vault v3 setup-token→payment-token
  and delete; Reporting v1 transaction search (paged, range chunked to 31-day windows).
