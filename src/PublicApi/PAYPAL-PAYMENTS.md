# PayPal payments & saved cards (PublicApi)

Additive capability on top of the existing eShopOnWeb catalog/basket/order flow: a shopper can pay for an
order by card and reuse a saved card; an operator fulfils, cancels, refunds and reconciles. Every capability
is an HTTP endpoint on **PublicApi** (JWT; identity comes from the token). The catalog/basket/checkout flow
is untouched.

## Architecture

- **Domain (`ApplicationCore`)**
  - `OrderAggregate/Payment` (aggregate root, one per order) carries the state PayPal owns: the hold
    (authorization), the capture (amount / PayPal fee / net proceeds) and each `PaymentRefund`.
  - `Order` gains additive `Status` (`OrderStatus`) and a stable `PaymentReference` (seeds idempotency keys
    and the PayPal invoice id). The order/order-item model is reused, not duplicated.
  - `PaymentMethodAggregate/SavedPaymentMethod` holds a vaulted card's token plus **safe** descriptors
    (brand, last four, expiry) — never the PAN/CVV.
  - Ports: `IPayPalClient` (+ DTOs), `IOrderPaymentService`, `IPaymentMethodService`, `IReconciliationService`.
- **Infrastructure**
  - `PayPal/PayPalClient` talks to PayPal REST directly — Orders v2 (authorize), Payments v2
    (capture / reauthorize / void / refund), Vault v3 (save / delete card), Transaction Search v1
    (reconciliation). OAuth token is cached and refreshed proactively. Card details flow through here only in
    memory and are never logged.
- **PublicApi** — one endpoint per action under `PaymentEndpoints/`, following the project's `IEndpoint`
  convention. Errors map to status codes via `ExceptionMiddleware`.

## Endpoints

| Method & route | Role | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog items. Returns **`orderId`**. Starts awaiting payment. |
| `POST /api/orders/{orderId}/pay` | shopper | Authorize (hold) the total with a one-off card **or** `savedPaymentMethodId`. |
| `POST /api/orders/{orderId}/fulfil` | operator | Capture the hold. Shows captured amount, PayPal fee, net. Renews a stale hold. |
| `POST /api/orders/{orderId}/cancel` | operator | Void the hold before fulfilment; no money moves. |
| `POST /api/orders/{orderId}/refunds` | shopper | Refund the capture, full or partial. Returns **`refundId`**. Body carries `idempotencyKey`. |
| `GET  /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET  /api/reconciliation?from=&to=` | operator | PayPal's transactions lined up against eShop orders over the whole range. |
| `POST /api/payment-methods` | shopper | Save a card. Returns **`paymentMethodId`** + safe descriptors. |
| `GET  /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card (also from PayPal's vault). |

Shopper endpoints act only on the caller's own data. Operator endpoints require the `Administrators` role.

## Money movement & guarantees

- **Hold at pay, take at fulfil, release at cancel, return at refund.** The hold equals the order total to
  the cent.
- **Idempotent in effect.** A double-click on pay/fulfil never charges twice (state guards + a stable,
  instrument-scoped `PayPal-Request-Id`). Refunds dedupe on the caller's `idempotencyKey`; two *distinct*
  partial refunds remain legitimate. A refund can never exceed what was captured.
- **Stale authorizations** are reauthorized before capture; one that can no longer be renewed returns a `422`
  with an operator-actionable message.

## Configuration

Bound from the `PayPal:` section — supply via user-secrets / environment, never hard-coded:

`PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment` (`sandbox`/`live`), `PayPal:Currency`,
and optional `PayPal:BaseUrl` (used verbatim for every call, including the token request, when set).

Load secrets (values read from the named environment variables — nothing is written into the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
# optional: dotnet user-secrets set "PayPal:BaseUrl" "$PAYPAL_BASEURL"
```

## Run (this machine)

Only the .NET 10 SDK / ASP.NET Core 10 runtime is present and there is no LocalDB, so roll forward and use the
in-memory store (data survives only within one run — pay, fulfil and refund within the same run):

```bash
export DOTNET_ROLL_FORWARD=Major
cd src/PublicApi
ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true \
  ASPNETCORE_URLS="https://localhost:10143;http://localhost:10144" \
  dotnet run
```

Get a bearer token from `POST /api/authenticate` (seed users: `demouser@microsoft.com` shopper,
`admin@microsoft.com` operator; password `Pass@word1`). Verify with the sandbox Visa `4111 1111 1111 1111`,
any future expiry, any CVC.
