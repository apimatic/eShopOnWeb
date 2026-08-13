# Order Notifications by SMS (Twilio)

An additive capability on `src/PublicApi` that keeps shoppers informed by text message as their
orders progress, using **Twilio** as the messaging provider. It does not replace the existing
catalog/basket/order flow.

Every Twilio interaction is built against the OpenAPI documents in `api-specs/`:

- **`twilio_api_v2010`** — the classic Message resource (`/2010-04-01/Accounts/{AccountSid}/Messages...`):
  send, fetch, list (reconciliation), update-to-cancel a scheduled message, and update-to-redact a
  message body. Auth is HTTP Basic (`AccountSid:AuthToken`).
- **`twilio_lookups_v2`** — `/v2/PhoneNumbers/{PhoneNumber}` for validating a number and getting its
  canonical E.164 form. Lookups is a different host and is **not** governed by `Twilio:BaseUrl`.

No pre-built Twilio SDK is used; the client is hand-written against those specs
(`src/Infrastructure/Twilio`).

## Endpoints

All under `/api/`, JWT-authenticated; the caller's identity comes from the token.

| Method & route | Who | What |
| --- | --- | --- |
| `POST /api/contact-numbers` | shopper | Register a mobile number (validated + canonicalised via Lookups). Returns `contactNumberId`. |
| `GET /api/contact-numbers` | shopper | The caller's registered numbers. |
| `DELETE /api/contact-numbers/{contactNumberId}` | shopper | Remove one of the caller's numbers. |
| `POST /api/orders` | shopper | Place an order from catalog item ids + quantities. Returns `orderId`. Sends "order placed". |
| `POST /api/orders/{orderId}/dispatch` | operator | Mark dispatched; send "on its way"; schedule a delivery follow-up with the provider. |
| `POST /api/orders/{orderId}/cancel` | operator | Cancel; send "cancelled"; call off the scheduled follow-up before it goes out. |
| `GET /api/my-orders` | shopper | The caller's orders, each with its notifications. |
| `GET /api/orders/{orderId}/notifications` | shopper | What was sent for this order and what became of each message (live-refreshed). Each entry carries `notificationId`. |
| `POST /api/notifications/{notificationId}/resend` | operator | Re-send a message. Requires an `Idempotency-Key` header. Returns the produced `notificationId`. |
| `DELETE /api/notifications/{notificationId}/content` | operator | Dispose of a message's content at the provider and here; the record survives. |
| `GET /api/notifications/reconciliation?from=&to=` | operator | Line up the provider's record of the sending number's messages against eShop's, over an ISO-8601 range. |

Operator endpoints require the `Administrators` role. Shopper endpoints act only on the caller's own
data. Phone numbers are never logged; API responses mask destination numbers (except a shopper's own
`GET /api/contact-numbers`).

## Configuration (`Twilio:` section)

Bind from configuration; never hard-code values. Load them into .NET user-secrets:

```bash
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
# Twilio:BaseUrl is optional — an override for the messaging API base address only.
```

`Twilio:DeliveryFollowUpDelayDays` (optional, default 3) controls how far out the follow-up is scheduled.

## Running

The machine has only the .NET 10 SDK and no LocalDB, so:

```bash
export DOTNET_ROLL_FORWARD=Major
cd <repo root>
dotnet run --project src/PublicApi/PublicApi.csproj \
  -e UseOnlyInMemoryDatabase=true -e ASPNETCORE_ENVIRONMENT=Development
```

> **In-memory note:** the in-memory provider ignores EF migrations and loses data on restart, so orders,
> contact numbers and notifications only survive within a single run — dispatch/cancel the orders you
> created in that same run. Because Web and PublicApi hold isolated in-memory stores, the notification
> flow is driven entirely through PublicApi (that is why `POST /api/orders` exists). For a real database,
> add an EF migration for the new `Status` column on `Order` and the `ContactNumbers` / `OrderNotifications`
> tables.

## Verify it yourself

With the server running on `https://localhost:10463` (use `curl -k` for the dev cert):

```bash
# 1. Get an admin token (admin is both a shopper and an operator here)
TOKEN=$(curl -sk -X POST https://localhost:10463/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | sed -E 's/.*"token":"([^"]+)".*/\1/')

# 2. Register the reachable (Canadian) test number — real Lookups call, stores canonical form
curl -sk -X POST https://localhost:10463/api/contact-numbers \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "{\"phoneNumber\":\"$TWILIO_TEST_TO_NUMBER\"}"

# 3. Place an order (sends a real "order placed" SMS)
curl -sk -X POST https://localhost:10463/api/orders \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":1,"quantity":2}]}'         # -> {"orderId":1,...}

# 4. See the message reach the phone (providerStatus becomes "delivered")
curl -sk https://localhost:10463/api/orders/1/notifications -H "Authorization: Bearer $TOKEN"

# 5. Dispatch: sends "on its way" and SCHEDULES the follow-up (providerStatus "scheduled")
curl -sk -X POST https://localhost:10463/api/orders/1/dispatch -H "Authorization: Bearer $TOKEN"
curl -sk https://localhost:10463/api/orders/1/notifications  -H "Authorization: Bearer $TOKEN"

# 6. Cancel: the scheduled follow-up flips to "canceled" (called off before it goes out)
curl -sk -X POST https://localhost:10463/api/orders/1/cancel  -H "Authorization: Bearer $TOKEN"
curl -sk https://localhost:10463/api/orders/1/notifications  -H "Authorization: Bearer $TOKEN"

# 7. Operator re-send with an idempotency key (repeat = no second send; fresh key = new send)
curl -sk -X POST https://localhost:10463/api/notifications/2/resend \
  -H "Authorization: Bearer $TOKEN" -H "Idempotency-Key: demo-1"

# 8. Dispose of a message's content (body removed at Twilio; record + outcome survive)
curl -sk -X DELETE https://localhost:10463/api/notifications/1/content -H "Authorization: Bearer $TOKEN"

# 9. Reconciliation over today (counts only the configured FromNumber's messages)
FROM=$(date -u +%Y-%m-%dT00:00:00Z); TO=$(date -u +%Y-%m-%dT23:59:59Z)
curl -sk "https://localhost:10463/api/notifications/reconciliation?from=$FROM&to=$TO" \
  -H "Authorization: Bearer $TOKEN"
```

`TWILIO_UNREACHABLE_TO_NUMBER` (a reserved US number) is accepted by the API but the carrier refuses it;
its notification ends up `undelivered` with a provider error code — an expected outcome of the live
account's registration status, not a defect. The order is still placed regardless.
