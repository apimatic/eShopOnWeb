# SMS order notifications (Twilio)

An **additive** capability on `src/PublicApi`: shoppers put a mobile number on file, get SMS
updates as their order moves (placed → dispatched → delivery follow-up / cancelled), and
operators can resend, dispose of message content, and reconcile against the provider. Twilio is
the messaging provider, reached exclusively through the **twilio-sdk** plugin package
(`AsadAli.TwilioSdk`).

## Where things live

| Layer | What was added |
|---|---|
| `ApplicationCore/Entities` | `ContactNumber`, `OrderNotification` (+ `NotificationKind`, `NotificationStatus`) aggregates |
| `ApplicationCore/Interfaces` | `ISmsNotificationService` (provider-agnostic), `IContactNumberService`, `IOrderNotificationService`, `IResendIdempotencyGuard` |
| `ApplicationCore/Services` | `ContactNumberService`, `OrderNotificationService`, `KeyedResendIdempotencyGuard` |
| `Infrastructure/Sms` | `TwilioSmsNotificationService` (the only Twilio SDK boundary), `TwilioSettings`, `TwilioServiceCollectionExtensions` |
| `Infrastructure/Data` | `ContactNumbers` / `OrderNotifications` `DbSet`s + EF configs |
| `PublicApi/*Endpoints` | the 11 HTTP endpoints below |

Clean-architecture layering is preserved: `ApplicationCore` never references the SDK; all Twilio
calls are behind `ISmsNotificationService`.

## Endpoints

Shopper-scoped (JWT, acts only on the caller's own data):

- `POST /api/contact-numbers` → `{ contactNumberId, phoneNumber }` (validates + canonicalises via Twilio Lookup)
- `GET /api/contact-numbers`
- `DELETE /api/contact-numbers/{contactNumberId}`
- `POST /api/orders` → `{ orderId }` (catalog item ids + quantities; identity from token)
- `GET /api/my-orders`
- `GET /api/orders/{orderId}/notifications` (each entry carries its own `notificationId`)

Operator-only (JWT + `Administrators` role):

- `POST /api/orders/{orderId}/dispatch` (sends "on its way" + schedules the follow-up with Twilio)
- `POST /api/orders/{orderId}/cancel` (sends "cancelled" + cancels the scheduled follow-up)
- `POST /api/notifications/{notificationId}/resend` → `{ notificationId }` (idempotency via `Idempotency-Key` header)
- `DELETE /api/notifications/{notificationId}/content` (redacts the body at Twilio)
- `GET /api/notifications/reconciliation?from={iso}&to={iso}`

## Configuration

Settings bind from the `Twilio:` section and are **validated at startup** (a missing credential
refuses boot):

| Key | Source env var |
|---|---|
| `Twilio:AccountSid` | `TWILIO_ACCOUNT_SID` |
| `Twilio:AuthToken` | `TWILIO_AUTH_TOKEN` (secret — never logged/returned/committed) |
| `Twilio:FromNumber` | `TWILIO_FROM_NUMBER` |
| `Twilio:MessagingServiceSid` | `TWILIO_MESSAGING_SERVICE_SID` |
| `Twilio:BaseUrl` | *(optional)* override for the messaging API host only (not Lookup) |

Load them into user-secrets for the PublicApi project (values stay out of the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
```

## Run the API (this machine)

Only the .NET 10 SDK is present and there is no LocalDB, so roll forward and use the in-memory DB:

```bash
export DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development \
       UseOnlyInMemoryDatabase=true \
       ASPNETCORE_URLS="https://localhost:11323;http://localhost:11324"
dotnet run --project src/PublicApi --no-launch-profile -c Debug
```

The in-memory store is per-process and per-host: place/dispatch/cancel the orders you create in
the **same** run, through PublicApi only. Seeded logins: `admin@microsoft.com` (operator) and
`demouser@microsoft.com` (shopper), password `Pass@word1`.

## Step-by-step verification (curl)

Twilio destinations are restricted to the two provided by the sandbox: `TWILIO_TEST_TO_NUMBER`
(Canadian, reachable) and `TWILIO_UNREACHABLE_TO_NUMBER` (US, accepted then refused by the
carrier). Register/message only those.

```bash
BASE=https://localhost:11323

# 1. Tokens
DEMO=$(curl -sk -X POST $BASE/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | python -c 'import sys,json;print(json.load(sys.stdin)["token"])')
ADMIN=$(curl -sk -X POST $BASE/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | python -c 'import sys,json;print(json.load(sys.stdin)["token"])')

# 2. Flow 1 — shopper registers the reachable number (stored canonical form is returned)
curl -sk -X POST $BASE/api/contact-numbers -H "Authorization: Bearer $DEMO" \
  -H 'Content-Type: application/json' -d "{\"phoneNumber\":\"$TWILIO_TEST_TO_NUMBER\"}"
#    an unusable number is rejected at registration (HTTP 400):
curl -sk -o /dev/null -w '%{http_code}\n' -X POST $BASE/api/contact-numbers -H "Authorization: Bearer $DEMO" \
  -H 'Content-Type: application/json' -d '{"phoneNumber":"not-a-number"}'
curl -sk $BASE/api/contact-numbers -H "Authorization: Bearer $DEMO"

# 3. Flow 2 — place an order (real SMS to the reachable number)
OID=$(curl -sk -X POST $BASE/api/orders -H "Authorization: Bearer $DEMO" \
  -H 'Content-Type: application/json' -d '{"items":[{"catalogItemId":1,"quantity":1}]}' \
  | python -c 'import sys,json;print(json.load(sys.stdin)["orderId"])')
#    notifications refresh live delivery status from Twilio (no callback URL exists):
curl -sk $BASE/api/orders/$OID/notifications -H "Authorization: Bearer $DEMO"   # OrderPlaced → delivered

# 4. Dispatch (operator) — "on its way" + a follow-up scheduled with Twilio:
curl -sk -X POST $BASE/api/orders/$OID/dispatch -H "Authorization: Bearer $ADMIN"
curl -sk $BASE/api/orders/$OID/notifications -H "Authorization: Bearer $DEMO"   # + DeliveryFollowUp = scheduled

# 5. Cancel (operator) — cancellation SMS + the scheduled follow-up is called off:
curl -sk -X POST $BASE/api/orders/$OID/cancel -H "Authorization: Bearer $ADMIN"
curl -sk $BASE/api/orders/$OID/notifications -H "Authorization: Bearer $DEMO"   # DeliveryFollowUp = canceled

# 6. Flow 3 — undeliverable + resend idempotency (use the US number on the admin account)
curl -sk -X POST $BASE/api/contact-numbers -H "Authorization: Bearer $ADMIN" \
  -H 'Content-Type: application/json' -d "{\"phoneNumber\":\"$TWILIO_UNREACHABLE_TO_NUMBER\"}"
AOID=$(curl -sk -X POST $BASE/api/orders -H "Authorization: Bearer $ADMIN" \
  -H 'Content-Type: application/json' -d '{"items":[{"catalogItemId":2,"quantity":1}]}' \
  | python -c 'import sys,json;print(json.load(sys.stdin)["orderId"])')
NID=$(curl -sk $BASE/api/orders/$AOID/notifications -H "Authorization: Bearer $ADMIN" \
  | python -c 'import sys,json;print(json.load(sys.stdin)["notifications"][0]["notificationId"])')   # status → undelivered
curl -sk -X POST $BASE/api/notifications/$NID/resend -H "Authorization: Bearer $ADMIN" -H 'Idempotency-Key: k1'  # new
curl -sk -X POST $BASE/api/notifications/$NID/resend -H "Authorization: Bearer $ADMIN" -H 'Idempotency-Key: k1'  # SAME id, no 2nd send
curl -sk -X POST $BASE/api/notifications/$NID/resend -H "Authorization: Bearer $ADMIN" -H 'Idempotency-Key: k2'  # new id

# 7. Content disposal (operator) — body no longer retrievable at the provider:
curl -sk -o /dev/null -w '%{http_code}\n' -X DELETE $BASE/api/notifications/$NID/content -H "Authorization: Bearer $ADMIN"  # 204

# 8. Reconciliation (operator) — provider's record vs eShop's, counting only Twilio:FromNumber:
curl -sk "$BASE/api/notifications/reconciliation?from=2026-08-18T00:00:00Z&to=2026-08-19T00:00:00Z" \
  -H "Authorization: Bearer $ADMIN"
```

What to expect: the reachable number's messages reach `delivered`; the US number's reach
`undelivered` (Twilio error `30034` — an expected outcome for this account, not a defect); the
follow-up shows `scheduled` after dispatch and `canceled` after cancel; the same idempotency key
returns the same `notificationId` with no second message; after content disposal the message body
is empty at Twilio while the record and status survive; the reconciliation report lists matched,
provider-only and eShop-only messages over the whole range.

## Automated tests

```bash
DOTNET_ROLL_FORWARD=Major dotnet test tests/UnitTests/UnitTests.csproj            # domain + idempotency
DOTNET_ROLL_FORWARD=Major dotnet test tests/IntegrationTests/IntegrationTests.csproj  # Twilio SDK wire-shape (offline, HttpClient seam)
```
