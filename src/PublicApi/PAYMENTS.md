# PayPal payments & saved cards (PublicApi)

This adds real money movement to eShopOnWeb as an **additive** capability on top of the existing
catalog/basket/order flow. Payment is processed with **PayPal** (card + vaulted cards). It does not
replace anything in the catalog/basket/order path.

Everything is exposed on **`src/PublicApi`** (JWT-authenticated), routed under `/api/`.

## What was built

**Flow 1 — pay for an order**

| Method & route | Who | What it does |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog items. Returns top-level `orderId`. Starts awaiting payment. |
| `POST /api/orders/{orderId}/pay` | shopper (owner) | **Authorize** (hold) the order total. Card details *or* a saved-card id. Money is not taken yet. |
| `POST /api/orders/{orderId}/fulfil` | **operator** | Mark fulfilled and **capture** the money. Reports captured amount, PayPal fee, net proceeds. Renews a stale authorization first. |
| `POST /api/orders/{orderId}/cancel` | **operator** | Cancel before fulfilment: void the hold, release the funds. |
| `POST /api/orders/{orderId}/refunds` | shopper (owner) or operator | Refund a captured order, full or partial. Returns top-level `refundId`. Carries a caller `idempotencyKey`. |
| `GET /api/my-orders` | shopper | The caller's own orders with payment state. |
| `GET /api/reconciliation?from={iso}&to={iso}` | **operator** | PayPal's transaction record lined up against eShop orders across the whole range. |

**Flow 2 — saved cards**

| Method & route | Who | What it does |
|---|---|---|
| `POST /api/payment-methods` | shopper | Vault a card at PayPal. Returns top-level `paymentMethodId` + a safe descriptor (brand/last4/expiry). |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper (owner) | Remove a saved card (also deletes the PayPal vault token). |

`fulfil`, `cancel` and `reconciliation` require the `Administrators` role. Every other endpoint is
shopper-scoped and acts only on the caller's own data. No full card number/CVV is ever stored in this
app's database or written to logs.

## The PayPal integration

- Built **only** from the OpenAPI specs in `api-specs/paypal/` (no third-party PayPal SDK):
  - `checkout_orders_v2` — create order + authorize (`POST /v2/checkout/orders`, `.../authorize`)
  - `payments_payment_v2` — capture / void / reauthorize / refund
  - `vault_payment_tokens_v3` — save/remove cards (direct card vault, with setup-token fallback)
  - `transaction_search_v1` — reconciliation (`GET /v1/reporting/transactions`, paged over 31-day windows)
- OAuth client-credentials token (`POST /v1/oauth2/token`) is cached and auto-refreshed.
- Base URL resolves from `PayPal:Environment` (sandbox/live), overridden verbatim by `PayPal:BaseUrl`
  when set (including the token call).
- Code lives in `src/Infrastructure/PayPal/` (gateways + typed HTTP client) and
  `src/ApplicationCore/Services/` (orchestration); the domain state is the `Payment` aggregate in
  `src/ApplicationCore/Entities/PaymentAggregate/`.

### Idempotency
- Each payment carries a globally-unique `Reference` (a GUID) used as PayPal's `invoice_id` and as the
  seed for authorize/capture/reauthorize `PayPal-Request-Id`s — so a double-click never authorizes or
  captures twice, and reused order ids across in-memory runs never collide.
- Refunds dedupe on the caller's `idempotencyKey` (stored per payment); the same key never refunds
  twice, while two distinct partial refunds of the same capture are allowed. A refund never exceeds the
  captured amount.

## Configuration

Bound from the `PayPal:` configuration section (never hard-coded):

| Key | From env var |
|---|---|
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` |
| `PayPal:Currency` | `PAYPAL_CURRENCY` |
| `PayPal:BaseUrl` | *(optional override)* |

Secret **values** live only in the environment / .NET user-secrets — never in the repo. `Program.cs`
maps the `PAYPAL_*` environment variables onto these keys at startup, so the same build runs against
whatever account the environment supplies.

Load the credentials into user-secrets once (values come from the environment, names only shown here):

```bash
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"     --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET" --project src/PublicApi
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   --project src/PublicApi
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      --project src/PublicApi
```

## Run it (this machine)

```bash
export DOTNET_ROLL_FORWARD=Major            # .NET 10 SDK present, targets net8.0
export ASPNETCORE_ENVIRONMENT=Development
export UseOnlyInMemoryDatabase=true         # no LocalDB here; data lives for one run only
export ASPNETCORE_URLS="https://localhost:9103;http://localhost:9104"
dotnet run --project src/PublicApi
```

Because the in-memory store resets on restart (and Web/PublicApi have separate stores), place, pay,
fulfil and refund the orders you create **within the same run**, through PublicApi only.

## Verify end-to-end (no browser)

```bash
API="https://localhost:9103/api"
tok(){ curl -sk -X POST "$API/authenticate" -H "Content-Type: application/json" \
  -d "{\"username\":\"$1\",\"password\":\"Pass@word1\"}" | python -c "import sys,json;print(json.load(sys.stdin)['token'])"; }
SHOP=$(tok demouser@microsoft.com); ADMIN=$(tok admin@microsoft.com)
SA=(-H "Authorization: Bearer $SHOP"); AA=(-H "Authorization: Bearer $ADMIN"); J=(-H "Content-Type: application/json")

# 1) place -> 2) pay (authorize) with the sandbox test card
OID=$(curl -sk -X POST "$API/orders" "${SA[@]}" "${J[@]}" \
  -d '{"items":[{"catalogItemId":5,"quantity":2},{"catalogItemId":4,"quantity":1}]}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
curl -sk -X POST "$API/orders/$OID/pay" "${SA[@]}" "${J[@]}" \
  -d '{"card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","cardholderName":"Demo User"}}'

# 3) fulfil (operator) -> capture, with fee + net
curl -sk -X POST "$API/orders/$OID/fulfil" "${AA[@]}"

# 4) refund part of it (idempotent on the key)
curl -sk -X POST "$API/orders/$OID/refunds" "${SA[@]}" "${J[@]}" -d '{"amount":9.00,"idempotencyKey":"ref-1"}'

# 5) saved card: save, then pay a SECOND order with it
PMID=$(curl -sk -X POST "$API/payment-methods" "${SA[@]}" "${J[@]}" \
  -d '{"card":{"number":"4111111111111111","expiry":"2031-05","securityCode":"123","cardholderName":"Demo User"}}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['paymentMethodId'])")
OID2=$(curl -sk -X POST "$API/orders" "${SA[@]}" "${J[@]}" -d '{"items":[{"catalogItemId":4,"quantity":1}]}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
curl -sk -X POST "$API/orders/$OID2/pay" "${SA[@]}" "${J[@]}" -d "{\"savedPaymentMethodId\":$PMID}"

# 6) my-orders, and operator reconciliation
curl -sk "$API/my-orders" "${SA[@]}"
curl -sk "$API/reconciliation?from=2026-08-01T00:00:00Z&to=2026-08-31T23:59:59Z" "${AA[@]}"
```

Test card: Visa `4111 1111 1111 1111`, any future expiry, any CVC, any name/address.

> **Reconciliation note:** PayPal's transaction reporting lags live activity, so a range covering
> payments you just created can legitimately show them as `EShopOnly` (or come back empty) until PayPal
> catches up. That is expected sandbox behaviour, not a missing capability — the report matches on each
> payment's globally-unique reference and PayPal's own ids, so it is correct over any range that has
> settled data.
