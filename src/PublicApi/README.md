# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments & saved cards

The PublicApi additionally exposes PayPal-backed payment processing for one-time orders and saved
cards. This is **additive** — the existing Catalog → Basket → Order flow is unchanged. All endpoints
are JWT-authenticated; the caller's identity (and therefore order/card ownership) comes from the token.

| Method & route | Purpose |
| --- | --- |
| `POST /api/orders` | Place an order from catalog item ids + quantities. Returns `orderId`. Starts *AwaitingPayment*. |
| `POST /api/orders/{orderId}/pay` | Pay with PayPal using card details **or** `savedPaymentMethodId`. Idempotent. |
| `POST /api/orders/{orderId}/refunds` | Full refund of the order's payment. Idempotent. |
| `GET /api/my-orders` | The caller's orders with payment state. |
| `POST /api/payment-methods` | Save a card (vaulted with PayPal). Returns `paymentMethodId` + a safe descriptor. |
| `GET /api/payment-methods` | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | Remove a saved card (revokes the PayPal vault token). |

Amounts come from catalog prices in USD. Payment and refund are idempotent in effect — a double-click
never produces a double charge or refund. Full card details are sent only to PayPal; they are never
stored in this app's database or written to logs. Saved cards are strictly scoped to their owner.

### Design

* PayPal is consumed through the OpenAPI specs under `api-specs/paypal/` (Checkout Orders v2 for the
  single-step create-with-card capture, Payments v2 for refunds, Vault Payment Tokens v3 for saved
  cards). The gateway is abstracted behind `IPayPalGateway` (ApplicationCore); the spec-faithful HTTP
  client lives in `Infrastructure/PayPal`.
* Endpoints are thin and delegate to `IOrderPaymentService` / `IPaymentMethodService`.

### Configuration

Bind PayPal settings from the `PayPal` configuration section — **never commit the values**:

| Config key | Environment variable |
| --- | --- |
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` (`sandbox`) |
| `PayPal:BaseUrl` | *(optional override; when set, used verbatim as the API base address)* |

Load the credentials into .NET user-secrets (kept outside the repo):

```bash
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"     --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET" --project src/PublicApi
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   --project src/PublicApi
```

