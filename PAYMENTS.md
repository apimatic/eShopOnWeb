# PayPal payments & saved cards (PublicApi)

This adds real money movement to eShopOnWeb as an **additive** capability on the
`src/PublicApi` project: a shopper pays for an order by card (or a saved card), and an
operator fulfils, cancels or refunds it. PayPal is the processor. The existing
catalog/basket/order flow is untouched.

The integration calls PayPal's REST API directly (Orders v2, Payments v2, Vault v3,
Reporting v1) — the approach the PayPal plugin sanctions for full control
(`references/mcp-tools.md`: *"For full control over request structure, fall back to the REST
API directly."*). All calls go to the PayPal **sandbox**.

## Configuration (secrets stay out of the repo)

Settings are bound from the `PayPal:` configuration section — nothing is hard-coded:

| Key | From env var | Notes |
|-----|--------------|-------|
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` | |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` | |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` | `sandbox` / `production` |
| `PayPal:Currency` | `PAYPAL_CURRENCY` | e.g. `USD` |
| `PayPal:BaseUrl` | *(optional)* | If set, used **verbatim** as the API base for every call (incl. the token request); otherwise derived from `Environment`. |

Load the credentials into .NET user-secrets for `src/PublicApi` (values come from the
environment; they are never written into any repo file):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
# Optional: dotnet user-secrets set "PayPal:BaseUrl" "https://api-m.sandbox.paypal.com"
```

## Run (this machine)

```bash
export DOTNET_ROLL_FORWARD=Major          # .NET 10 SDK present, app targets net8.0
export ASPNETCORE_ENVIRONMENT=Development
export UseOnlyInMemoryDatabase=true       # no LocalDB here; data lives only for the run
export ASPNETCORE_URLS="https://localhost:10723;http://localhost:10724"
dotnet run --project src/PublicApi --no-launch-profile
```

Swagger: <https://localhost:10723/swagger>. In-memory data resets each run, so create, pay,
fulfil and refund within the same run.

## Endpoints

Shopper-scoped (JWT; act only on the caller's own data):

| Verb & route | Purpose |
|---|---|
| `POST /api/orders` | Place an order from catalog items → returns `orderId`. Awaiting payment. |
| `POST /api/orders/{orderId}/pay` | Authorize (hold) the order total with card details **or** a saved card id. No capture. |
| `POST /api/orders/{orderId}/refunds` | Refund a captured order, full/partial. Caller-supplied `idempotencyKey`. Returns `refundId`. (Owner or admin.) |
| `GET  /api/my-orders` | The caller's orders with payment state. |
| `POST /api/payment-methods` | Save a card → returns `paymentMethodId` + safe description (brand/last4/expiry). |
| `GET  /api/payment-methods` | The caller's saved cards. |
| `DELETE /api/payment-methods/{id}` | Remove a saved card (from PayPal's vault and this app). |

Operator-only (administrator role):

| Verb & route | Purpose |
|---|---|
| `POST /api/orders/{orderId}/fulfil` | Capture the held funds; renews a stale hold if needed. Records captured/fee/net. |
| `POST /api/orders/{orderId}/cancel` | Void the hold before fulfilment (releases funds). |
| `GET  /api/reconciliation?from={iso}&to={iso}` | PayPal's transactions over a range, lined up against eShop orders (paginated + 31-day chunked). |

## Design notes

- **Direct card, no browser.** Orders are created with `intent=AUTHORIZE` and
  `payment_source.card` (raw card) or `payment_source.token` (vaulted card). If PayPal ever
  answers with a payer-action/3-DS challenge, the API stops and reports it (HTTP 422) rather
  than building an approval round-trip.
- **Hold → take → return.** `pay` authorizes; `fulfil` captures
  (`/v2/payments/authorizations/{id}/capture`) and reads the fee/net breakdown; `cancel` voids;
  `refunds` refunds the capture. A stale hold is reauthorized before capture; if it can no
  longer be renewed the operator gets an actionable 409.
- **Idempotency.** `pay`/`fulfil`/`cancel` are idempotent in effect (state checks + a per-order
  lock + deterministic `PayPal-Request-Id`). Refunds dedupe on the caller's `idempotencyKey`
  (repeat → same `refundId`); distinct keys allow distinct partial refunds; a partly-refunded
  order never becomes refundable beyond what was captured.
- **Card safety.** Full card numbers are sent only to PayPal — never stored in this app's
  database and never logged. Saved cards keep only PayPal's vault token id and a safe
  description.
- **Ownership.** A shopper only ever sees, uses or deletes their own orders and saved cards
  (others return 404/not-listed).

## Schema

The `Order` aggregate gains `PaymentStatus`, an owned `PayPalPayment` (hold/capture ids,
statuses, captured/fee/net), and an owned `OrderRefund` collection. A new
`SavedPaymentMethod` aggregate holds vaulted-card references. An EF migration
(`AddPayPalPaymentsAndSavedCards`) is included for the SQL provider; the in-memory provider
used on this machine ignores migrations.
