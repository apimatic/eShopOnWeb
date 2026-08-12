# Order notifications by SMS (Twilio)

Shoppers are kept informed by text message as their orders progress. The capability is additive —
the existing catalog/basket/order flow is untouched — and is exposed entirely on the **PublicApi**
project (JWT-authenticated), routed under `/api/`.

## What was added

| Area | Where |
|------|-------|
| Domain: `ContactNumber`, `OrderNotification` (+ order `Status`/dispatched/cancelled) | `src/ApplicationCore/Entities/NotificationAggregate`, `…/OrderAggregate/Order.cs` |
| Provider client (Twilio over plain HTTP) | `src/Infrastructure/Messaging/TwilioMessagingService.cs` |
| Orchestration (compose/send/schedule/cancel/refresh/reconcile/idempotent resend/dispose) | `src/ApplicationCore/Services/OrderNotificationService.cs`, `ContactNumberService.cs`, `OrderProcessingService.cs` |
| HTTP endpoints | `src/PublicApi/{ContactNumberEndpoints,OrderEndpoints,NotificationEndpoints}` |

### Endpoints

Shopper-scoped (any authenticated user, acts only on the caller's own data):

- `POST /api/contact-numbers` → `{ contactNumberId, … }`
- `GET /api/contact-numbers`
- `DELETE /api/contact-numbers/{contactNumberId}`
- `POST /api/orders` (catalog item ids + quantities) → `{ orderId, … }`
- `GET /api/my-orders`
- `GET /api/orders/{orderId}/notifications` (each entry carries its own `notificationId`)

Operator-only (`Administrators` role):

- `POST /api/orders/{orderId}/dispatch`
- `POST /api/orders/{orderId}/cancel`
- `POST /api/notifications/{notificationId}/resend` (idempotency key: `Idempotency-Key` header or `?idempotencyKey=`) → `{ notificationId, … }`
- `DELETE /api/notifications/{notificationId}/content`
- `GET /api/notifications/reconciliation?from={iso}&to={iso}`

## The Twilio contract used (all confirmed against Twilio docs)

- **Validate/canonicalize** a number: `GET https://lookups.twilio.com/v2/PhoneNumbers/{n}` → `valid`, `phone_number`. (Separate host; **not** governed by `Twilio:BaseUrl`.)
- **Send**: `POST {messaging-base}/2010-04-01/Accounts/{sid}/Messages.json` with `To`, `From`, `Body`.
- **Schedule** (follow-up): same, plus `MessagingServiceSid`, `ScheduleType=fixed`, `SendAt` (ISO-8601, 15 min–35 days out).
- **Cancel scheduled**: `POST …/Messages/{sid}.json` with `Status=canceled`.
- **Fetch status** (no public callback URL exists, so status is pulled on read): `GET …/Messages/{sid}.json` → `status`, `error_code`.
- **Dispose content**: `POST …/Messages/{sid}.json` with `Body=` (empty) — redacts the text at Twilio, keeps the record.
- **Reconcile**: `GET …/Messages.json?From={Twilio:FromNumber}&DateSent>=…&DateSent<=…` (+ pagination) — asks the provider only for our own sending number's messages.

`{messaging-base}` is `Twilio:BaseUrl` when set, otherwise `https://api.twilio.com`.

## Configuration

Bound from the `Twilio:` section (values live in .NET user-secrets on this machine, never in the repo):
`Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, and optional `Twilio:BaseUrl`.

To (re)load them from the environment variables (values are never printed):

```bash
P=src/PublicApi/PublicApi.csproj
dotnet user-secrets set --project $P "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"          >/dev/null
dotnet user-secrets set --project $P "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"           >/dev/null
dotnet user-secrets set --project $P "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"          >/dev/null
dotnet user-secrets set --project $P "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID" >/dev/null
```

## Run it (this machine)

Only the .NET 10 SDK is installed and there is no LocalDB, so:

```bash
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development
export UseOnlyInMemoryDatabase=true
export ASPNETCORE_URLS="https://localhost:9283;http://localhost:9284"
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

`global.json` was set to `rollForward: latestMajor` so the .NET 10 SDK builds the net8.0 solution.
The in-memory store is per-host and cleared on restart, so create/dispatch/cancel within one run.
Swagger UI: `https://localhost:9283/swagger`.

## Verify it yourself (curl)

> The account is **live**: keep volume low. Message **only** the two provided fixtures —
> `TWILIO_TEST_TO_NUMBER` (Canada, really delivers) and `TWILIO_UNREACHABLE_TO_NUMBER`
> (US, accepted then refused by the carrier — an expected outcome, not a defect). Never send elsewhere.

```bash
BASE=https://localhost:9283
ADMIN=$(curl -sk -X POST $BASE/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | jq -r .token)

# Flow 1 — contact number (canonical form is stored & returned)
curl -sk -X POST $BASE/api/contact-numbers -H "Authorization: Bearer $ADMIN" \
  -H "Content-Type: application/json" -d "{\"phoneNumber\":\"$TWILIO_TEST_TO_NUMBER\"}"   # -> contactNumberId
curl -sk $BASE/api/contact-numbers -H "Authorization: Bearer $ADMIN"

# Flow 2 — place / dispatch / cancel (real texts to the CA number)
curl -sk -X POST $BASE/api/orders -H "Authorization: Bearer $ADMIN" \
  -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":1,"quantity":2}]}'   # -> orderId
curl -sk $BASE/api/orders/1/notifications -H "Authorization: Bearer $ADMIN"    # poll: OrderPlaced -> delivered
curl -sk -X POST $BASE/api/orders/1/dispatch -H "Authorization: Bearer $ADMIN" # on-its-way + follow-up scheduled
curl -sk $BASE/api/orders/1/notifications -H "Authorization: Bearer $ADMIN"    # DeliveryFollowUp = scheduled
curl -sk -X POST $BASE/api/orders/1/cancel -H "Authorization: Bearer $ADMIN"   # cancelled + follow-up called off
curl -sk $BASE/api/orders/1/notifications -H "Authorization: Bearer $ADMIN"    # DeliveryFollowUp = canceled
curl -sk $BASE/api/my-orders -H "Authorization: Bearer $ADMIN"

# Flow 3 — resend is idempotent on the key; content disposal; reconciliation
NID=... # a notificationId that did not reach the shopper (e.g. an undelivered US send)
curl -sk -X POST $BASE/api/notifications/$NID/resend -H "Authorization: Bearer $ADMIN" -H "Idempotency-Key: k1"  # new
curl -sk -X POST $BASE/api/notifications/$NID/resend -H "Authorization: Bearer $ADMIN" -H "Idempotency-Key: k1"  # reused=true, no 2nd send
curl -sk -X POST $BASE/api/notifications/$NID/resend -H "Authorization: Bearer $ADMIN" -H "Idempotency-Key: k2"  # new
curl -sk -X DELETE $BASE/api/notifications/$NID/content -H "Authorization: Bearer $ADMIN"   # body redacted at Twilio, record kept
curl -sk -G $BASE/api/notifications/reconciliation -H "Authorization: Bearer $ADMIN" \
  --data-urlencode "from=2026-08-12T00:00:00Z" --data-urlencode "to=2026-08-13T00:00:00Z"
```

Cross-check any message directly at the provider:
`curl -s -u "$TWILIO_ACCOUNT_SID:$TWILIO_AUTH_TOKEN" https://api.twilio.com/2010-04-01/Accounts/$TWILIO_ACCOUNT_SID/Messages/{sid}.json`
— a cancelled follow-up shows `status: canceled, date_sent: null`; a disposed message shows an empty `body`.
