# PayPal Integration Verification Guide

## Prerequisites

Set the following environment variables before starting the API:

```
PayPal__ClientId=<your-sandbox-client-id>
PayPal__ClientSecret=<your-sandbox-client-secret>
PayPal__Environment=sandbox
UseOnlyInMemoryDatabase=true
ASPNETCORE_URLS=https://localhost:16743;http://localhost:16744
DOTNET_ROLL_FORWARD=Major
```

## Start the API

```
cd src/PublicApi
dotnet run -c Release
```

## Sandbox card

`4111 1111 1111 1111` — expiry any future date (e.g. `2027-02`), CVV any 3 digits

---

## Flow 1 — Pay for an order (one-off card)

### 1. Authenticate as shopper

```
POST /api/authenticate
{ "username": "demouser@microsoft.com", "password": "Pass@word1" }
```

Save the returned `token` as `SHOPPER_TOKEN`.

### 2. Place an order

```
POST /api/orders
Authorization: Bearer <SHOPPER_TOKEN>
{
  "items": [{ "catalogItemId": 1, "quantity": 1 }],
  "street": "123 Main St", "city": "Redmond", "state": "WA",
  "country": "US", "zipCode": "98052"
}
```

Response: `{ "orderId": 1 }` — save as `ORDER_ID`.

### 3. Pay with a one-off card

```
POST /api/orders/{ORDER_ID}/pay
Authorization: Bearer <SHOPPER_TOKEN>
{
  "cardNumber": "4111111111111111",
  "cardExpiry": "2027-02",
  "cardCvv": "123",
  "cardName": "Demo User",
  "billingCountry": "US"
}
```

Response: `{ "status": "Authorized", "authorizationId": "..." }`

### 4. View my orders

```
GET /api/my-orders
Authorization: Bearer <SHOPPER_TOKEN>
```

Response: array of orders with `paymentStatus: "Authorized"`.

### 5. Authenticate as admin

```
POST /api/authenticate
{ "username": "admin@microsoft.com", "password": "Pass@word1" }
```

Save as `ADMIN_TOKEN`.

### 6. Fulfil (capture funds)

```
POST /api/orders/{ORDER_ID}/fulfil
Authorization: Bearer <ADMIN_TOKEN>
```

Response: `{ "status": "Captured", "captureId": "...", "capturedAmount": ..., "payPalFee": ..., "netAmount": ... }`

### 7. Partial refund

Use a unique idempotency key each time:

```
POST /api/orders/{ORDER_ID}/refunds
Authorization: Bearer <ADMIN_TOKEN>
{
  "amount": 5.00,
  "currency": "USD",
  "idempotencyKey": "refund-unique-key-001"
}
```

Response: `{ "refundId": 1, "payPalRefundId": "...", "amount": 5.00, "status": "COMPLETED" }`

Repeat with the **same key** — returns the same `refundId` without calling PayPal again (idempotency).

### 8. Cancel an order (only works for Authorized, not yet Captured)

Place a second order and pay it, then:

```
POST /api/orders/{ORDER_ID_2}/cancel
Authorization: Bearer <ADMIN_TOKEN>
```

Response: `{ "status": "Voided" }`

---

## Flow 2 — Saved cards

### 1. Save a card

```
POST /api/payment-methods
Authorization: Bearer <SHOPPER_TOKEN>
{
  "cardNumber": "4111111111111111",
  "cardExpiry": "2027-02",
  "cardCvv": "123",
  "cardName": "Demo User",
  "billingCountry": "US"
}
```

Response: `{ "paymentMethodId": 1, "last4": "1111", "brand": "VISA", "expiry": "2027-02" }`

### 2. List saved cards

```
GET /api/payment-methods
Authorization: Bearer <SHOPPER_TOKEN>
```

### 3. Place order and pay with saved card

Place a new order (Step 2 above), then:

```
POST /api/orders/{ORDER_ID}/pay
Authorization: Bearer <SHOPPER_TOKEN>
{ "savedCardId": 1 }
```

Response: `{ "status": "Authorized", "authorizationId": "..." }`

### 4. Delete saved card

```
DELETE /api/payment-methods/1
Authorization: Bearer <SHOPPER_TOKEN>
```

Response: `204 No Content`

List cards again — returns empty array.

---

## Reconciliation report

```
GET /api/reconciliation?from=2026-08-01T00:00:00Z&to=2026-08-31T23:59:59Z
Authorization: Bearer <ADMIN_TOKEN>
```

Response:
```json
{
  "from": "...",
  "to": "...",
  "matched": [...],
  "eShopOnly": [...],
  "paypalOnly": [...]
}
```

Note: PayPal transaction search has a short settlement delay (1-5 minutes). A freshly captured order may appear in `eShopOnly` until the transaction settles in PayPal's reporting API.

---

## Idempotency notes

- `POST /api/orders/{id}/pay` — safe to retry; returns existing authorization if already authorized.
- `POST /api/orders/{id}/fulfil` — safe to retry; returns existing capture if already captured.
- `POST /api/orders/{id}/cancel` — safe to retry; returns success if already voided.
- `POST /api/orders/{id}/refunds` — caller supplies `idempotencyKey`; same key returns the same refund record without a second PayPal call.
