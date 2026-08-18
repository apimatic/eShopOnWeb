# Order notifications by SMS (Twilio) — PublicApi

An additive capability on `src/PublicApi`: shoppers put a mobile number on file, SMS go out as an
order moves (placed / dispatched / cancelled), a "how did delivery go?" follow-up is queued with
Twilio for a few days later and called off if the order is cancelled, and operators can resend,
dispose of message content, and reconcile against the provider's own record.

All Twilio access goes through the **twilio-sdk** plugin's `AsadAli.TwilioSdk` package, isolated
behind `ISmsGateway` (`src/Infrastructure/Notifications/TwilioSmsGateway.cs`). A message that cannot
be sent is recorded as a failed notification and **never** fails the underlying order operation.

## Endpoints

| Method & route | Who | Purpose |
|---|---|---|
| `POST /api/contact-numbers` | shopper | Register a mobile number (validated + canonicalised by the provider). Returns `contactNumberId`. |
| `GET /api/contact-numbers` | shopper | The caller's registered numbers. |
| `DELETE /api/contact-numbers/{contactNumberId}` | shopper | Remove one; pending follow-ups to it are called off. |
| `POST /api/orders` | shopper | Place an order from catalog items. Returns `orderId`. |
| `POST /api/orders/{orderId}/dispatch` | **admin** | Mark dispatched; "on its way" SMS + queue the follow-up. |
| `POST /api/orders/{orderId}/cancel` | **admin** | Cancel; call off the follow-up + "cancelled" SMS. |
| `GET /api/my-orders` | shopper | The caller's orders, each with its notifications' outcomes. |
| `GET /api/orders/{orderId}/notifications` | shopper | Notifications for the caller's own order; each has a `notificationId`. |
| `POST /api/notifications/{notificationId}/resend` | **admin** | Resend; idempotent on a caller-supplied `idempotencyKey`. Returns the new `notificationId`. |
| `DELETE /api/notifications/{notificationId}/content` | **admin** | Dispose of the message text at the provider and locally; the record survives. |
| `GET /api/notifications/reconciliation?from={iso}&to={iso}` | **admin** | Provider's record for the range (its own traffic from `Twilio:FromNumber`) vs what eShop sent. |

## Configuration (`Twilio:` section)

Bound from `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`,
and optional `Twilio:BaseUrl` (messaging-API host override only). **No values are stored in the repo** —
load them into .NET user-secrets. The auth token is never logged or returned; shopper numbers are never
logged.

---

## Verify it yourself

Prerequisites: the six `TWILIO_*` environment variables set; .NET 10 SDK; a trusted HTTPS dev cert
(`dotnet dev-certs https --check`). This machine has no ASP.NET 8 runtime, so run with roll-forward.

### 1. Load the credentials into user-secrets (values come from the env vars, never typed)

```bash
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
cd ../..
```

### 2. Run PublicApi (in-memory DB, on the assigned ports)

```bash
export DOTNET_ROLL_FORWARD=Major
UseOnlyInMemoryDatabase=true ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="https://localhost:11003;http://localhost:11004" \
  dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

Swagger: `https://localhost:11003/swagger`. (`curl -k` below skips cert prompts.)

### 3. Get bearer tokens

```bash
BASE=https://localhost:11003/api
SHOP=$(curl -sk -X POST $BASE/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
ADM=$(curl -sk -X POST $BASE/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}'    | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
```

### 4. Flow 1 — contact number (real Twilio Lookup; stores canonical E.164)

```bash
curl -sk -X POST $BASE/contact-numbers -H "Authorization: Bearer $SHOP" \
  -H "Content-Type: application/json" -d "{\"number\":\"$TWILIO_TEST_TO_NUMBER\"}"     # -> 201 {contactNumberId, e164Number}
curl -sk $BASE/contact-numbers -H "Authorization: Bearer $SHOP"                        # -> the number on file
# A number the provider won't accept is rejected here, not at send time:
curl -sk -X POST $BASE/contact-numbers -H "Authorization: Bearer $SHOP" \
  -H "Content-Type: application/json" -d '{"number":"not-a-real-number"}'              # -> 400
```

### 5. Flow 2 — order lifecycle (a real text really arrives at the Canadian number)

```bash
OID=$(curl -sk -X POST $BASE/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":1,"quantity":2}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")

curl -sk -X POST $BASE/orders/$OID/dispatch -H "Authorization: Bearer $ADM"            # on-its-way SMS + follow-up queued
curl -sk $BASE/orders/$OID/notifications -H "Authorization: Bearer $SHOP"              # follow-up shows deliveryStatus "scheduled"

curl -sk -X POST $BASE/orders/$OID/cancel   -H "Authorization: Bearer $ADM"            # cancelled SMS + follow-up called off
curl -sk $BASE/orders/$OID/notifications -H "Authorization: Bearer $SHOP"              # follow-up now "canceled" (never sent)

curl -sk $BASE/my-orders -H "Authorization: Bearer $SHOP"                              # orders with notification outcomes
```

The Canadian number receives the placed / dispatched / cancelled texts (`deliveryStatus: "delivered"`);
the follow-up goes `scheduled` → `canceled` on cancellation — it never reaches the shopper.

### 6. Undelivered outcome + Flow 3 — resend, disposal, reconciliation (admin)

```bash
# A US number this account cannot deliver to (accepted, then carrier-refused = "undelivered"):
UID=$(curl -sk -X POST $BASE/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":3,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
# (register $TWILIO_UNREACHABLE_TO_NUMBER first if you want this order messaged)
NID=$(curl -sk $BASE/orders/$UID/notifications -H "Authorization: Bearer $SHOP" | python -c "import sys,json;print(json.load(sys.stdin)['notifications'][0]['notificationId'])")

# Resend is idempotent on the caller's key:
curl -sk -X POST $BASE/notifications/$NID/resend -H "Authorization: Bearer $ADM" -H "Content-Type: application/json" -d '{"idempotencyKey":"k1"}'  # sends, returns notificationId
curl -sk -X POST $BASE/notifications/$NID/resend -H "Authorization: Bearer $ADM" -H "Content-Type: application/json" -d '{"idempotencyKey":"k1"}'  # same id, duplicate:true, no second send
curl -sk -X POST $BASE/notifications/$NID/resend -H "Authorization: Bearer $ADM" -H "Content-Type: application/json" -d '{"idempotencyKey":"k2"}'  # fresh key -> new send

# Dispose of a message's content at the provider (record survives, body gone):
curl -sk -X DELETE $BASE/notifications/$NID/content -H "Authorization: Bearer $ADM"    # -> 204; the notification then shows contentRedacted:true

# Reconciliation over a range that has data (ISO-8601):
curl -sk "$BASE/notifications/reconciliation?from=2026-08-18T00:00:00Z&to=2026-08-19T00:00:00Z" -H "Authorization: Bearer $ADM"
```

Reconciliation lists the provider's messages **from `Twilio:FromNumber`** for the range and lines them
up against eShop's records: `matched`, `providerOnly` (the account's other traffic the provider knows and
eShop doesn't), and `eShopOnly`.

### 7. Guardrails to spot-check

- A shopper token on any admin route → **403** (e.g. `POST /api/notifications/{id}/resend`).
- One shopper cannot see another's order notifications → **404**.
- Stop the app before rebuilding so it releases the build outputs.
