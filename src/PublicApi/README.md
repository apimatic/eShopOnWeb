# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Finix card payments

All payment endpoints live in `src/PublicApi`, require a JWT bearer token, and are routed under `/api/`.

| Method | Route | Who | Purpose |
| --- | --- | --- | --- |
| POST | `/api/orders` | shopper | Places an order from catalog item ids and quantities; returns `orderId`. |
| POST | `/api/orders/{orderId}/pay` | shopper (owner) | Authorizes (holds) the order total with a saved card (`paymentMethodId`) or one-off `card` details. |
| POST | `/api/orders/{orderId}/fulfil` | administrator | Captures the held amount; records Finix captured amount, fee and net proceeds. |
| POST | `/api/orders/{orderId}/cancel` | administrator | Voids the hold before fulfilment; no money moves. |
| POST | `/api/orders/{orderId}/refunds` | shopper (owner) or administrator | Refunds a captured payment, fully or partly; `idempotencyKey` prevents double refunds. |
| GET | `/api/my-orders` | shopper | The caller's orders with payment state. |
| GET | `/api/reconciliation?from=...&to=...` | administrator | Finix transfers for the range, matched against eShop orders (all pages). |
| POST | `/api/payment-methods` | shopper | Saves a card; returns `paymentMethodId`. |
| GET | `/api/payment-methods` | shopper | The caller's saved cards. |
| DELETE | `/api/payment-methods/{paymentMethodId}` | shopper (owner) | Removes a saved card and disables it at Finix. |

### Configuration

Settings are bound from the `Finix` section using exactly these keys:
`Finix:Username`, `Finix:Password`, `Finix:MerchantId`, `Finix:Environment`,
`Finix:Currency`, and optional `Finix:BaseUrl`. The values are supplied by the
`FINIX_USERNAME`, `FINIX_PASSWORD`, `FINIX_MERCHANT_ID`, `FINIX_ENVIRONMENT`,
and `FINIX_CURRENCY` environment variables (or .NET user-secrets); no values are
committed to the repository.

The Finix OpenAPI document in `finix-spec/finix-openapi.yaml` is the
authoritative contract for every Finix call. The client is hand-written against
that spec; no third-party Finix SDK is used.

### Verification

1. Start `PublicApi` with the in-memory database and the assigned port block.
2. Get bearer tokens from `POST /api/authenticate` (shopper and administrator).
3. Save a card with `POST /api/payment-methods`, then place an order with
   `POST /api/orders`.
4. Pay with the saved card or one-off card details; confirm the hold equals the
   order total.
5. Fulfil as administrator; confirm the captured amount, Finix fee and net.
6. Refund partly, repeat with the same idempotency key, then refund the rest.
7. Run `GET /api/reconciliation` for a range that contains the transactions.
8. Delete the saved card and confirm it can no longer be used to pay.
