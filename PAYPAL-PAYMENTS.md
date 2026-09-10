# PayPal payments & saved cards (PublicApi)

Additive capability on top of eShopOnWeb: authorize/capture/refund real money through **PayPal**
(as the processor) and let a shopper **vault a card** for reuse. Everything is exposed on the
**`src/PublicApi`** JWT API, routed under `/api/`, and is drivable end-to-end without a browser.

The original catalog/basket/order flow is untouched. `Order` gains a fulfilment `Status`; a new
`Payment` aggregate carries the money-movement state PayPal owns (hold, capture, refunds).

## PayPal contract

Every PayPal call is built **strictly** against the OpenAPI specs in `api-specs/paypal/` — no
PayPal SDK is used. The hand-written client lives in `src/Infrastructure/PayPal/PayPalGateway.cs`.

| Capability | Spec used | Endpoint(s) |
|---|---|---|
| Access token | (all) `securitySchemes.Oauth2` | `POST /v1/oauth2/token` (client-credentials) |
| Authorize (hold) | `checkout_orders_v2` | `POST /v2/checkout/orders` (intent `AUTHORIZE`, `payment_source.card` or `card.vault_id`) → auto/explicit `…/authorize` |
| Capture at fulfil | `payments_payment_v2` | `POST /v2/payments/authorizations/{id}/capture` (reads `seller_receivable_breakdown`) |
| Renew stale hold | `payments_payment_v2` | `POST /v2/payments/authorizations/{id}/reauthorize` |
| Cancel (void) | `payments_payment_v2` | `POST /v2/payments/authorizations/{id}/void` |
| Refund | `payments_payment_v2` | `POST /v2/payments/captures/{id}/refund` |
| Vault card / list / delete | `vault_payment_tokens_v3` | `POST/GET/DELETE /v3/vault/payment-tokens` |
| Reconciliation | `transaction_search_v1` | `GET /v1/reporting/transactions` (chunked ≤31 days, fully paginated) |

## Endpoints

Shopper-scoped (any authenticated user; acts only on the caller's own data):
- `POST /api/orders` → `{ orderId, … }`
- `POST /api/orders/{orderId}/pay` — body `{ "card": {…} }` **or** `{ "savedCardId": N }`
- `GET  /api/my-orders`
- `POST /api/payment-methods` → `{ paymentMethodId, brand, lastDigits, … }`
- `GET  /api/payment-methods`
- `DELETE /api/payment-methods/{paymentMethodId}`

Operator-only (administrator role):
- `POST /api/orders/{orderId}/fulfil`
- `POST /api/orders/{orderId}/cancel`
- `POST /api/orders/{orderId}/refunds` — body `{ "amount": 10.00, "idempotencyKey": "…" }` (amount optional = full) → `{ refundId, … }`
- `GET  /api/reconciliation?from={iso}&to={iso}`

## Configuration & secrets

Settings bind from the **`PayPal:`** section — no values are hard-coded or committed:
`PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`, and the
optional `PayPal:BaseUrl` (when set, used verbatim for every call incl. the token request).

Load the sandbox credentials from the environment into .NET user-secrets (values never touch the repo):

```bash
dotnet user-secrets --project src/PublicApi set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets --project src/PublicApi set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets --project src/PublicApi set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets --project src/PublicApi set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

## Run (this machine)

`global.json` is set to `rollForward: latestMajor`; run with `DOTNET_ROLL_FORWARD=Major` (only the
.NET 10 SDK is installed). Use the in-memory store (no LocalDB here); data lives for one run only,
so pay/fulfil/refund the orders you create in that same run.

```bash
export DOTNET_ROLL_FORWARD=Major
dotnet build src/PublicApi/PublicApi.csproj

ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="https://localhost:30603;http://localhost:30604" \
UseOnlyInMemoryDatabase=true \
dotnet run --project src/PublicApi
```

## Verify manually (curl)

```bash
B=https://localhost:30603
SHOP=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)
ADMIN=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | jq -r .token)

# 1) place -> pay (authorize hold) -> fulfil (capture) -> refund
OID=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":1,"quantity":2}]}' | jq .orderId)
curl -sk -X POST $B/api/orders/$OID/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","name":"Demo User",
       "billingAddress":{"addressLine1":"1 Main","adminArea2":"Redmond","adminArea1":"WA","postalCode":"98052","countryCode":"US"}}}'
curl -sk -X POST $B/api/orders/$OID/fulfil  -H "Authorization: Bearer $ADMIN"   # capture; shows fee + net
curl -sk -X POST $B/api/orders/$OID/refunds -H "Authorization: Bearer $ADMIN" -H "Content-Type: application/json" \
  -d '{"amount":10.00,"idempotencyKey":"my-key-1"}'                            # partial refund

# 2) saved card -> reuse on a new order
PMID=$(curl -sk -X POST $B/api/payment-methods -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","name":"Demo User",
       "billingAddress":{"addressLine1":"1 Main","adminArea2":"Redmond","adminArea1":"WA","postalCode":"98052","countryCode":"US"}}' | jq .paymentMethodId)
OID2=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":2,"quantity":1}]}' | jq .orderId)
curl -sk -X POST $B/api/orders/$OID2/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d "{\"savedCardId\":$PMID}"

# 3) reconciliation (admin)
curl -sk "$B/api/reconciliation?from=2026-09-08T00:00:00Z&to=2026-09-11T00:00:00Z" -H "Authorization: Bearer $ADMIN"
```

An automated run of all of the above (39 assertions) lives in `tools/e2e_verify.py`
(`python tools/e2e_verify.py` against a running instance).

## Notes

- **Idempotency:** `pay` and `fulfil` are idempotent in effect (a double-click never authorizes or
  captures twice), enforced by payment state plus a stable `PayPal-Request-Id`. Refunds dedupe on
  the caller's idempotency key; two *distinct* keys are two legitimate partial refunds. A refund can
  never exceed what remains of the capture.
- **Stale holds:** at fulfil, an expired authorization is reauthorized before capture; if it can no
  longer be renewed the operator gets an actionable error instead of a silent failure.
- **Reconciliation lag:** PayPal's transaction report lags live activity, so a range covering
  just-created payments can legitimately show them as `EShopOnly` (eShop knows, PayPal's report
  doesn't yet). This is expected sandbox behaviour, not a gap.
- **Browser challenges:** if PayPal answered a card with a 3-D Secure / `PAYER_ACTION_REQUIRED`
  challenge, the API stops and reports it (HTTP 409) rather than building an approval round-trip.
  The sandbox test Visa does not trigger this.
- Full card details are never stored in the app database and never logged; only brand + last 4 +
  expiry are kept for a saved card.
