# PayPal payments & saved cards (PublicApi)

An **additive** capability on top of eShopOnWeb: collect money with **PayPal** as the processor,
and let a shopper **save a card** to reuse on a later order. It reuses the existing
`Order`/`OrderItem` model and lives entirely on the JWT-authenticated **`src/PublicApi`** project.
Nothing replaces the existing catalog/basket/order flow.

The money moves in the shape of a real payment:

- **Authorize** at checkout — put a hold on the money (no capture yet).
- **Capture** at fulfilment — that is when the money is actually taken; the payment then shows
  PayPal's captured amount, fee, and net proceeds.
- **Void** on cancel before fulfilment — release the hold, no money moved.
- **Refund** after fulfilment — in full or in part, never beyond what was captured.

## Endpoints

All routes are under `/api/`, JWT-authenticated; the caller's identity comes from the token.
Operator actions (**fulfil, cancel, reconciliation**) require the administrator role. Every other
endpoint is shopper-scoped and acts only on the caller's own data.

| Method & route | Who | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog item ids + quantities. Returns `orderId`. Starts *awaiting payment*. |
| `POST /api/orders/{orderId}/pay` | shopper | Authorize (hold) the order total, paying with a one-off `card` **or** a `savedCardId`. |
| `POST /api/orders/{orderId}/fulfil` | admin | Fulfil and **capture**; renews a stale hold automatically. |
| `POST /api/orders/{orderId}/cancel` | admin | Cancel before fulfilment; **voids** the hold. |
| `POST /api/orders/{orderId}/refunds` | shopper | Refund a captured order, full or partial. Requires `idempotencyKey`. Returns `refundId`. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from=&to=` | admin | PayPal's records vs eShop orders over a range (ISO-8601, all pages). |
| `POST /api/payment-methods` | shopper | Save (vault) a card. Returns `paymentMethodId` + safe descriptor. |
| `GET /api/payment-methods` | shopper | The caller's saved cards (safe descriptors only). |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card; afterwards unusable. |

**Idempotency.** Payment operations are idempotent in effect: a double-click never authorizes or
captures twice (the payment state machine short-circuits, and every mutating PayPal call carries a
stable `PayPal-Request-Id`). Refunds take a caller-supplied `idempotencyKey` — repeating it returns
the original refund; two distinct partial refunds remain legitimate as long as the total never
exceeds the captured amount.

## Configuration

Settings bind from the **`PayPal:`** section — never hard-coded, never committed:

| Key | From env var |
|---|---|
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` |
| `PayPal:Currency` | `PAYPAL_CURRENCY` |
| `PayPal:BaseUrl` | *(optional)* verbatim API base URL used for every call, including the token request |

Load them into .NET user-secrets for the PublicApi project (values come from the environment):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

## Design

- **Domain** (`ApplicationCore/Entities/PaymentAggregate`): `Payment` (aggregate root, one per order)
  holds the state PayPal owns — the hold (authorization id/status/expiry), the capture
  (id/status/gross/fee/net), and the `PaymentRefund` collection — plus a `PaymentStatus` state machine.
  `SavedCard` holds only a PayPal vault token id and a safe descriptor (brand, last four, expiry) —
  **never** a card number.
- **Gateway** (`Infrastructure/Payments/PayPalPaymentGateway`): the sole boundary to PayPal via the
  `paypal-sdk` plugin (`AsadAli.Checkout.Sdk` / `PayPalServerSdk`). Direct-card flow — create an order
  with `intent=AUTHORIZE` carrying the card, which authorizes at create time (drivable without a
  browser). Translates every SDK/transport failure into a single `PaymentGatewayException` so no SDK
  type leaks out; a 3-D Secure / `PAYER_ACTION_REQUIRED` challenge surfaces as
  `PaymentApprovalRequiredException` rather than a browser round-trip.
- **Orchestration** (`ApplicationCore/Services`): `OrderPaymentService`, `SavedCardService`,
  `ReconciliationService`.
- **Endpoints** (`PublicApi/PaymentEndpoints`): one class per route, following the project's
  `MinimalApi.Endpoint` convention.

## Persistence caveats on this machine

The in-memory database (`UseOnlyInMemoryDatabase=true`) is per-host and resets on restart, so **pay,
fulfil and refund the orders you created in the same run**. An order placed through the Web storefront
is invisible to PublicApi — which is exactly why `POST /api/orders` exists here.

---

## Verify it yourself

### 0. Prerequisites

- Only the .NET 10 SDK is installed but the app targets .NET 8. `global.json` uses
  `rollForward: latestMajor`; run with `DOTNET_ROLL_FORWARD=Major`.
- Ensure the HTTPS dev cert is trusted: `dotnet dev-certs https --check` (add `--trust` if needed).
  The examples below use `curl -k` to skip verification.
- Load the four `PayPal:*` secrets (see **Configuration** above).

### 1. Run PublicApi

```bash
cd <repo root>
DOTNET_ROLL_FORWARD=Major \
UseOnlyInMemoryDatabase=true \
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="https://localhost:8923;http://localhost:8924" \
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

Swagger: <https://localhost:8923/swagger>.

### 2. Get tokens (seeded users, password `Pass@word1`)

```bash
B=https://localhost:8923
SHOP=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
ADMIN=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
CARD='{"number":"4111111111111111","expiry":"2027-01","securityCode":"123","name":"Demo User","billingCountryCode":"US"}'
```

### 3. Flow 1 — pay, fulfil, refund

```bash
# Place an order (items 5 x2 + 4 x1 = $29.00)
curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":5,"quantity":2},{"catalogItemId":4,"quantity":1}]}'
# -> {"orderId":1,...,"paymentStatus":"AwaitingPayment"}

# Authorize (hold) with the sandbox Visa
curl -sk -X POST $B/api/orders/1/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d "{\"card\":$CARD}"
# -> paymentStatus":"Authorized", authorizationId set

# Fulfil (capture) as admin — money is taken; see gross/fee/net
curl -sk -X POST $B/api/orders/1/fulfil -H "Authorization: Bearer $ADMIN"
# -> paymentStatus":"Captured","capturedGross":29.00,"payPalFee":1.24,"netAmount":27.76

# Partial refund $10 under an idempotency key
curl -sk -X POST $B/api/orders/1/refunds -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"amount":10.00,"idempotencyKey":"r1"}'
# -> {"refundId":"...","status":"COMPLETED","amount":10.00}

# Repeating key r1 returns the SAME refund (no double refund); a $20 refund now is rejected (409, > $19 left)
```

### 4. Cancel (before fulfilment)

```bash
OID=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":3,"quantity":1}]}' | sed -n 's/.*"orderId":\([0-9]*\).*/\1/p')
curl -sk -X POST $B/api/orders/$OID/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" -d "{\"card\":$CARD}"
curl -sk -X POST $B/api/orders/$OID/cancel -H "Authorization: Bearer $ADMIN"   # -> "Cancelled" (hold released)
```

### 5. Flow 2 — save a card and reuse it

```bash
PMID=$(curl -sk -X POST $B/api/payment-methods -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d "{\"card\":$CARD,\"label\":\"My Visa\"}" | sed -n 's/.*"paymentMethodId":\([0-9]*\).*/\1/p')
curl -sk $B/api/payment-methods -H "Authorization: Bearer $SHOP"           # lists the saved card (last4 only)

OID2=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":2,"quantity":1}]}' | sed -n 's/.*"orderId":\([0-9]*\).*/\1/p')
curl -sk -X POST $B/api/orders/$OID2/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d "{\"savedCardId\":$PMID}"                                             # pays with the saved card
curl -sk -X DELETE $B/api/payment-methods/$PMID -H "Authorization: Bearer $SHOP"   # 204; card now gone & unusable
```

### 6. My orders & reconciliation

```bash
curl -sk $B/api/my-orders -H "Authorization: Bearer $SHOP"
curl -sk "$B/api/reconciliation?from=2026-08-01T00:00:00Z&to=2026-08-12T00:00:00Z" -H "Authorization: Bearer $ADMIN"
```

> PayPal's transaction reporting lags live activity, so a reconciliation range covering payments you
> just created can legitimately come back empty — that is an expected sandbox result. Over a range that
> already has data, each eShop capture appears as a `Matched` line against PayPal's own record.

### 7. Isolation checks (expected)

- Admin `GET /api/my-orders` does not show the shopper's orders.
- Admin (a different buyer) refunding the shopper's order → `404`.
- Unauthenticated `pay` → `401`; shopper hitting `fulfil`/`cancel`/`reconciliation` → `403`.
