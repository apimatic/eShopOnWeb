# SMS Order Notifications (Twilio)

Keeps eShopOnWeb shoppers informed by text message as their orders progress, using **Twilio** as
the messaging provider. Additive to the existing catalog/basket/order flow. All capabilities are
HTTP endpoints on **`src/PublicApi`** (JWT-authenticated), routed under `/api/`.

## What was added

| Layer | Additions |
|---|---|
| **ApplicationCore** | `OrderStatus` + `Order.MarkDispatched()/MarkCancelled()`; `ContactNumber` and `OrderNotification` aggregates; `ISmsProvider` + `IOrderNotificationService` (with `OrderNotificationService`); specifications; message composer. |
| **Infrastructure** | `TwilioSmsProvider` (raw HTTP: form-encoded requests, JSON responses, HTTP Basic auth); `TwilioOptions`; EF config + `CatalogContext` DbSets. |
| **PublicApi** | Endpoints for all three flows; DI wiring; `Twilio` config section. |

The integration talks to Twilio over HTTP exactly as documented — it does **not** use a Twilio SDK.
Messaging calls (`send`, `fetch`, `update`/redact/cancel, `list`) go to `Twilio:BaseUrl` when set,
otherwise `https://api.twilio.com`. Phone validation uses the Lookup host `https://lookups.twilio.com`
(not governed by `BaseUrl`). There is no inbound webhook, so delivery outcomes are obtained by asking
the provider (fetch/list).

## Endpoints

| Method & route | Role | Purpose |
|---|---|---|
| `POST /api/contact-numbers` | shopper | Register a mobile number (validated + canonicalised via Lookup). Returns `contactNumberId`. |
| `GET /api/contact-numbers` | shopper | The caller's registered numbers. |
| `DELETE /api/contact-numbers/{id}` | shopper | Remove one of the caller's numbers. |
| `POST /api/orders` | shopper | Place an order from catalog item ids + quantities. Returns `orderId`. Messages "placed". |
| `POST /api/orders/{orderId}/dispatch` | **operator** | Mark dispatched. Messages "on its way" + schedules a follow-up with the provider. |
| `POST /api/orders/{orderId}/cancel` | **operator** | Cancel. Messages "cancelled" + calls off the pending follow-up. |
| `GET /api/my-orders` | shopper | The caller's orders with notification state. |
| `GET /api/orders/{orderId}/notifications` | owner or operator | What was sent for the order; each entry has a `notificationId`. |
| `POST /api/notifications/{notificationId}/resend` | **operator** | Re-send a message. Idempotent per `idempotencyKey`. Returns the produced `notificationId`. |
| `DELETE /api/notifications/{notificationId}/content` | **operator** | Dispose of a message's content (redact at provider); record + outcome survive. |
| `GET /api/notifications/reconciliation?from={iso}&to={iso}` | **operator** | Provider's record (from the configured sending number) vs eShop's, over a range. |

Operator = the administrator role (`Administrators`) the project already uses. Everything else is
shopper-scoped and acts only on the caller's own data.

## Configuration & secrets

Settings bind from the `Twilio:` section (see `src/PublicApi/appsettings.json` for the keys). **Secret
values are never committed** — load them into .NET user-secrets. The values come from environment
variables; only the variable/secret *names* appear anywhere in the repo.

```bash
P=src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"          --project "$P"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"           --project "$P"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"          --project "$P"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID" --project "$P"
# Twilio:BaseUrl is optional (messaging-API host override); leave unset to use api.twilio.com.
```

## Running (this machine)

`global.json` rolls forward to the installed .NET 10 SDK; the app targets net8.0 (runtime present).
LocalDB is absent, so run with the in-memory database. Bind to the assigned port block (9303/9304).

```bash
export DOTNET_ROLL_FORWARD=Major
ASPNETCORE_ENVIRONMENT=Development \
UseOnlyInMemoryDatabase=true \
ASPNETCORE_URLS="https://localhost:9303;http://localhost:9304" \
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

> **In-memory caveat:** orders, contact numbers and notifications live only for a single run. Register,
> place, dispatch and cancel within the same run. (Restarting is what makes reconciliation's
> `providerOnly` list appear: the provider still remembers messages the fresh in-memory store forgot.)

## Step-by-step self-verification

Uses the two sandbox destinations only: `TWILIO_TEST_TO_NUMBER` (Canadian, deliverable) and
`TWILIO_UNREACHABLE_TO_NUMBER` (US, accepted then refused by the carrier). Never message any other real
number. `https://localhost:9303`, `curl -k` for the dev cert.

```bash
BASE=https://localhost:9303

# 0. Bearer token (admin is both an operator and a shopper here)
TOKEN=$(curl -sk -X POST "$BASE/api/authenticate" -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' \
  | sed -E 's/.*"token":"([^"]+)".*/\1/I')

# 1. Register the deliverable (Canadian) number — real Lookup validation, canonical E.164 stored
curl -sk -X POST "$BASE/api/contact-numbers" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d "{\"phoneNumber\":\"$TWILIO_TEST_TO_NUMBER\"}"

# 2. Place an order  -> real "order placed" SMS; note orderId (=1)
curl -sk -X POST "$BASE/api/orders" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":1,"quantity":1}]}'

# 3. Watch it reach 'delivered' (the GET refreshes status from the provider)
curl -sk "$BASE/api/orders/1/notifications" -H "Authorization: Bearer $TOKEN"

# 4. Dispatch -> "on its way" SMS + a follow-up scheduled with the provider (status 'scheduled')
curl -sk -X POST "$BASE/api/orders/1/dispatch" -H "Authorization: Bearer $TOKEN"

# 5. Cancel -> "cancelled" SMS + the scheduled follow-up flips to 'canceled' (never sent)
curl -sk -X POST "$BASE/api/orders/1/cancel" -H "Authorization: Bearer $TOKEN"
curl -sk "$BASE/api/orders/1/notifications" -H "Authorization: Bearer $TOKEN"   # DeliveryFollowUp = canceled

# 6. Swap to the undeliverable number: delete CA, register US, place order #2
curl -sk -X DELETE "$BASE/api/contact-numbers/1" -H "Authorization: Bearer $TOKEN"
curl -sk -X POST "$BASE/api/contact-numbers" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d "{\"phoneNumber\":\"$TWILIO_UNREACHABLE_TO_NUMBER\"}"
curl -sk -X POST "$BASE/api/orders" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":3,"quantity":1}]}'   # orderId=2

# 7. It is accepted then settles at 'undelivered' with a provider error code (poll a few times)
curl -sk "$BASE/api/orders/2/notifications" -H "Authorization: Bearer $TOKEN"

# 8. Operator resend (idempotent). Repeat with the SAME key -> same notificationId, no new send.
#    A FRESH key is a legitimate second attempt. (notification 5 is order 2's OrderPlaced.)
curl -sk -X POST "$BASE/api/notifications/5/resend" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"idempotencyKey":"resend-key-001"}'
curl -sk -X POST "$BASE/api/notifications/5/resend" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"idempotencyKey":"resend-key-001"}'   # same notificationId
curl -sk -X POST "$BASE/api/notifications/5/resend" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"idempotencyKey":"resend-key-002"}'   # new notificationId

# 9. Dispose of a message's content -> redacted at the provider; status/outcome survive
curl -sk -X DELETE "$BASE/api/notifications/5/content" -H "Authorization: Bearer $TOKEN"

# 10. Reconciliation over a range with data (matched / providerOnly / eShopOnly)
curl -sk "$BASE/api/notifications/reconciliation?from=2026-08-12T00:00:00Z&to=2026-08-12T23:59:59Z" \
  -H "Authorization: Bearer $TOKEN"
```

Authorization can be spot-checked with a normal-user token (`demouser@microsoft.com` / `Pass@word1`):
operator routes return **403**, no token returns **401**, and another shopper's order/number returns
**404**.

## Design notes

- **Delivery follow-up** is queued *with Twilio* (`ScheduleType=fixed` + `SendAt` a few days out +
  `MessagingServiceSid`), pinned to the configured `From`. It is not held by a timer in this app.
  Cancelling the order calls it off via `Status=canceled` (with a short retry to cover the brief window
  where a just-scheduled message is not yet cancelable).
- **Reconciliation** asks the provider for the configured `From` number's messages over the range
  (`From` + `DateSent` filters applied server-side, ISO-8601 date-times, full pagination), then counts
  only genuinely-sent outbound messages and lines them up against eShop's records by message SID.
- **Privacy:** the shopper's number is never logged; it is stored canonical and shown masked in
  notification/reconciliation views. The auth token is never logged, returned, or written to a file.
- **Resilience:** a message that cannot be sent never fails the underlying order operation — the send
  failure is recorded on the notification and the request still succeeds.
