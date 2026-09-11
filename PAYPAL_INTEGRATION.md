# PayPal payments & saved cards (PublicApi)

Additive capability on top of eShopOnWeb: a shopper pays for an order by card, an operator
fulfils/cancels/refunds it, and a shopper can save a card in PayPal's Vault and reuse it.
PayPal is the payment processor; money is **held at pay**, **taken at fulfil**, **released at
cancel**, and **returned on refund**. Nothing replaces the existing catalog/basket/order flow.

## Endpoints (all under `/api/`, JWT-authenticated on PublicApi)

Shopper (acts only on the caller's own data):
- `POST /api/orders` — place an order from catalog items → returns **`orderId`**.
- `POST /api/orders/{orderId}/pay` — authorize (hold) the total with card **or** a saved card.
- `POST /api/orders/{orderId}/refunds` — refund captured payment (full/partial) → returns **`refundId`**.
- `GET  /api/my-orders` — the caller's orders with payment state.
- `POST /api/payment-methods` — save a card → returns **`paymentMethodId`** (+ brand/last4/expiry only).
- `GET  /api/payment-methods` — the caller's saved cards.
- `DELETE /api/payment-methods/{paymentMethodId}` — remove a saved card.

Operator (Administrators role):
- `POST /api/orders/{orderId}/fulfil` — capture the money; reports captured amount, PayPal fee, net.
- `POST /api/orders/{orderId}/cancel` — void the hold before fulfilment.
- `GET  /api/reconciliation?from={iso}&to={iso}` — PayPal transactions vs eShop orders over the whole range.

Payment operations are idempotent in effect (a double-click never authorizes/captures twice);
refunds honor a caller-supplied `idempotencyKey`.

## Configuration

Settings are bound from the `PayPal:` section — **values are never committed**. Keys:
`PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`,
`PayPal:BaseUrl` (optional; when set it is used verbatim for every PayPal call incl. the token
request). Load them into user-secrets from the provided environment variables:

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   # e.g. sandbox
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      # e.g. USD
# Optional: dotnet user-secrets set "PayPal:BaseUrl" "$PAYPAL_BASEURL"
```

## Run (this machine)

.NET 10 SDK only + no LocalDB, so roll forward and use the in-memory store
(`UseOnlyInMemoryDatabase=true` is already set for the Development profile):

```bash
cd repo
DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="https://localhost:31403;http://localhost:31404" \
  dotnet run --project src/PublicApi --no-launch-profile
```

Note: with the in-memory provider, orders/payments/saved cards live only for a single run —
pay, fulfil and refund the orders you created in that same run.

## Verify it yourself

Automated (drives every flow end-to-end against the sandbox with the test card):

```bash
python verify_paypal.py
```

Or by hand (get a bearer token first — the storefront cookie won't work here):

```bash
API=https://localhost:31403
TOK=$(curl -sk -X POST $API/api/authenticate -H 'Content-Type: application/json' \
      -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | python -c 'import sys,json;print(json.load(sys.stdin)["token"])')
ADM=$(curl -sk -X POST $API/api/authenticate -H 'Content-Type: application/json' \
      -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | python -c 'import sys,json;print(json.load(sys.stdin)["token"])')

# 1) place an order (note the orderId)
curl -sk -X POST $API/api/orders -H "Authorization: Bearer $TOK" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":5,"quantity":2},{"catalogItemId":4,"quantity":1}]}'

# 2) authorize with the sandbox test card (Visa 4111...)
curl -sk -X POST $API/api/orders/1/pay -H "Authorization: Bearer $TOK" -H 'Content-Type: application/json' \
  -d '{"card":{"number":"4111111111111111","expiry":"12/2030","securityCode":"123","cardholderName":"Test Buyer","billingAddress":{"countryCode":"US","postalCode":"95131"}}}'

# 3) operator fulfils -> capture with fee/net
curl -sk -X POST $API/api/orders/1/fulfil -H "Authorization: Bearer $ADM"

# 4) shopper refunds part of it (idempotencyKey required)
curl -sk -X POST $API/api/orders/1/refunds -H "Authorization: Bearer $TOK" -H 'Content-Type: application/json' \
  -d '{"amount":5.00,"idempotencyKey":"demo-1"}'

# 5) save a card, then reuse it on a new order
curl -sk -X POST $API/api/payment-methods -H "Authorization: Bearer $TOK" -H 'Content-Type: application/json' \
  -d '{"number":"4111111111111111","expiry":"12/2030","securityCode":"123","cardholderName":"Test Buyer","billingAddress":{"countryCode":"US"}}'
# ... POST /api/orders again, then pay with {"savedPaymentMethodId": <id>}

# 6) reconciliation over a range with data (recent activity may lag up to ~3h in sandbox)
curl -sk "$API/api/reconciliation?from=2026-08-01T00:00:00Z&to=2026-09-11T00:00:00Z" -H "Authorization: Bearer $ADM"
```

## Design notes

- `OrderPayment` aggregate carries PayPal-owned state (hold/capture/refund ids + status) keyed
  by the existing `Order`. `SavedPaymentMethod` stores only a Vault token + safe description;
  full card details are never stored or logged.
- Pay creates a PayPal order with `intent=AUTHORIZE` funded by the card/vault token; fulfil
  captures the authorization; cancel voids it; refunds go against the capture id. A stale
  authorization is reauthorized before capture, and an unrenewable one returns an
  operator-actionable message.
- Reconciliation chunks the range into ≤31-day windows and pages through every page, matching
  PayPal transactions to orders by the eShop reference stamped on each PayPal order.
