# PayPal Payments & Saved Cards (PublicApi)

This adds real money movement to eShopOnWeb as an **additive** capability on the `PublicApi`
project: a logged-in shopper places an order and pays by card (PayPal **authorize** at pay time,
**capture** at fulfilment, **refund** on return), and can **save a card** to reuse on a later order.
It does not replace the existing catalog/basket/order flow.

All PayPal interaction goes through one gateway, `IPayPalClient` (implemented by
`Infrastructure/PayPal/PayPalClient.cs`), which is the only place that talks HTTP/JSON to PayPal.

## Endpoints

| Method & route | Who | What |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog items. Returns **`orderId`**. Starts `AwaitingPayment`. |
| `POST /api/orders/{orderId}/pay` | shopper (own order) | **Authorize** the total — hold funds, don't take them. Body carries card details *or* `savedPaymentMethodId`. |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Mark fulfilled and **capture**. Payment then shows captured gross, PayPal fee, net. Renews a stale hold automatically. |
| `POST /api/orders/{orderId}/cancel` | **admin** | Cancel before fulfilment — **void** the hold, no money moved. |
| `POST /api/orders/{orderId}/refunds` | shopper (own order) | **Refund** the capture, full or partial. Returns **`refundId`**. Body carries `idempotencyKey` (+ optional `amount`). |
| `GET /api/my-orders` | shopper | The caller's own orders with payment state. |
| `GET /api/reconciliation?from={iso}&to={iso}` | **admin** | PayPal's transactions for the range lined up against eShop orders (whole range, paged). |
| `POST /api/payment-methods` | shopper | Save (vault) a card. Returns **`paymentMethodId`** + safe descriptors (brand, last4) — never full card details. |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper (own card) | Remove a saved card — it stops appearing and can no longer pay. |

Ownership is enforced everywhere: one shopper can never see, use, or act on another's orders or
cards (a mismatch returns `403`, identically whether or not the resource exists).

## Configuration & secrets

Settings bind from the `PayPal:` section using exactly these keys — **no values are committed**:

| Key | Source env var | Notes |
| --- | --- | --- |
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` | REST app client id (sandbox business account). |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` | REST app secret. |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` | `sandbox` or `live`. |
| `PayPal:Currency` | `PAYPAL_CURRENCY` | ISO-4217, e.g. `USD`. Amounts are priced/charged in this currency. |
| `PayPal:BaseUrl` | *(optional)* | If set, used verbatim for **every** call incl. the token request; otherwise derived from `Environment`. |

Load the sandbox credentials from the environment into **.NET user-secrets** (they land outside the
repo, in your user profile):

```bash
PROJ=src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"     --project "$PROJ"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET" --project "$PROJ"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   --project "$PROJ"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      --project "$PROJ"
```

## Run it (this machine)

Only the .NET 10 SDK is installed and there is no LocalDB, so:

```bash
export DOTNET_ROLL_FORWARD=Major          # global.json already uses rollForward: latestMajor
export ASPNETCORE_ENVIRONMENT=Development  # loads user-secrets
export UseOnlyInMemoryDatabase=true        # no LocalDB; store is per-process and resets on restart
export ASPNETCORE_URLS="https://localhost:8623;http://localhost:8624"

dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

> In-memory caveat: orders, payments and saved cards live only for a single run, and PublicApi has
> its own store separate from the Web host. Create, pay, fulfil and refund within the same run —
> which is why `POST /api/orders` exists on this API.

## Verify end to end

### Automated

With the app running, drive every flow (authorize → capture → refund, cancel/void, save-card reuse,
ownership/role checks, reconciliation) against the sandbox test card:

```bash
python tests/manual/verify_paypal_flow.py           # defaults to https://localhost:8623
```

Expected tail: `==== RESULT: 63 checks passed, 0 failed ====`.

### Manual (curl)

Get tokens (shopper `demouser@microsoft.com`, admin `admin@microsoft.com`, password `Pass@word1`):

```bash
API=https://localhost:8623/api
SHOP=$(curl -sk -X POST $API/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
ADMIN=$(curl -sk -X POST $API/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
```

1. **Place an order** (note the returned `orderId`):

   ```bash
   curl -sk -X POST $API/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
     -d '{"items":[{"catalogItemId":1,"quantity":1},{"catalogItemId":2,"quantity":2}]}'
   ```

2. **Pay (authorize)** with the sandbox card `4111 1111 1111 1111` (any future expiry / CVC):

   ```bash
   curl -sk -X POST $API/orders/1/pay -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
     -d '{"card":{"number":"4111111111111111","expiry":"2027-05","securityCode":"123","cardholderName":"Demo Shopper","billingAddress":{"countryCode":"US","addressLine1":"1 Market St","adminArea2":"San Jose","adminArea1":"CA","postalCode":"95131"}}}'
   ```

3. **Fulfil (capture)** as admin — response shows `capturedGross`, `payPalFee`, `netAmount`:

   ```bash
   curl -sk -X POST $API/orders/1/fulfil -H "Authorization: Bearer $ADMIN"
   ```

4. **Refund** (partial shown; omit `amount` for the full remainder; reuse a key to prove no double refund):

   ```bash
   curl -sk -X POST $API/orders/1/refunds -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
     -d '{"amount":10.00,"idempotencyKey":"refund-1"}'
   ```

5. **Save a card**, then **reuse it** on a new order:

   ```bash
   curl -sk -X POST $API/payment-methods -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
     -d '{"card":{"number":"4111111111111111","expiry":"2027-05","securityCode":"123"},"label":"My Visa"}'
   # -> paymentMethodId

   curl -sk -X POST $API/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
     -d '{"items":[{"catalogItemId":4,"quantity":1}]}'
   curl -sk -X POST $API/orders/2/pay -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
     -d '{"savedPaymentMethodId":1}'
   ```

6. **Cancel** (void) another order before fulfilment, list **my-orders**, run **reconciliation**:

   ```bash
   curl -sk -X POST $API/orders/3/cancel -H "Authorization: Bearer $ADMIN"
   curl -sk $API/my-orders -H "Authorization: Bearer $SHOP"
   curl -sk "$API/reconciliation?from=2026-07-12T00:00:00Z&to=2026-08-11T00:00:00Z" -H "Authorization: Bearer $ADMIN"
   ```

## Behaviour notes

- **Idempotent in effect.** A double-clicked `pay` never authorizes twice; a double-clicked `fulfil`
  never captures twice. Authorize/capture use stable per-order PayPal request-ids. Refund dedups on
  the caller's `idempotencyKey`; two *distinct* keys give two legitimate partial refunds, capped so a
  partly-refunded order never becomes refundable beyond what was captured.
- **Stale holds** are renewed (reauthorized) before capture rather than failing fulfilment. A hold
  that can no longer be renewed returns a `422` with an operator-actionable message.
- **Reconciliation** pages and date-chunks (≤31-day windows) so the whole range is covered. PayPal
  transaction reporting lags live activity by up to a few hours, so a range covering payments you
  just created may legitimately come back empty on the PayPal side — that is expected, not a gap.
  Just-created captures then appear under `inEShopNotInPayPal` until PayPal's reporting catches up.
- **Card data** (PAN/CVV) is passed straight to PayPal and is never stored in the app database or
  written to logs. Saved cards keep only PayPal's vault token id plus brand and last-four.
