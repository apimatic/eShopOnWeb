# PayPal payments & saved cards (PublicApi)

Adds PayPal as the payment processor for one-time orders, plus saved cards, to eShopOnWeb.
This is **additive** — the existing Catalog → Basket → Order flow is unchanged. Everything is
exposed as JWT-authenticated HTTP endpoints on **`src/PublicApi`**, under `/api/`.

## Endpoints

| Method & route | Purpose |
|---|---|
| `POST /api/orders` | Place an order from catalog item ids + quantities. Returns top-level `orderId`. Starts **AwaitingPayment**. |
| `POST /api/orders/{orderId}/pay` | Pay with PayPal — body carries **either** `card` details **or** a saved `paymentMethodId`. |
| `POST /api/orders/{orderId}/refunds` | Full refund of the order's payment. Order becomes **Refunded**. |
| `GET  /api/my-orders` | The caller's orders with their payment state. |
| `POST /api/payment-methods` | Save (vault) a card. Returns top-level `paymentMethodId` + a safe descriptor (brand / last4 / expiry). |
| `GET  /api/payment-methods` | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | Remove a saved card. |

The caller's identity comes from the JWT (`ClaimTypes.Name`). A shopper only ever sees / uses /
deletes their own orders and cards. Amounts come from catalog prices, currency **USD**. Full card
details are never stored in the app database and never logged — only a PayPal vault token id plus a
brand/last4/expiry descriptor is kept.

## Configuration (secrets never in the repo)

Bound from the `PayPal:` config section — `PayPal:ClientId`, `PayPal:ClientSecret`,
`PayPal:Environment` (`sandbox`), `PayPal:BaseUrl` (optional verbatim base-URL override). Load the
sandbox credentials from the environment variables into **user-secrets** (values stay outside the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
```

## Run it (this machine)

Only the .NET 10 SDK is installed and there is no LocalDB, so roll forward and use the in-memory store:

```bash
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development          # so user-secrets load
export UseOnlyInMemoryDatabase=true                # no LocalDB on this box
export ASPNETCORE_URLS="https://localhost:8303;http://localhost:8304"   # your assigned port block
dotnet run --project src/PublicApi --no-launch-profile
```

> In-memory data is per-process and resets on restart, so drive the whole flow through PublicApi in a
> single run (that is why `POST /api/orders` exists here).

## Verify end-to-end (curl)

```bash
API=https://localhost:8303

# 1. Bearer token from PublicApi's own authenticate endpoint
TOKEN=$(curl -sk -X POST $API/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)
AUTH="Authorization: Bearer $TOKEN"

# 2. Place an order  ->  note orderId, paymentStatus=AwaitingPayment
curl -sk -X POST $API/api/orders -H "$AUTH" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":5,"quantity":2},{"catalogItemId":4,"quantity":1}]}'

# 3. Pay it with the PayPal sandbox test Visa  ->  paymentStatus=Paid
curl -sk -X POST $API/api/orders/1/pay -H "$AUTH" -H "Content-Type: application/json" -d '{
  "card":{"cardholderName":"Demo User","number":"4111111111111111","expiryMonth":12,"expiryYear":2030,
  "securityCode":"123","billingAddress":{"addressLine1":"1 Market St","city":"San Francisco",
  "state":"CA","postalCode":"94105","countryCode":"US"}}}'

# 4. Pay again (double-click)  ->  still Paid, SAME capture id, no second charge
# 5. Refund  ->  paymentStatus=Refunded ; refund again -> same refund id, no second refund
curl -sk -X POST $API/api/orders/1/refunds -H "$AUTH"

# 6. Your orders with payment state
curl -sk $API/api/my-orders -H "$AUTH"

# --- Saved cards ---
# 7. Save a card  ->  note paymentMethodId, brand=VISA, last4=1111 (never the full number)
curl -sk -X POST $API/api/payment-methods -H "$AUTH" -H "Content-Type: application/json" -d '{
  "alias":"My Visa","card":{"cardholderName":"Demo User","number":"4111111111111111",
  "expiryMonth":11,"expiryYear":2031,"securityCode":"123","billingAddress":{"addressLine1":"1 Market St",
  "city":"San Francisco","state":"CA","postalCode":"94105","countryCode":"US"}}}'

# 8. List cards ; 9. place a new order and pay with the saved card
curl -sk $API/api/payment-methods -H "$AUTH"
curl -sk -X POST $API/api/orders -H "$AUTH" -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":2,"quantity":1}]}'
curl -sk -X POST $API/api/orders/2/pay -H "$AUTH" -H "Content-Type: application/json" -d '{"paymentMethodId":1}'

# 10. Delete the card (204) ; afterwards it is gone from the list and can no longer pay (404)
curl -sk -X DELETE $API/api/payment-methods/1 -H "$AUTH" -w "%{http_code}\n"
```

Swagger UI is at `https://localhost:8303/swagger`.

## Automated tests

Hermetic integration tests (fake gateway, no live PayPal) cover both flows, idempotency, ownership
isolation and validation:

```bash
export DOTNET_ROLL_FORWARD=Major
dotnet test tests/PublicApiIntegrationTests
```

## Notes

- **Idempotency:** payment/refund keys are stable per order (so a double-click reuses the same PayPal
  request id and cannot charge/refund twice) yet unique per order instance; the app also short-circuits
  once an order is Paid/Refunded. A dedicated handler blocks any transport-level resend of a write, so a
  create/capture/refund is delivered at most once.
- **Production go-live:** the pinned PayPal SDK exposes only the `Sandbox` server. Targeting live PayPal
  would require setting `PayPal:BaseUrl` to the live host (or a newer SDK). In scope here (sandbox) this
  is not a limitation.
- A SQL Server migration (`AddPaymentsAndSavedCards`) is included for real-database deployments; the
  in-memory provider used on this machine ignores migrations.
