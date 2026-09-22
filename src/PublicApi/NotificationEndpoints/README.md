# Order notifications by SMS (Twilio)

An **additive** capability on `src/PublicApi`: shoppers put a mobile number on file, and SMS
notifications go out as their orders move (placed → dispatched → cancelled), with a delivery
follow-up scheduled a few days after dispatch and called off if the order is cancelled. Operators
can re-send a message that did not arrive, dispose of a message's content, and reconcile against
the provider. It does not replace the existing catalog/basket/order flow.

Twilio is reached **only** through the vendored `src/TwilioSdk` SDK, behind
`ApplicationCore.Interfaces.ISmsProvider` (implemented by `Infrastructure/Sms/TwilioSmsProvider`).
A message that cannot be sent never fails the underlying order operation.

## Endpoints

Shopper-scoped (any authenticated caller; acts only on the caller's own data):

| Method & route | Purpose |
| --- | --- |
| `POST /api/contact-numbers` | Register a mobile number (provider-validated + canonicalised). Returns `contactNumberId`. |
| `GET /api/contact-numbers` | The caller's registered numbers. |
| `DELETE /api/contact-numbers/{contactNumberId}` | Remove one of the caller's numbers. |
| `POST /api/orders` | Place an order from catalog item ids + quantities. Returns `orderId`. |
| `GET /api/my-orders` | The caller's orders, each with its notifications' outcomes. |
| `GET /api/orders/{orderId}/notifications` | What was sent for the order and what became of each message (each entry has a `notificationId`). |

Operator-only (restricted to the `Administrators` role):

| Method & route | Purpose |
| --- | --- |
| `POST /api/orders/{orderId}/dispatch` | Mark dispatched: notify + queue a delivery follow-up. |
| `POST /api/orders/{orderId}/cancel` | Cancel: notify + call off any pending follow-up. |
| `POST /api/notifications/{notificationId}/resend` | Re-send a message that did not reach the shopper. Body `{ "idempotencyKey": "..." }`; returns the produced `notificationId`. |
| `DELETE /api/notifications/{notificationId}/content` | Dispose of a message's content (provider redaction + local clear); the fact and outcome survive. |
| `GET /api/notifications/reconciliation?from={iso}&to={iso}` | Provider's messages from the configured number in range vs eShop's records. |

## Configuration (bound from the `Twilio:` section)

| Key | From env var | Notes |
| --- | --- | --- |
| `Twilio:AccountSid` | `TWILIO_ACCOUNT_SID` | required |
| `Twilio:AuthToken` | `TWILIO_AUTH_TOKEN` | required, secret (never logged/returned) |
| `Twilio:FromNumber` | `TWILIO_FROM_NUMBER` | required; immediate messages send from it; reconciliation filters on it |
| `Twilio:MessagingServiceSid` | `TWILIO_MESSAGING_SERVICE_SID` | required; used to schedule the follow-up |
| `Twilio:BaseUrl` | — | optional; overrides the **messaging** API base URL only (Lookups is unaffected) |

The host **refuses to start** if any required value is missing or blank. Load secrets into
user-secrets (never into repo files):

```bash
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
```

## Verify it yourself (this machine)

This environment: `global.json` pins SDK 8.0.x but only .NET 10 is installed, and there is no
LocalDB — so roll forward and use the in-memory store. Data survives only within a single run, so
place/dispatch/cancel the same order in one run.

```bash
export DOTNET_ROLL_FORWARD=Major
# 1) Load secrets (above), then run on your port block:
cd src/PublicApi
ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true \
  ASPNETCORE_URLS="https://localhost:37063;http://localhost:37064" \
  dotnet run --no-launch-profile

# 2) In another shell — get tokens (default password: Pass@word1)
ADMIN=$(curl -sk -X POST https://localhost:37063/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
SHOP=$(curl -sk -X POST https://localhost:37063/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")

# 3) Register the reachable Canadian test number (a real message will arrive here)
curl -sk -X POST https://localhost:37063/api/contact-numbers -H "Authorization: Bearer $SHOP" \
  -H "Content-Type: application/json" -d "{\"phoneNumber\":\"$TWILIO_TEST_TO_NUMBER\"}"

# 4) Place → dispatch → cancel (real SMS at each step; a follow-up is scheduled then called off)
OID=$(curl -sk -X POST https://localhost:37063/api/orders -H "Authorization: Bearer $SHOP" \
  -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":1,"quantity":1}]}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
curl -sk -X POST https://localhost:37063/api/orders/$OID/dispatch -H "Authorization: Bearer $ADMIN"
curl -sk https://localhost:37063/api/orders/$OID/notifications -H "Authorization: Bearer $SHOP"   # follow-up = Scheduled
curl -sk -X POST https://localhost:37063/api/orders/$OID/cancel   -H "Authorization: Bearer $ADMIN"
curl -sk https://localhost:37063/api/orders/$OID/notifications -H "Authorization: Bearer $SHOP"   # follow-up = Cancelled

# 5) Undeliverable outcome + operator resend (idempotent). Register the unreachable US number
#    for a second shopper (admin), order, then resend the undelivered message.
curl -sk -X POST https://localhost:37063/api/contact-numbers -H "Authorization: Bearer $ADMIN" \
  -H "Content-Type: application/json" -d "{\"phoneNumber\":\"$TWILIO_UNREACHABLE_TO_NUMBER\"}"
OID2=$(curl -sk -X POST https://localhost:37063/api/orders -H "Authorization: Bearer $ADMIN" \
  -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":2,"quantity":1}]}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
# find the undelivered notification id from:
curl -sk https://localhost:37063/api/orders/$OID2/notifications -H "Authorization: Bearer $ADMIN"
# resend it twice under the SAME key (2nd is a no-op replay), then a fresh key (new send):
curl -sk -X POST https://localhost:37063/api/notifications/<NID>/resend -H "Authorization: Bearer $ADMIN" \
  -H "Content-Type: application/json" -d '{"idempotencyKey":"K1"}'
curl -sk -X POST https://localhost:37063/api/notifications/<NID>/resend -H "Authorization: Bearer $ADMIN" \
  -H "Content-Type: application/json" -d '{"idempotencyKey":"K1"}'   # same notificationId, no 2nd message

# 6) Dispose content, then reconcile over today
curl -sk -X DELETE https://localhost:37063/api/notifications/<NID>/content -H "Authorization: Bearer $ADMIN"
FROM=$(date -u +%Y-%m-%dT00:00:00Z); TO=$(date -u -d "+1 day" +%Y-%m-%dT00:00:00Z)
curl -sk "https://localhost:37063/api/notifications/reconciliation?from=$FROM&to=$TO" -H "Authorization: Bearer $ADMIN"
```

Only ever register/message `TWILIO_TEST_TO_NUMBER` (Canadian, reachable) and
`TWILIO_UNREACHABLE_TO_NUMBER` (US, undeliverable for this account — an expected outcome, not a
defect). Never text any other real number.

## Tests

- `tests/UnitTests` — `OrderNotificationServiceTests` (real EF in-memory store + fake provider):
  send-gating, the dispatch/cancel no-op guard, follow-up schedule+cancel, and resend idempotency
  (the primary-key claim's real duplicate rejection).
- `tests/PublicApiIntegrationTests` — boots the host with placeholder `Twilio:*` config (no live
  call): operator endpoints reject a normal user, and the offline shopper order flow.

## Notes for a real (non-in-memory) deployment

On SQL Server the three new tables (`ContactNumbers`, `SmsNotifications`, `SmsIdempotencyKeys`)
and the `Order.NotificationStatus` column need an EF migration. The in-memory provider used on this
machine ignores migrations and creates them automatically, so no migration ships here.
