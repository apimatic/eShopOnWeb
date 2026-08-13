# Order notifications by SMS (Twilio)

Additive capability on **`src/PublicApi`**: shoppers put a mobile number on file, receive SMS as their
orders move (placed → dispatched → cancelled), a "how did delivery go?" follow-up is queued with Twilio
for a few days later, and operators can re-send, dispose of message content, and reconcile against Twilio.

Nothing in the existing catalog/basket/order flow changes.

## What was added

| Layer | Files |
|------|-------|
| Domain | `ApplicationCore/Entities/NotificationAggregate/*` (`ContactNumber`, `OrderNotification`, `NotificationKind`), `ApplicationCore/Messaging/*` (result/report DTOs), `ApplicationCore/Interfaces/ISmsMessagingService.cs`, `IContactNumberService.cs`, `IOrderNotificationService.cs`, `Exceptions/InvalidPhoneNumberException.cs`, `Specifications/*` |
| Application | `ApplicationCore/Services/ContactNumberService.cs`, `OrderNotificationService.cs` |
| Infrastructure | `Infrastructure/Services/Twilio/*` (`TwilioMessagingService`, `TwilioSettings`, `SmsProviderException`), EF configs + `CatalogContext` DbSets |
| API | `PublicApi/ContactNumberEndpoints/*`, `PublicApi/OrderEndpoints/*`, `PublicApi/NotificationEndpoints/*`, DI in `Program.cs` |

## Endpoints

Shopper-scoped (any authenticated caller, acts only on the caller's own data):

- `POST /api/contact-numbers` → `{ contactNumberId, ... }` — validates with Twilio Lookup, stores the canonical E.164 form, rejects unusable numbers (400)
- `GET /api/contact-numbers`
- `DELETE /api/contact-numbers/{contactNumberId}`
- `POST /api/orders` → `{ orderId, ... }` — places an order from catalog item ids/quantities; sends "placed"
- `GET /api/my-orders`
- `GET /api/orders/{orderId}/notifications` — each entry carries its own `notificationId`; statuses refreshed live from Twilio

Operator-only (administrator role):

- `POST /api/orders/{orderId}/dispatch` — sends "dispatched" and **queues the follow-up with Twilio** (`ScheduleType=fixed`, `SendAt` = +3 days)
- `POST /api/orders/{orderId}/cancel` — sends "cancelled" and **cancels the not-yet-sent follow-up** (`Status=canceled`)
- `POST /api/notifications/{notificationId}/resend` → `{ notificationId, reused }` — caller-supplied idempotency key (body `idempotencyKey` or `Idempotency-Key` header)
- `DELETE /api/notifications/{notificationId}/content` — redacts the body at Twilio (`Body=""`); the record and outcome survive
- `GET /api/notifications/reconciliation?from={iso}&to={iso}` — asks Twilio for **`Twilio:FromNumber`'s** messages in the range and lines them up against eShop's records

A messaging failure never fails the order operation. A shopper with no number on file is not messaged.
Shopper numbers are never written to logs; the auth token is never logged, returned, or committed.

## Configuration (`Twilio:` section — never hard-coded)

`Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, and optional
`Twilio:BaseUrl` (override for the **messaging** API only; Lookup always uses its own host). Load the
values into user-secrets on the `PublicApi` project — never into a repo file:

```bash
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
```

## Run it (this machine)

Only the .NET 10 SDK is installed and there is no LocalDB, so roll forward and use the in-memory store:

```bash
export DOTNET_ROLL_FORWARD=Major
cd src/PublicApi
ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true \
  ASPNETCORE_URLS="https://localhost:9943;http://localhost:9944" \
  dotnet run --no-launch-profile
```

`global.json` is set to `rollForward: latestMajor`. The in-memory store is per-host and resets on
restart, so create/dispatch/cancel orders within a single run.

## Verify end to end

Use the two provided test destinations only: `TWILIO_TEST_TO_NUMBER` (Canadian, really delivers) and
`TWILIO_UNREACHABLE_TO_NUMBER` (US, accepted then carrier-refused — an expected outcome, not a defect).
`curl -k` trusts the dev cert.

```bash
B=https://localhost:9943
TOKEN=$(curl -sk -X POST "$B/api/authenticate" -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | jq -r .token)

# 1. Register the deliverable (Canadian) number — stored canonical form is returned
curl -sk -X POST "$B/api/contact-numbers" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d "{\"phoneNumber\":\"$TWILIO_TEST_TO_NUMBER\"}"

# 2. Place an order -> real "placed" SMS; capture orderId
OID=$(curl -sk -X POST "$B/api/orders" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":5,"quantity":1}]}' | jq .orderId)

# 3. Watch it reach delivered (a real message arrives at the Canadian handset)
curl -sk "$B/api/orders/$OID/notifications" -H "Authorization: Bearer $TOKEN" | jq '.notifications[]|{kind,status}'

# 4. Dispatch -> "on its way" SMS + follow-up shows status "scheduled"
curl -sk -X POST "$B/api/orders/$OID/dispatch" -H "Authorization: Bearer $TOKEN" | jq '.notifications[]|{kind,status,scheduledFor}'

# 5. Cancel -> follow-up flips to "canceled" (never sent) + "cancelled" SMS
curl -sk -X POST "$B/api/orders/$OID/cancel" -H "Authorization: Bearer $TOKEN" | jq '.notifications[]|{kind,status}'

# 6. Undeliverable path: register the US number, place an order, watch it settle "undelivered"
curl -sk -X POST "$B/api/contact-numbers" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d "{\"phoneNumber\":\"$TWILIO_UNREACHABLE_TO_NUMBER\"}"
UID=$(curl -sk -X POST "$B/api/orders" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":4,"quantity":1}]}' | jq .orderId)
# poll until status=undelivered:
curl -sk "$B/api/orders/$UID/notifications" -H "Authorization: Bearer $TOKEN" | jq '.notifications[]|{kind,status,errorCode}'

# 7. Re-send the undelivered message (NID = its notificationId). Same key = no second send.
curl -sk -X POST "$B/api/notifications/$NID/resend" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"idempotencyKey":"k1"}'   # reused:false
curl -sk -X POST "$B/api/notifications/$NID/resend" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"idempotencyKey":"k1"}'   # reused:true (no new message)

# 8. Dispose of a message's content — body is redacted at Twilio, record survives
curl -sk -X DELETE "$B/api/notifications/1/content" -H "Authorization: Bearer $TOKEN" -o /dev/null -w "%{http_code}\n"

# 9. Reconcile today's range against Twilio (From = Twilio:FromNumber)
FROM=$(date -u +%Y-%m-%dT00:00:00Z); TO=$(date -u -d "+1 hour" +%Y-%m-%dT%H:%M:%SZ)
curl -sk "$B/api/notifications/reconciliation?from=$FROM&to=$TO" -H "Authorization: Bearer $TOKEN" \
  | jq '{providerCount,eShopCount,matchedCount,providerOnlyCount,eShopOnlyCount}'
```

`admin@microsoft.com` / `Pass@word1` is an administrator (can drive both shopper and operator endpoints);
`demouser@microsoft.com` is a plain shopper (operator endpoints return 403, other users' data 404).
