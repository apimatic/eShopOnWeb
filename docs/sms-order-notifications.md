# Order notifications by SMS (Twilio)

An additive capability on top of eShopOnWeb: shoppers put a mobile number on file, receive SMS as
their orders move (placed / dispatched / cancelled), and operators can re-send, dispose of message
content, and reconcile against the provider. Everything is exposed on the **PublicApi** project
(JWT-authenticated) under `/api/`. **Twilio** is the provider, and the OpenAPI documents in
`api-specs/twilio/` are the authoritative contract for every Twilio interaction.

No pre-built Twilio SDK is used — the integration is hand-written against the spec
(`src/Infrastructure/Twilio/TwilioSmsGateway.cs`).

## Twilio operations used (from `api-specs/`)

| Need | Spec document | Operation |
|------|---------------|-----------|
| Validate a number at registration, get canonical E.164 | `twilio_lookups_v2` | `GET /v2/PhoneNumbers/{PhoneNumber}` → `valid`, `phone_number` |
| Send a message now | `twilio_api_v2010` | `POST .../Messages.json` (`To`,`From`,`Body`) |
| Schedule the delivery follow-up (provider holds it) | `twilio_api_v2010` | `POST .../Messages.json` (`MessagingServiceSid`,`ScheduleType=fixed`,`SendAt`) |
| Read a message's delivery outcome | `twilio_api_v2010` | `GET .../Messages/{Sid}.json` |
| Call off a scheduled message | `twilio_api_v2010` | `POST .../Messages/{Sid}.json` (`Status=canceled`) |
| Dispose of message content (redact) | `twilio_api_v2010` | `POST .../Messages/{Sid}.json` (`Body=`) |
| Reconcile provider records | `twilio_api_v2010` | `GET .../Messages.json?From=&DateSent>=&DateSent<=` (paged) |

The Lookup API is served from `https://lookups.twilio.com`; the messaging API from
`https://api.twilio.com` unless `Twilio:BaseUrl` overrides it (messaging only). Auth is HTTP Basic
(`AccountSid:AuthToken`).

## Endpoints

Shopper-scoped (any authenticated caller; acts only on the caller's own data):

- `POST /api/contact-numbers` `{ "phoneNumber": "..." }` → `{ contactNumberId, phoneNumber }` (canonical). Rejects unusable numbers with 400.
- `GET /api/contact-numbers`
- `DELETE /api/contact-numbers/{contactNumberId}` → 204 / 404
- `POST /api/orders` `{ "items": [{ "catalogItemId", "quantity" }], "shipToAddress"?: {...} }` → `{ orderId }`
- `GET /api/my-orders` — orders, each with its notifications
- `GET /api/orders/{orderId}/notifications` — each entry carries its own `notificationId`

Operator-only (Administrators role):

- `POST /api/orders/{orderId}/dispatch`
- `POST /api/orders/{orderId}/cancel`
- `POST /api/notifications/{notificationId}/resend` `{ "idempotencyKey": "..." }` (or `Idempotency-Key` header) → `{ notificationId, replayed }`
- `DELETE /api/notifications/{notificationId}/content` → 204
- `GET /api/notifications/reconciliation?from={iso}&to={iso}`

## Configuration

Bound from the `Twilio:` section (no values in the repo — loaded from environment into user-secrets):
`Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`,
`Twilio:BaseUrl` (optional messaging override). The auth token is never logged, returned, or written
to a file; shopper numbers are never logged and are returned masked.

## Run it

This machine has only the .NET 10 SDK and no SQL LocalDB, so roll forward and use the in-memory store.

```bash
# 1) Load the Twilio credentials from environment into user-secrets (values stay out of the repo)
cd src/PublicApi
export DOTNET_ROLL_FORWARD=Major
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"

# 2) Run (in-memory DB; JWT host)
DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true \
  ASPNETCORE_URLS="https://localhost:10063;http://localhost:10064" \
  dotnet run --no-launch-profile
```

Swagger UI: `https://localhost:10063/swagger`.

## Verify end to end (curl)

`admin@microsoft.com` / `Pass@word1` is both an operator and a shopper. `TWILIO_TEST_TO_NUMBER`
(Canadian) is deliverable; `TWILIO_UNREACHABLE_TO_NUMBER` (reserved US) is accepted then reported
`undelivered` — that is an expected outcome for this live account, not a defect.

```bash
B=https://localhost:10063
TOKEN=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
H="Authorization: Bearer $TOKEN"

# Flow 1 — numbers (canonical form stored; invalid rejected)
curl -sk -X POST $B/api/contact-numbers -H "$H" -H "Content-Type: application/json" -d "{\"phoneNumber\":\"$TWILIO_TEST_TO_NUMBER\"}"
curl -sk -X POST $B/api/contact-numbers -H "$H" -H "Content-Type: application/json" -d "{\"phoneNumber\":\"$TWILIO_UNREACHABLE_TO_NUMBER\"}"
curl -sk -X POST $B/api/contact-numbers -H "$H" -H "Content-Type: application/json" -d '{"phoneNumber":"+1512"}'   # 400
curl -sk $B/api/contact-numbers -H "$H"

# Flow 2 — place / dispatch / cancel; watch notifications
OID=$(curl -sk -X POST $B/api/orders -H "$H" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":3,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
sleep 5; curl -sk $B/api/orders/$OID/notifications -H "$H"     # delivered + undelivered
curl -sk -X POST $B/api/orders/$OID/dispatch -H "$H"
sleep 5; curl -sk $B/api/orders/$OID/notifications -H "$H"     # + DeliveryFeedbackRequest = scheduled
curl -sk -X POST $B/api/orders/$OID/cancel -H "$H"
sleep 3; curl -sk $B/api/orders/$OID/notifications -H "$H"     # scheduled follow-ups now canceled
curl -sk $B/api/my-orders -H "$H"

# Flow 3 — resend (idempotent), dispose content, reconcile
NID=<a notificationId that is undelivered from the list above>
KEY=$(python -c "import uuid;print(uuid.uuid4().hex)")
curl -sk -X POST $B/api/notifications/$NID/resend -H "$H" -H "Content-Type: application/json" -d "{\"idempotencyKey\":\"$KEY\"}"  # replayed:false
curl -sk -X POST $B/api/notifications/$NID/resend -H "$H" -H "Content-Type: application/json" -d "{\"idempotencyKey\":\"$KEY\"}"  # replayed:true, same id
curl -sk -X DELETE $B/api/notifications/$NID/content -H "$H"                                                                      # 204
curl -sk "$B/api/notifications/reconciliation?from=2026-08-13T00:00:00Z&to=2026-08-14T00:00:00Z" -H "$H"
```

Cross-check any message directly at the provider (status/body) with:
`curl -s -u "$TWILIO_ACCOUNT_SID:$TWILIO_AUTH_TOKEN" https://api.twilio.com/2010-04-01/Accounts/$TWILIO_ACCOUNT_SID/Messages/<SID>.json`.

## Notes

- With the in-memory provider, data lives only for the run and is per-host — place/dispatch/cancel the
  same order within one run (that is why `POST /api/orders` exists on the API).
- The delivery follow-up is scheduled ~3 days out **with the provider** (not by a timer in this app),
  so it does not fire during a test; cancelling the order calls it off at the provider.
- Reconciliation asks the provider for messages from `Twilio:FromNumber` only; other account traffic
  shows up as "provider-only" discrepancies, and canceled/not-yet-sent follow-ups show up as
  "eShop-only".
