# Order notifications by SMS (Twilio) — verification guide

An additive capability on the eShopOnWeb **PublicApi** host: shoppers put a mobile number on file,
the shop texts them as an order moves (placed → dispatched → cancelled), a delivery follow-up is
scheduled with the provider a few days after dispatch and called off if the order is cancelled, and
operators can resend, dispose of message content, and reconcile against the provider's own records.

All Twilio interaction goes through the **AsadAli.TwilioSdk** plugin SDK. The catalog/basket/order
flow is unchanged; the existing `Order`/`OrderItem` model is reused.

## What was added

- **Domain** (`src/ApplicationCore`): `ContactNumber`, `OrderNotification` (with provider SID +
  delivery status), an `OrderStatus` on `Order` (Placed/Dispatched/Cancelled), the
  `ITwilioMessagingGateway` seam, and the `ContactNumberService` / `OrderNotificationService`
  orchestration.
- **Infrastructure** (`src/Infrastructure/Twilio`): `TwilioMessagingGateway` (the only code that
  calls the SDK), `TwilioSettings`, and DI wiring. EF stores for the two new entities.
- **PublicApi** (`src/PublicApi`): the HTTP endpoints below, plus exception→status mapping.

## Endpoints

| Method & route | Who | Purpose |
|---|---|---|
| `POST /api/contact-numbers` | shopper | Register a number (provider-validated; canonical E.164 stored). Returns `contactNumberId`. |
| `GET /api/contact-numbers` | shopper | The caller's numbers. |
| `DELETE /api/contact-numbers/{contactNumberId}` | shopper | Remove one of the caller's numbers. |
| `POST /api/orders` | shopper | Place an order from catalog items. Returns `orderId`. Texts "order placed". |
| `POST /api/orders/{orderId}/dispatch` | operator | Text "on its way" + schedule the delivery follow-up. |
| `POST /api/orders/{orderId}/cancel` | operator | Text "cancelled" + call off a not-yet-sent follow-up. |
| `GET /api/my-orders` | shopper | The caller's orders, each with its notifications' outcomes. |
| `GET /api/orders/{orderId}/notifications` | owner/operator | Notifications for an order; each carries `notificationId`. |
| `POST /api/notifications/{notificationId}/resend` | operator | Resend (idempotency key in body). Returns the new `notificationId`. |
| `DELETE /api/notifications/{notificationId}/content` | operator | Redact the message body at the provider; the record survives. |
| `GET /api/notifications/reconciliation?from={iso}&to={iso}` | operator | Provider records (from `Twilio:FromNumber`) vs eShop's, over the range. |

Operator = the `Administrators` role. Shopper endpoints act only on the caller's own data.

## Configuration

Settings bind from the `Twilio:` section: `AccountSid`, `AuthToken`, `FromNumber`,
`MessagingServiceSid`, and optional `BaseUrl` (a messaging-API host override; the lookup API keeps
its own host). **No secret values live in the repo** — load them into .NET user-secrets:

```bash
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
# Twilio:BaseUrl is optional; leave unset to use the provider default messaging host.
```

## Run the API

This machine has the ASP.NET Core 8 runtime but only the .NET 10 SDK, and no LocalDB — so roll the
SDK forward and use the in-memory store. Bind to the assigned port block (9563/9564 here).

```bash
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="https://localhost:9563;http://localhost:9564"
export UseOnlyInMemoryDatabase=true          # also set in appsettings.Development.json
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

> In-memory note: the store is per-run and per-host. Drive the whole flow through PublicApi in one
> run (dispatch/cancel the orders you created in that same run).

Swagger is at `https://localhost:9563/swagger`.

## Drive the flows (curl)

Get tokens (default seeded users; password `Pass@word1`):

```bash
B=https://localhost:9563/api
ST=$(curl -sk -X POST $B/authenticate -H "Content-Type: application/json" \
     -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)   # shopper
AT=$(curl -sk -X POST $B/authenticate -H "Content-Type: application/json" \
     -d '{"username":"admin@microsoft.com","password":"Pass@word1"}'   | jq -r .token)   # operator
```

**Only ever text the two provided destinations** — `TWILIO_TEST_TO_NUMBER` (Canadian, really
delivers) and `TWILIO_UNREACHABLE_TO_NUMBER` (US, carrier refuses). Never any other real number.

**Flow 1 — contact number** (deliverable path):

```bash
curl -sk -X POST $B/contact-numbers -H "Authorization: Bearer $ST" -H "Content-Type: application/json" \
     -d "{\"phoneNumber\":\"$TWILIO_TEST_TO_NUMBER\"}"          # -> 201 {contactNumberId, phoneNumber(E.164)}
curl -sk $B/contact-numbers -H "Authorization: Bearer $ST"      # -> the caller's numbers
curl -sk -X POST $B/contact-numbers -H "Authorization: Bearer $ST" -H "Content-Type: application/json" \
     -d '{"phoneNumber":"+1555"}'                               # -> 400 (provider rejects it)
```

**Flow 2 — order lifecycle** (real, deliverable messages to the Canadian number):

```bash
OID=$(curl -sk -X POST $B/orders -H "Authorization: Bearer $ST" -H "Content-Type: application/json" \
      -d '{"items":[{"catalogItemId":5,"quantity":2}]}' | jq -r .orderId)     # texts "placed"
curl -sk $B/orders/$OID/notifications -H "Authorization: Bearer $ST"          # OrderPlaced -> delivered
curl -sk -X POST $B/orders/$OID/dispatch -H "Authorization: Bearer $AT"       # texts "on its way" + schedules follow-up
curl -sk $B/orders/$OID/notifications -H "Authorization: Bearer $ST"          # DeliveryFollowUp -> scheduled (has SID + sendAt)
curl -sk -X POST $B/orders/$OID/cancel   -H "Authorization: Bearer $AT"       # texts "cancelled" + cancels the follow-up
curl -sk $B/orders/$OID/notifications -H "Authorization: Bearer $ST"          # DeliveryFollowUp -> canceled
curl -sk $B/my-orders -H "Authorization: Bearer $ST"                          # order + all notification outcomes
```

**Flow 3 — operator actions.** Produce an undeliverable message with the US number, then resend:

```bash
# (register the US number for a caller, place an order to it — it goes 'undelivered' with an error code)
curl -sk -X POST $B/contact-numbers -H "Authorization: Bearer $AT" -H "Content-Type: application/json" \
     -d "{\"phoneNumber\":\"$TWILIO_UNREACHABLE_TO_NUMBER\"}"
OID2=$(curl -sk -X POST $B/orders -H "Authorization: Bearer $AT" -H "Content-Type: application/json" \
       -d '{"items":[{"catalogItemId":4,"quantity":1}]}' | jq -r .orderId)
NID=$(curl -sk $B/orders/$OID2/notifications -H "Authorization: Bearer $AT" | jq -r '.[0].notificationId')
# ... poll that notification until status is 'undelivered'

# Resend with a key — repeating the SAME key does NOT send again; a FRESH key does.
curl -sk -X POST $B/notifications/$NID/resend -H "Authorization: Bearer $AT" -H "Content-Type: application/json" -d '{"idempotencyKey":"A"}'  # -> {notificationId: X}
curl -sk -X POST $B/notifications/$NID/resend -H "Authorization: Bearer $AT" -H "Content-Type: application/json" -d '{"idempotencyKey":"A"}'  # -> same X (no new message)
curl -sk -X POST $B/notifications/$NID/resend -H "Authorization: Bearer $AT" -H "Content-Type: application/json" -d '{"idempotencyKey":"B"}'  # -> new id

# Dispose of a message's content at the provider (record survives).
curl -sk -X DELETE $B/notifications/1/content -H "Authorization: Bearer $AT"                    # -> 204

# Reconcile the provider's record (from Twilio:FromNumber) against eShop's, over a range with data.
FROM=$(date -u -d '-1 hour' +%Y-%m-%dT%H:%M:%SZ); TO=$(date -u -d '+5 min' +%Y-%m-%dT%H:%M:%SZ)
curl -sk "$B/notifications/reconciliation?from=$FROM&to=$TO" -H "Authorization: Bearer $AT"
```

The reconciliation report has `matched` (both sides agree, by SID), `providerOnly` (the provider
knows a message eShop doesn't), and `eShopOnly` (eShop holds one the provider didn't report in the
range — e.g. a scheduled/cancelled follow-up that was never sent).

## What was verified against the live account

- A real order-placed / dispatched / cancelled SMS to the Canadian number reached it (`delivered`).
- The delivery follow-up was really scheduled with the provider, then **canceled before it went out**
  when the order was cancelled.
- A message to the US number came back `undelivered` (carrier refusal, error 30034) — handled as an
  outcome, not a failure.
- Operator resend is idempotent per key; a fresh key sends again.
- Content disposal redacts the body at the provider while the record (SID + status) survives.
- Reconciliation over a populated range matched the provider's records to eShop's by SID.
- Phone numbers and the auth token appear in **no** log line.

Design notes: a messaging failure never fails the order operation (the order is still placed /
dispatched / cancelled); a shopper with no number on file is simply not messaged; one shopper can
never see, use, or delete another's numbers or orders.
