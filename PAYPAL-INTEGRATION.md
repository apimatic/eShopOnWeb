# PayPal payments & saved cards — eShopOnWeb PublicApi

This adds real money movement to eShopOnWeb as an **additive** capability on `src/PublicApi`
(JWT-authenticated). PayPal is the payment processor, integrated through a **hand-written client built
directly to the OpenAPI specs in `api-specs/`** — no third-party PayPal SDK is used.

## What was added

### Domain (`src/ApplicationCore`)
- `Entities/OrderAggregate/Order.cs` gains an `OrderStatus` (AwaitingPayment → Authorized → Fulfilled;
  Cancelled; PartiallyRefunded/Refunded) with guarded transitions. The existing Order/OrderItem model
  is reused for placing orders.
- `Entities/PaymentAggregate/Payment.cs` holds the state PayPal owns for one order: the PayPal order id,
  the authorization (hold) id/status/expiry, the capture id/status and its gross/fee/net amounts, and a
  collection of `PaymentRefund`s. It enforces the money rules (refund only after capture, never beyond
  the captured amount).
- `Entities/PaymentAggregate/SavedCard.cs` holds a vaulted card: PayPal's vault token plus a safe
  description (brand, last four, expiry). **No full card number is ever stored.**
- `Interfaces/IPayPalClient.cs` is the client contract; `Services/OrderPaymentService.cs` and
  `Services/SavedCardService.cs` orchestrate the flows.

### PayPal client (`src/Infrastructure/PayPal`)
`PayPalHttpClient` implements `IPayPalClient` against these spec documents:
- **checkout_orders_v2** — create order (intent `AUTHORIZE`) and authorize it with a card or a vaulted card.
- **payments_payment_v2** — capture, reauthorize, void, refund.
- **vault_payment_tokens_v3** — vault / delete a saved card.
- **transaction_search_v1** — the reconciliation report (chunked to ≤31-day windows, all pages).

OAuth tokens are obtained with the client-credentials flow from the specs' security scheme and cached.

### Endpoints (`src/PublicApi/PaymentEndpoints`)
| Method & route | Role | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | place an order from catalog items → returns **`orderId`** |
| `POST /api/orders/{orderId}/pay` | shopper | authorize (hold) the total with a card or a saved card |
| `POST /api/orders/{orderId}/fulfil` | **admin** | capture the held funds; shows captured/fee/net |
| `POST /api/orders/{orderId}/cancel` | **admin** | release the hold before fulfilment |
| `POST /api/orders/{orderId}/refunds` | shopper | refund in full/part → returns **`refundId`** (idempotency key) |
| `GET /api/my-orders` | shopper | caller's orders with payment state |
| `GET /api/reconciliation?from=&to=` | **admin** | PayPal transactions lined up against eShop orders |
| `POST /api/payment-methods` | shopper | save a card → returns **`paymentMethodId`** |
| `GET /api/payment-methods` | shopper | caller's saved cards |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | remove a saved card |

Every shopper endpoint acts only on the caller's own data (another shopper's order/card reads as 404).
Fulfil, cancel and reconciliation require the `Administrators` role.

## Configuration

Settings bind from the `PayPal:` section (keys `ClientId`, `ClientSecret`, `Environment`, `Currency`,
`BaseUrl`). **No secret values live in the repo** — they are loaded into .NET user-secrets from the
`PAYPAL_*` environment variables. `PayPal:BaseUrl` is an optional override used verbatim for every call
(including the token request) when set; otherwise the base URL is derived from `PayPal:Environment`.

Load the credentials into user-secrets (run once, from `src/PublicApi`):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
# optional: dotnet user-secrets set "PayPal:BaseUrl" "<url>"
```

## Run it (this machine)

```bash
export DOTNET_ROLL_FORWARD=Major          # .NET 10 SDK rolls forward from the pinned 8.0.x
cd src/PublicApi
ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true \
  dotnet run --no-launch-profile --urls "https://localhost:30523;http://localhost:30524"
```

> In-memory data resets on restart and is isolated per host, so place → pay → fulfil → refund the orders
> you create within the **same** run, driven entirely through PublicApi.

## Verify it yourself (no browser needed)

All requests go to `https://localhost:30523` (use `curl -k` for the dev cert). Sandbox test card:
`4111 1111 1111 1111`, any future expiry, any CVC.

1. **Get tokens** (shopper + admin; default password `Pass@word1`):
   ```bash
   SH=$(curl -sk https://localhost:30523/api/authenticate -H "Content-Type: application/json" \
        -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
   AD=$(curl -sk https://localhost:30523/api/authenticate -H "Content-Type: application/json" \
        -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
   ```
2. **Place an order** (returns `orderId`):
   ```bash
   curl -sk https://localhost:30523/api/orders -H "Authorization: Bearer $SH" -H "Content-Type: application/json" \
     -d '{"items":[{"catalogItemId":1,"quantity":2}],"shipToAddress":{"street":"1 Market","city":"SF","state":"CA","country":"US","zipCode":"94105"}}'
   ```
3. **Pay (authorize)** — the response shows `payment.authorizationId` and `payment.amount` equal to the order total:
   ```bash
   curl -sk https://localhost:30523/api/orders/1/pay -H "Authorization: Bearer $SH" -H "Content-Type: application/json" \
     -d '{"card":{"number":"4111111111111111","expiry":"2027-12","securityCode":"123","name":"John Shopper","addressLine1":"1 Market","city":"SF","state":"CA","postalCode":"94105","countryCode":"US"}}'
   ```
4. **Fulfil (capture)** as admin — the payment now shows `captureId`, `capturedAmount`, `payPalFee`, `netAmount`:
   ```bash
   curl -sk -X POST https://localhost:30523/api/orders/1/fulfil -H "Authorization: Bearer $AD"
   ```
5. **Refund** part of it (repeat with the same `idempotencyKey` → no second refund):
   ```bash
   curl -sk https://localhost:30523/api/orders/1/refunds -H "Authorization: Bearer $SH" -H "Content-Type: application/json" \
     -d '{"amount":1.00,"idempotencyKey":"refund-1"}'
   ```
6. **Saved card**: save it, then pay a *new* order with it:
   ```bash
   PM=$(curl -sk https://localhost:30523/api/payment-methods -H "Authorization: Bearer $SH" -H "Content-Type: application/json" \
     -d '{"number":"4111111111111111","expiry":"2027-12","securityCode":"123","name":"John Shopper","addressLine1":"1 Market","city":"SF","state":"CA","postalCode":"94105","countryCode":"US"}' \
     | python -c "import sys,json;print(json.load(sys.stdin)['paymentMethodId'])")
   # place another order (orderId 2), then:
   curl -sk https://localhost:30523/api/orders/2/pay -H "Authorization: Bearer $SH" -H "Content-Type: application/json" \
     -d "{\"savedPaymentMethodId\":$PM}"
   ```
7. **List / delete** saved cards, and **reconciliation** (admin), e.g. over the last 20 days:
   ```bash
   curl -sk https://localhost:30523/api/payment-methods -H "Authorization: Bearer $SH"
   curl -sk -X DELETE https://localhost:30523/api/payment-methods/$PM -H "Authorization: Bearer $SH"
   curl -sk "https://localhost:30523/api/reconciliation?from=2026-08-21T00:00:00Z&to=2026-09-10T23:59:59Z" -H "Authorization: Bearer $AD"
   ```

Swagger UI (all endpoints, "OrderPaymentEndpoints" / "PaymentMethodEndpoints"): `https://localhost:30523/swagger`.

## Notes / edge behaviour
- **Idempotency**: pay/fulfil use deterministic PayPal request ids so a double-click never holds or
  captures twice; refunds de-duplicate on the caller's key per capture (distinct partial refunds use
  distinct keys).
- **Stale authorization**: at fulfilment an expired hold is re-authorized automatically; if it can no
  longer be renewed the operator gets a 409 saying the shopper must pay again.
- **3-D Secure / payer-action challenge**: if PayPal asks for browser approval, the pay call returns
  HTTP 422 explaining it rather than building an approval round-trip (the sandbox test card does not
  trigger this).
- **Reconciliation lag**: PayPal's transaction reporting lags live activity, so a range covering
  payments you just made can legitimately return few/no rows; run it over an older range that has data.
