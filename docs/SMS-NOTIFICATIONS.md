# Order notifications by SMS (Twilio) — PublicApi

An additive capability on the eShopOnWeb **PublicApi** project: shoppers register a mobile
number, receive SMS as their order moves (placed → dispatched → cancelled), and operators can
re-send, dispose of message content, and reconcile against the provider. Messaging goes through
**Twilio** via the `AsadAli.TwilioSdk` SDK.

## Where things live

| Concern | Location |
|---|---|
| Domain entities (`ContactNumber`, `Notification`) + services + `ISmsGateway` | `src/ApplicationCore` |
| EF config + DbSets (in-memory friendly) | `src/Infrastructure/Data` |
| Twilio gateway, settings, DI wiring (SDK-facing) | `src/PublicApi/Sms` |
| HTTP endpoints | `src/PublicApi/{ContactNumber,OrderNotification,Notification}Endpoints` |

> The Twilio SDK drags in .NET 10 `Microsoft.Extensions.*` packages, so the SDK-facing code is
> **contained in PublicApi** to keep those out of the shared `Infrastructure`/`Web` projects.

## Configuration (`Twilio:` section — values from environment, never in the repo)

| Key | From env var |
|---|---|
| `Twilio:AccountSid` | `TWILIO_ACCOUNT_SID` |
| `Twilio:AuthToken` | `TWILIO_AUTH_TOKEN` (secret — never logged/returned) |
| `Twilio:FromNumber` | `TWILIO_FROM_NUMBER` (sending number; reconciliation filters on this) |
| `Twilio:MessagingServiceSid` | `TWILIO_MESSAGING_SERVICE_SID` (required to schedule follow-ups) |
| `Twilio:BaseUrl` | *(optional)* verbatim override for the **messaging** API only |

Settings are validated at startup (`ValidateOnStart`): a missing credential refuses to boot.

---

## Verify it yourself

### 0. One-time setup

```bash
# This machine: SDK pinned to 8.0.x but only .NET 10 is installed → roll forward.
export DOTNET_ROLL_FORWARD=Major

# Trust the dev cert if needed
dotnet dev-certs https --check --trust

# Load the real Twilio credentials into user-secrets (values stay out of the repo)
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
cd ../..
```

### 1. Run PublicApi (in-memory DB, assigned port)

```bash
export DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development \
       UseOnlyInMemoryDatabase=true ASPNETCORE_URLS="https://localhost:11283"
dotnet run --project src/PublicApi --no-launch-profile
```

> In-memory means data lives only for this run — do place/dispatch/cancel within the same run.

### 2. Get tokens

```bash
API=https://localhost:11283
SHOPPER=$(curl -k -s -X POST $API/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)
ADMIN=$(curl -k -s -X POST $API/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | jq -r .token)
```

### 3. Flow 1 — contact number on file (shopper)

```bash
# Register the deliverable Canadian number and the undeliverable US number (the ONLY two to use).
# Returns { contactNumberId, phoneNumber (provider-canonical E.164) }.
curl -k -s -X POST $API/api/contact-numbers -H "Authorization: Bearer $SHOPPER" \
  -H "Content-Type: application/json" -d "{\"phoneNumber\":\"$TWILIO_TEST_TO_NUMBER\"}"
curl -k -s -X POST $API/api/contact-numbers -H "Authorization: Bearer $SHOPPER" \
  -H "Content-Type: application/json" -d "{\"phoneNumber\":\"$TWILIO_UNREACHABLE_TO_NUMBER\"}"

# An unusable number is rejected at registration (400):
curl -k -s -X POST $API/api/contact-numbers -H "Authorization: Bearer $SHOPPER" \
  -H "Content-Type: application/json" -d '{"phoneNumber":"+1555"}'

curl -k -s $API/api/contact-numbers -H "Authorization: Bearer $SHOPPER"      # list caller's numbers
# curl -k -s -X DELETE $API/api/contact-numbers/1 -H "Authorization: Bearer $SHOPPER"   # 204; gone afterwards
```

### 4. Flow 2 — messages as the order moves

```bash
# Place an order (shopper). Returns { orderId }. Real SMS is sent to the numbers on file.
OID=$(curl -k -s -X POST $API/api/orders -H "Authorization: Bearer $SHOPPER" \
  -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":5,"quantity":1}]}' | jq -r .orderId)

# Dispatch (operator): "on its way" now + a delivery follow-up QUEUED WITH TWILIO ~3 days out.
curl -k -s -X POST $API/api/orders/$OID/dispatch -H "Authorization: Bearer $ADMIN"

# See where each notification got to (each entry has a notificationId).
curl -k -s $API/api/orders/$OID/notifications -H "Authorization: Bearer $SHOPPER" | jq
#   Expect: the CA number -> "delivered"; the US number -> "undelivered" (carrier refuses);
#           two DeliveryFollowUp entries -> "scheduled" with a scheduledFor ~3 days ahead.

# Cancel (operator): "cancelled" now + the not-yet-sent follow-ups are called off at Twilio.
curl -k -s -X POST $API/api/orders/$OID/cancel -H "Authorization: Bearer $ADMIN"
curl -k -s $API/api/orders/$OID/notifications -H "Authorization: Bearer $SHOPPER" | jq
#   Expect: the two follow-ups now -> "canceled" (they can never reach the customer).

curl -k -s $API/api/my-orders -H "Authorization: Bearer $SHOPPER" | jq     # orders + notification states
```

### 5. Flow 3 — operator actions

```bash
# Pick a notificationId that did not reach the shopper (e.g. an "undelivered"/"send_failed" one).
NID=<notificationId>

# Resend with an idempotency key. Same key => no second message; a fresh key => a genuine resend.
curl -k -s -X POST $API/api/notifications/$NID/resend -H "Authorization: Bearer $ADMIN" \
  -H "Idempotency-Key: attempt-1"        # -> { notificationId (new), outcome: "sent" }
curl -k -s -X POST $API/api/notifications/$NID/resend -H "Authorization: Bearer $ADMIN" \
  -H "Idempotency-Key: attempt-1"        # -> same notificationId, outcome: "replayed"
curl -k -s -X POST $API/api/notifications/$NID/resend -H "Authorization: Bearer $ADMIN" \
  -H "Idempotency-Key: attempt-2"        # -> new notificationId, outcome: "sent"

# Dispose message content at the provider (204). The send record + status survive; the text does not.
curl -k -s -X DELETE $API/api/notifications/$NID/content -H "Authorization: Bearer $ADMIN"

# Reconcile: provider's record of THIS number's messages vs eShop's, over a range with data.
FROM=$(date -u -d '-2 hours' +%Y-%m-%dT%H:%M:%SZ);  TO=$(date -u -d '+10 min' +%Y-%m-%dT%H:%M:%SZ)
curl -k -s -G $API/api/notifications/reconciliation \
  --data-urlencode "from=$FROM" --data-urlencode "to=$TO" -H "Authorization: Bearer $ADMIN" | jq
#   Expect matched / provider-only / eShop-only counts (e.g. canceled follow-ups show as eShop-only).
```

### Authorization

- Dispatch, cancel, resend, content-disposal, reconciliation require the **Administrators** role.
- Every other endpoint is shopper-scoped: a shopper only ever sees/uses/deletes their own numbers
  and orders (cross-shopper access returns 403; another's number returns 404).

### Notes

- **Only** `TWILIO_TEST_TO_NUMBER` (Canadian, deliverable) and `TWILIO_UNREACHABLE_TO_NUMBER`
  (US, accepted-then-refused) may be registered/messaged. The US "undelivered" is an expected live
  outcome, not a defect.
- A message that cannot be sent never fails the order operation; it is recorded as `send_failed`.
- Numbers are stored in provider-canonical form and are never written to logs; API responses mask
  destinations to the last 4 digits (the shopper's own numbers list is the exception).
