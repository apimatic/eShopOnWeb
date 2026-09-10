# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Payments (PayPal) & saved cards

An additive capability that lets a shopper pay for an order with a card (processed by
**PayPal**) and reuse a **saved card** for later orders. It does not replace the existing
catalog/basket/order flow — it adds the money movement (hold at checkout, take at fulfilment,
give back on a return) and the operator actions around it. All endpoints are JWT-authenticated
and the caller's identity comes from the token.

### Endpoints

| Method & route | Role | Purpose |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog items. Returns `orderId`. Prices come from the catalog. |
| `POST /api/orders/{orderId}/pay` | shopper | Authorize (hold) the order total via PayPal, using card details **or** a saved card (`savedPaymentMethodId`). |
| `POST /api/orders/{orderId}/fulfil` | operator | Fulfil the order and capture the held funds. Records captured amount, PayPal fee and net proceeds. Renews a stale authorization automatically. |
| `POST /api/orders/{orderId}/cancel` | operator | Cancel before fulfilment; voids the hold so no money moves. |
| `POST /api/orders/{orderId}/refunds` | shopper | Refund a captured order, full or partial. Requires a caller-supplied `idempotencyKey`. Returns `refundId`. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from={ISO}&to={ISO}` | operator | PayPal's transactions for a date range lined up against eShop orders. Covers the whole range (31-day windows, all pages). |
| `POST /api/payment-methods` | shopper | Save a card. Returns `paymentMethodId` and a safe descriptor (brand, last four, expiry). |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card. |

Operator endpoints require the `Administrators` role. Every other endpoint acts only on the
caller's own orders and cards. Full card details are never stored in this app's database and
never logged.

### Configuration

Settings bind from the `PayPal:` configuration section (never hard-coded):

- `PayPal:ClientId`, `PayPal:ClientSecret` — REST app credentials of the sandbox business account.
- `PayPal:Environment` — `sandbox` or `live`.
- `PayPal:Currency` — settlement currency, e.g. `USD`.
- `PayPal:BaseUrl` — optional. When set it is used verbatim for **every** PayPal call
  (including the token request); otherwise the base is derived from `PayPal:Environment`.

Load the credentials into .NET user-secrets for the `PublicApi` project (values must never be
committed):

```bash
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"     --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET" --project src/PublicApi
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   --project src/PublicApi
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      --project src/PublicApi
```

### Design notes

- **PayPal APIs used:** Orders v2 (`intent=AUTHORIZE` with a card or a `vault_id`), Payments v2
  (capture / void / reauthorize / refund), Payment Method Tokens v3 (vault a card without a
  purchase, via setup-token → payment-token), and Transaction Search v1 (reconciliation).
- **Idempotency:** a repeated pay or fulfil never charges the shopper twice (existing-payment /
  existing-capture checks plus a per-run PayPal `PayPal-Request-Id`); a repeated refund under
  the same `idempotencyKey` returns the original refund. A partly-refunded order can never be
  refunded beyond what was captured.
- **Persistence:** payment state (PayPal order/authorization/capture/refund ids and status)
  lives in a `Payment` aggregate; saved cards in a `PaymentMethod` aggregate. With
  `UseOnlyInMemoryDatabase=true` these live only for the run.
