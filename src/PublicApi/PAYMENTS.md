# PayPal payments & saved cards (PublicApi)

An **additive** capability on top of eShopOnWeb's existing catalog/basket/order model: it collects
money with **PayPal** as the processor and lets a shopper **save a card** for reuse. It does not
replace the existing flow — it adds the money movement (hold at checkout, take at fulfilment, give
back on a return) and the operator actions around it.

All PayPal interaction goes through the **apimatic `paypal-sdk`** (`AsadAli.Checkout.Sdk`,
namespace `PayPalServerSdk`). The only place the SDK is touched is
`Infrastructure/Payments/PayPalPaymentGateway.cs`, behind the `IPayPalPaymentGateway` abstraction.

## Architecture

| Layer | Pieces |
| --- | --- |
| **Domain** (`ApplicationCore`) | `Order` gains `OrderStatus` + a `Payment` child (PayPal ids/status, captured/fee/net, `PaymentRefund` collection). `Buyer`/`PaymentMethod` hold saved cards (vault token + safe descriptor only). Aggregates guard every state transition and the "never refund beyond captured" invariant. |
| **Gateway** | `IPayPalPaymentGateway` (ApplicationCore) → `PayPalPaymentGateway` (Infrastructure). Authorize (card or vaulted), capture, reauthorize, void, refund, vault, delete-vault, transaction search. Translates SDK failures into caller-safe `PaymentGatewayException`s (4xx vs 5xx preserved; no SDK detail leaked). |
| **Services** | `OrderPaymentService`, `PaymentMethodService`, `ReconciliationService` orchestrate domain + gateway (idempotency, stale-hold renewal, ownership). |
| **API** | Endpoints under `/api` on `PublicApi` (JWT). Shopper-scoped by token identity; operator actions restricted to the administrator role. |

## Endpoints

| Method & route | Who | Purpose |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog items. Returns top-level `orderId`. Starts `AwaitingPayment`. |
| `POST /api/orders/{orderId}/pay` | shopper | **Authorize** (hold) the order total — card details **or** `savedPaymentMethodId`. Does not capture. Idempotent per order. |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Capture the hold (money taken). Reports captured amount, PayPal fee, net. Renews a stale hold first. |
| `POST /api/orders/{orderId}/cancel` | **admin** | Void the hold before fulfilment; funds released. |
| `POST /api/orders/{orderId}/refunds` | shopper | Refund the capture, full or partial. Body: `{ amount?, idempotencyKey }`. Returns top-level `refundId`. |
| `GET  /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET  /api/reconciliation?from={iso}&to={iso}` | **admin** | PayPal's transactions for the range (all pages) lined up against eShop orders. |
| `POST /api/payment-methods` | shopper | Save (vault) a card. Returns top-level `paymentMethodId` + a safe descriptor (brand, last4, expiry). |
| `GET  /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card (also deletes the vault token; no longer usable to pay). |

A shopper can only see/act on their own orders and cards; another shopper's resource returns `404`.

## Configuration (`PayPal:` section)

Bound in `Infrastructure/Payments/PayPalServiceCollectionExtensions.cs`. **Values live in
.NET user-secrets / environment — never in the repo.**

| Key | Source env var | Notes |
| --- | --- | --- |
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` | REST client id of the sandbox **business** account |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` | |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` | `sandbox` (default) or `live`/`production` |
| `PayPal:Currency` | `PAYPAL_CURRENCY` | e.g. `USD` |
| `PayPal:BaseUrl` | *(optional)* | If set, used **verbatim** for every call (incl. the OAuth token request), overriding the environment. |

Load the secrets (values from the environment, never written to a repo file):

```bash
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"     --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET" --project src/PublicApi
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   --project src/PublicApi
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      --project src/PublicApi
```

## Running (this machine)

The SDK is pinned to 8.0.x but only .NET 10 is installed, and there is no LocalDB, so run with
roll-forward and the in-memory database. The in-memory store is **per host and lost on restart** —
create, pay, fulfil and refund an order within the **same run**.

```bash
export DOTNET_ROLL_FORWARD=Major
cd src/PublicApi
ASPNETCORE_ENVIRONMENT=Development \
UseOnlyInMemoryDatabase=true \
ASPNETCORE_URLS="https://localhost:8563;http://localhost:8564" \
dotnet run
# Swagger: https://localhost:8563/swagger
```

## Verify (curl)

```bash
B=https://localhost:8563/api
CARD='{"number":"4111111111111111","expiry":"2027-12","securityCode":"123","name":"Test Shopper","billingAddress":{"city":"Redmond","state":"WA","postalCode":"98052","countryCode":"US"}}'
TOK=$(curl -sk -X POST $B/authenticate -H 'Content-Type: application/json' -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | sed -E 's/.*"token":"([^"]+)".*/\1/')
ADM=$(curl -sk -X POST $B/authenticate -H 'Content-Type: application/json' -d '{"username":"admin@microsoft.com","password":"Pass@word1"}'    | sed -E 's/.*"token":"([^"]+)".*/\1/')

# Flow 1: place -> authorize -> fulfil (capture) -> refund
OID=$(curl -sk -X POST $B/orders -H "Authorization: Bearer $TOK" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":2,"quantity":1}]}' | sed -E 's/.*"orderId":([0-9]+).*/\1/')
curl -sk -X POST $B/orders/$OID/pay     -H "Authorization: Bearer $TOK" -H 'Content-Type: application/json' -d "{\"card\":$CARD}"
curl -sk -X POST $B/orders/$OID/fulfil  -H "Authorization: Bearer $ADM"                               # capturedGross/payPalFee/netAmount
curl -sk -X POST $B/orders/$OID/refunds -H "Authorization: Bearer $TOK" -H 'Content-Type: application/json' -d '{"amount":10.00,"idempotencyKey":"r1"}'
curl -sk    $B/my-orders                -H "Authorization: Bearer $TOK"

# Flow 2: save a card, reuse it to pay a second order, then delete it
PMID=$(curl -sk -X POST $B/payment-methods -H "Authorization: Bearer $TOK" -H 'Content-Type: application/json' \
  -d "{\"card\":$CARD,\"alias\":\"my visa\"}" | sed -E 's/.*"paymentMethodId":([0-9]+).*/\1/')
O2=$(curl -sk -X POST $B/orders -H "Authorization: Bearer $TOK" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":1,"quantity":1}]}' | sed -E 's/.*"orderId":([0-9]+).*/\1/')
curl -sk -X POST $B/orders/$O2/pay -H "Authorization: Bearer $TOK" -H 'Content-Type: application/json' -d "{\"savedPaymentMethodId\":$PMID}"
curl -sk -X DELETE $B/payment-methods/$PMID -H "Authorization: Bearer $TOK"

# Operator reconciliation (report may be empty for very recent activity — PayPal reporting lags; not a gap)
curl -sk "$B/reconciliation?from=2026-08-01T00:00:00Z&to=2026-08-31T23:59:59Z" -H "Authorization: Bearer $ADM"
```

### Idempotency notes
- **Pay** is idempotent per order (a double-click reuses the same PayPal order/authorization — no second hold).
- **Refund** uses the caller-supplied `idempotencyKey`: repeating a key returns the same refund; two distinct
  keys are two legitimate partial refunds. PayPal remembers request-ids **account-wide**, so across repeated
  test runs on the same account use a **fresh** `idempotencyKey` each time.
- If PayPal answers a card with a browser-approval challenge, the API returns `402` and stops (no approval
  round-trip is built) — the sandbox test Visa does not trigger this.
