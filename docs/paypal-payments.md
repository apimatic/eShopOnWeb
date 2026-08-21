# PayPal payments & saved cards (PublicApi)

An **additive** capability on top of eShopOnWeb: it lets a shopper pay for an order with a card
(or a saved card) via **PayPal**, and lets an operator fulfil, cancel, refund and reconcile. It
reuses the existing `Order`/`OrderItem` model and does not change the catalog/basket/order flow.

All endpoints live on **`src/PublicApi`** (JWT-authenticated) under `/api/`. The caller's identity
comes from the token. Fulfil, cancel and reconciliation are **administrator-only**; every other
endpoint is scoped to the calling shopper's own data.

## The money model

The lifecycle mirrors what PayPal owns, tracked on an `OrderPayment` (one per order, keyed by
`OrderId`, separate from the `Order` aggregate so the change is additive):

`AwaitingPayment → Authorized (hold) → Captured (money taken at fulfilment)`, with
`Voided` (cancel before fulfilment) and `PartiallyRefunded`/`Refunded` (return after fulfilment).

- **Pay** places a *hold* (PayPal create-order `intent=AUTHORIZE` + authorization) — money is not taken.
- **Fulfil** *captures* the hold — that is when the money moves; the payment then records PayPal's
  captured amount, fee and net proceeds (`seller_receivable_breakdown`). A stale hold is renewed
  (re-authorized) before capture; one that can no longer be renewed surfaces PayPal's reason to the operator.
- **Cancel** *voids* the hold before fulfilment.
- **Refund** returns a captured payment, in full or in part; a partly-refunded order can never be
  refunded beyond what was captured.

Payment operations are **idempotent in effect** (a double-click never authorizes or captures twice);
refunds take a caller-supplied idempotency key. Full card numbers are never stored in the app database
and never written to logs — only PayPal's ids/tokens and safe descriptors (brand, last four, expiry).

## Endpoints

| Method & route | Who | Purpose | Top-level id |
|---|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog items | `orderId` |
| `POST /api/orders/{orderId}/pay` | shopper | Authorize (hold) with a card or a saved card | |
| `POST /api/orders/{orderId}/fulfil` | admin | Capture at fulfilment | |
| `POST /api/orders/{orderId}/cancel` | admin | Void the hold before fulfilment | |
| `POST /api/orders/{orderId}/refunds` | shopper | Refund a capture (full/partial, idempotency key) | `refundId` |
| `GET /api/my-orders` | shopper | The caller's orders + payment state | |
| `GET /api/reconciliation?from=&to=` | admin | PayPal transactions (ISO-8601 range, paged) vs eShop orders | |
| `POST /api/payment-methods` | shopper | Save (vault) a card | `paymentMethodId` |
| `GET /api/payment-methods` | shopper | The caller's saved cards | |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card | |

## Configuration

Settings bind from the `PayPal:` section (no values are hard-coded — the same build runs against any
account). The credentials come from user-secrets / environment, never from a file in the repo:

| Key | From env var | Notes |
|---|---|---|
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` | REST client id |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` | REST secret |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` | e.g. `sandbox` |
| `PayPal:Currency` | `PAYPAL_CURRENCY` | e.g. `USD` |
| `PayPal:BaseUrl` | `PAYPAL_BASE_URL` | optional; when set, used verbatim as the API base for **every** call, including the OAuth token request |

On startup `Program.cs` maps any present `PAYPAL_*` environment variable onto its `PayPal:*` key
in-memory (no secret is written to disk); user-secrets remain a fallback. `PayPal:WireLog=true` enables
diagnostic logging of PayPal **responses** (never request bodies, so no card data is logged).

## Implementation layout

- **ApplicationCore** — `OrderPayment`/`PaymentRefund`/`SavedPaymentMethod` entities; the SDK-agnostic
  `IPayPalPaymentGateway` + result records; `OrderPaymentService`, `SavedCardService`,
  `ReconciliationService`; specifications; `PaymentException` types.
- **Infrastructure/PayPal** — `PayPalPaymentGateway` (the only code that talks to the PayPal .NET SDK,
  `AsadAli.Checkout.Sdk`), `CurrencyFormatter`, and `AddPayPalIntegration` DI wiring.
- **PublicApi** — `OrderPaymentEndpoints/` and `PaymentMethodEndpoints/` (MinimalApi.Endpoint style);
  `ExceptionMiddleware` maps `PaymentException` types to HTTP status codes.

The PayPal .NET SDK is the sole reference for talking to PayPal, per the integration contract in
`paypal-plan.md`.
