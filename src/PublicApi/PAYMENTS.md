# PayPal payments & saved cards (PublicApi)

An additive capability on top of the existing catalog/basket/order flow: collect money with
**PayPal** as the processor, and let a shopper **save a card** (PayPal Vault) to reuse on a
later order. All capabilities are JWT-authenticated HTTP endpoints on the **PublicApi**
project; the caller's identity comes from the token (username in the `Name` claim).

## Endpoints

| Method & route | Who | Purpose |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog items. Returns `orderId`. Starts *awaiting payment*. |
| `POST /api/orders/{orderId}/pay` | shopper | **Authorize** the order total (hold funds). Body carries card details **or** `savedPaymentMethodId`. |
| `POST /api/orders/{orderId}/fulfil` | admin | **Capture** the held funds. Shows PayPal's captured amount, fee, and net. Renews a stale hold if needed. |
| `POST /api/orders/{orderId}/cancel` | admin | **Void** the hold before fulfilment — no money moves. |
| `POST /api/orders/{orderId}/refunds` | shopper | **Refund** a captured payment, full or partial. Returns `refundId`. Idempotent per `idempotencyKey`. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from={iso}&to={iso}` | admin | PayPal's transaction record vs eShop orders over a date range. |
| `POST /api/payment-methods` | shopper | Vault a card. Returns `paymentMethodId` + a safe descriptor (never full card details). |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card (from PayPal's vault and this app). |

Admin = the `Administrators` role the project already uses. Every other endpoint is
shopper-scoped and acts only on the caller's own data. A shopper never sees, uses, or deletes
another shopper's order or saved card.

## Money flow (direct card, browser-less)

`create order (intent=AUTHORIZE)` → `authorize with card / vault_id` = hold → `capture` at
fulfil → `refund` / `void`. Amounts come from catalog prices; the currency comes from
`PayPal:Currency`. The authorized amount equals the order total to the cent.

- **Idempotency.** Pay/fulfil are idempotent in effect (an existing hold/capture is returned
  rather than repeated) and also carry a deterministic `PayPal-Request-Id`. Refunds are
  idempotent per caller-supplied `idempotencyKey`; two *distinct* partial refunds of the same
  capture are allowed, and total refunds can never exceed the captured amount.
- **Stale holds.** If a hold has lapsed by fulfilment, it is reauthorized and then captured;
  if it can no longer be renewed, the operator gets an actionable error.
- **3-D Secure / browser approval.** If PayPal asks for a browser approval, the call stops
  with a clear error (`422`) — this integration is browser-less by design.

## Configuration

Bound from the `PayPal:` configuration section (values via configuration / **user-secrets**,
never hard-coded, never committed):

| Key | Source env var | Notes |
| --- | --- | --- |
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` | REST app client id (sandbox business account). |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` | REST app secret. |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` | `sandbox` or `live`/`production`. |
| `PayPal:Currency` | `PAYPAL_CURRENCY` | ISO-4217 currency for all charges. |
| `PayPal:BaseUrl` | — | Optional. When set, used verbatim as the base address for **every** PayPal call (incl. the OAuth token request); otherwise derived from `Environment`. |

Load them into user-secrets for the PublicApi project:

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

## Design / layout

- **ApplicationCore** — `Order` gains an optional owned `OrderPayment` (with an owned
  `PaymentRefund` collection); new `SavedPaymentMethod` aggregate; `IPayPalGateway` abstraction
  and result types under `Payments/`; application services `OrderPaymentService`,
  `SavedCardService`, `ReconciliationService`; specifications and payment exceptions.
- **Infrastructure** — `PayPalGateway` (typed `HttpClient`) implements the gateway: OAuth
  token caching, idempotency/`Prefer` headers, error translation (surfacing PayPal's
  `debug_id`), and reconciliation date-range chunking (≤31-day windows) + full pagination.
  EF config maps the owned payment; a migration adds the SQL schema. Registered via
  `services.AddPayPalPaymentServices(configuration)`.
- **PublicApi** — one endpoint per action (MinimalApi.Endpoint `IEndpoint`), grouped under
  `OrderPaymentEndpoints/`, `PaymentMethodEndpoints/`, `ReconciliationEndpoints/`; view models
  under `PaymentModels/`. Domain exceptions map to HTTP status codes in `ExceptionMiddleware`.

Card data is forwarded to PayPal only; it is never stored in this app's database and never
written to logs.
