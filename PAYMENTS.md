# Payments & Saved Cards (PayPal)

An additive capability on top of eShopOnWeb's catalog/basket/order flow: shoppers pay for
orders by card via **PayPal** (money held at checkout, taken at fulfilment, returned on refund),
and can **save a card** to reuse on later orders. All of it is exposed on **`src/PublicApi`**
(JWT-authenticated); the identity of the caller comes from the token.

## Endpoints

All routes are under `/api/`. Shopper endpoints act only on the caller's own data; operator
endpoints require the `Administrators` role.

| Method & route | Who | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog items (starts *awaiting payment*). Returns `orderId`. |
| `POST /api/orders/{orderId}/pay` | shopper | **Authorize** (hold) the order total with a one-off card **or** a saved card. |
| `POST /api/orders/{orderId}/fulfil` | operator | Fulfil and **capture** the held funds. Shows captured amount, PayPal fee, net. |
| `POST /api/orders/{orderId}/cancel` | operator | Cancel before fulfilment: **void** the hold; no money moves. |
| `POST /api/orders/{orderId}/refunds` | shopper | **Refund** a captured order (full or partial). Returns `refundId`. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from={iso}&to={iso}` | operator | PayPal's transactions for a range, lined up against eShop orders. |
| `POST /api/payment-methods` | shopper | Save (vault) a card. Returns `paymentMethodId` and a safe description. |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card. |

### Behaviour notes

- **Hold now, take later:** `pay` authorizes; `fulfil` captures. The authorized amount equals
  the order total to the cent.
- **Stale holds are renewed, not failed:** at fulfilment a hold past its expiry is
  re-authorized before capture. A hold that can no longer be renewed surfaces PayPal's own
  reason so an operator can act on it.
- **Idempotent by effect:** a double-submitted `pay`/`fulfil` never authorizes or captures
  twice (stable per-payment PayPal-Request-Id keys). Refunds take a **caller-supplied
  idempotency key** — repeating under the same key never refunds twice; two distinct partial
  refunds remain legitimate. A partial refund can never exceed what was captured.
- **Card data:** the full card number and CVC are never stored in the app's database and never
  written to logs. Saved cards keep only PayPal's vault token id plus brand + last four.

## Configuration

Bind from the `PayPal:` section (values come from configuration / user-secrets, never the repo):

| Key | From env var | Notes |
|---|---|---|
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` | REST client id |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` | REST secret |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` | e.g. `sandbox` |
| `PayPal:Currency` | `PAYPAL_CURRENCY` | e.g. `USD` |
| `PayPal:BaseUrl` | — | Optional. When set, used verbatim as the API base for **every** call, including the OAuth token request. |
| `PayPal:WireLog` | — | Optional `true` to log PayPal requests/responses (card number/CVC redacted). Off by default. |

Load the secrets into .NET user-secrets for `src/PublicApi` (never commit values):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

## Design

- Domain (`src/ApplicationCore`): the `Order` aggregate gains a `Payment` child holding the
  state PayPal owns (order/authorization/capture ids and statuses) and a `Refund` collection,
  with the state machine enforced there. Saved cards are a `PaymentMethod` aggregate. The
  PayPal boundary is the `IPayPalPaymentGateway` interface — the domain never sees the SDK.
- Infrastructure (`src/Infrastructure/PayPal`): `PayPalPaymentGateway` is the only class that
  uses the PayPal .NET SDK. It maps the app's intents to SDK calls and translates every failure
  into a caller-safe `PaymentGatewayException` with an appropriate HTTP status.
- API (`src/PublicApi`): one endpoint per action, following the project's `IEndpoint<>` style.
