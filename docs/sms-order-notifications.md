# Order notifications by SMS (Twilio)

An additive capability on **`src/PublicApi`** that keeps shoppers informed by text message as their
orders progress, using **Twilio** as the messaging provider. It does not replace the existing
catalog/basket/order flow.

## What was added

| Flow | Endpoint | Auth |
|------|----------|------|
| Register a mobile number | `POST /api/contact-numbers` | shopper |
| List my numbers | `GET /api/contact-numbers` | shopper |
| Remove a number | `DELETE /api/contact-numbers/{contactNumberId}` | shopper (own only) |
| Place an order | `POST /api/orders` | shopper |
| Dispatch an order | `POST /api/orders/{orderId}/dispatch` | operator |
| Cancel an order | `POST /api/orders/{orderId}/cancel` | operator |
| My orders + notification state | `GET /api/my-orders` | shopper |
| An order's notifications | `GET /api/orders/{orderId}/notifications` | shopper (own) / operator (any) |
| Resend a message | `POST /api/notifications/{notificationId}/resend` | operator |
| Dispose of a message's content | `DELETE /api/notifications/{notificationId}/content` | operator |
| Reconciliation report | `GET /api/notifications/reconciliation?from=&to=` | operator |

Operator endpoints require the existing **Administrators** role. Every other endpoint is shopper-scoped
and acts only on the caller's own data (identity comes from the JWT).

## How it maps to the Twilio OpenAPI specs

The Twilio OpenAPI documents in `api-specs/twilio/` are the authoritative contract. No pre-built Twilio
SDK is used — the client in `src/Infrastructure/Twilio/TwilioMessagingClient.cs` is hand-written against
the spec (HTTP Basic auth, `accountSid_authToken`).

| Capability | Spec document | Operation |
|------------|---------------|-----------|
| Validate a number, get canonical E.164 | `twilio_lookups_v2` | `GET /v2/PhoneNumbers/{PhoneNumber}` |
| Send / schedule a message | `twilio_api_v2010` | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` |
| Fetch delivery outcome | `twilio_api_v2010` | `GET .../Messages/{Sid}.json` |
| Cancel a scheduled message | `twilio_api_v2010` | `POST .../Messages/{Sid}.json` (`Status=canceled`) |
| Redact a message body | `twilio_api_v2010` | `POST .../Messages/{Sid}.json` (`Body=`) |
| Reconciliation listing | `twilio_api_v2010` | `GET .../Messages.json?From=&DateSent>=&DateSent<=` |

Design notes:
- The **delivery follow-up** is scheduled with Twilio (`ScheduleType=fixed`, `SendAt` ≈ 3 days out) via the
  Messaging Service — Twilio holds the timer, not this app. On order cancel (or removal of the destination
  number) the not-yet-sent follow-up is called off with `Status=canceled`, so a cancelled delivery is
  never asked about.
- **Content disposal** redacts the body at Twilio (`Body=`) so the text is no longer retrievable there,
  while the message record and its outcome survive.
- **Reconciliation** asks Twilio only for `Twilio:FromNumber`'s messages (`From` filter) over the range and
  lines them up against eShop's own records by message SID. Twilio's `DateSent` bounds are whole GMT days,
  so the upper bound is widened by a day and trimmed to the exact instant in memory to cover the whole range.
- A message that cannot be sent is recorded as a failure but **never** fails the order operation. A shopper
  with no number on file is simply not messaged. Phone numbers are stored but never written to logs, and are
  masked in API responses.

## Configuration

Bound from the `Twilio:` section (values come from user-secrets / environment, never the repo):

- `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`
- `Twilio:BaseUrl` — optional override for the **messaging** API only (Lookups is unaffected).

Load the secrets (values from the provided environment variables):

```bash
P=src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"          --project "$P"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"           --project "$P"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"          --project "$P"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID" --project "$P"
```

## Verify it yourself

This machine has only the .NET 10 SDK and no LocalDB, so run with roll-forward and the in-memory store.
With the in-memory provider, PublicApi holds its own store — drive the whole flow through PublicApi.

**1. Run PublicApi**

```bash
export DOTNET_ROLL_FORWARD=Major
ASPNETCORE_ENVIRONMENT=Development \
UseOnlyInMemoryDatabase=true \
ASPNETCORE_URLS="https://localhost:10703;http://localhost:10704" \
dotnet run --project src/PublicApi --no-launch-profile
```

**2. Get tokens** (`-k` trusts the dev cert)

```bash
SH=$(curl -sk -X POST https://localhost:10703/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | grep -o '"token":"[^"]*' | cut -d'"' -f4)
AD=$(curl -sk -X POST https://localhost:10703/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}'    | grep -o '"token":"[^"]*' | cut -d'"' -f4)
```

**3. Register the two verification numbers** (register/message only these two)

```bash
curl -sk -X POST https://localhost:10703/api/contact-numbers -H "Authorization: Bearer $SH" \
  -H "Content-Type: application/json" -d "{\"phoneNumber\":\"$TWILIO_TEST_TO_NUMBER\"}"        # reachable
curl -sk -X POST https://localhost:10703/api/contact-numbers -H "Authorization: Bearer $SH" \
  -H "Content-Type: application/json" -d "{\"phoneNumber\":\"$TWILIO_UNREACHABLE_TO_NUMBER\"}" # undeliverable
# a bad number is rejected here:
curl -sk -X POST https://localhost:10703/api/contact-numbers -H "Authorization: Bearer $SH" \
  -H "Content-Type: application/json" -d '{"phoneNumber":"+1555000"}'                          # 400
```

**4. Place → dispatch → observe → cancel**

```bash
OID=$(curl -sk -X POST https://localhost:10703/api/orders -H "Authorization: Bearer $SH" \
  -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":5,"quantity":1}]}' \
  | grep -o '"orderId":[0-9]*' | grep -o '[0-9]*')

curl -sk -X POST "https://localhost:10703/api/orders/$OID/dispatch" -H "Authorization: Bearer $AD"
curl -sk "https://localhost:10703/api/orders/$OID/notifications" -H "Authorization: Bearer $SH"   # follow-up == scheduled
curl -sk -X POST "https://localhost:10703/api/orders/$OID/cancel" -H "Authorization: Bearer $AD"
curl -sk "https://localhost:10703/api/orders/$OID/notifications" -H "Authorization: Bearer $SH"   # follow-up == canceled
```

Expect the reachable number's messages `delivered`, the unreachable number's `undelivered` (error 30034),
and the delivery follow-up `scheduled` after dispatch then `canceled` after cancel.

**5. Resend idempotency** (use a notification id from step 4 whose message did not reach)

```bash
curl -sk -X POST https://localhost:10703/api/notifications/2/resend -H "Authorization: Bearer $AD" \
  -H "Content-Type: application/json" -d '{"idempotencyKey":"k1"}'   # sends -> notificationId N
curl -sk -X POST https://localhost:10703/api/notifications/2/resend -H "Authorization: Bearer $AD" \
  -H "Content-Type: application/json" -d '{"idempotencyKey":"k1"}'   # same key -> same N, no new send
curl -sk -X POST https://localhost:10703/api/notifications/2/resend -H "Authorization: Bearer $AD" \
  -H "Content-Type: application/json" -d '{"idempotencyKey":"k2"}'   # fresh key -> new notification
```

**6. Content disposal**

```bash
curl -sk -X DELETE https://localhost:10703/api/notifications/1/content -H "Authorization: Bearer $AD"
# Optional provider-side proof (body becomes empty, status survives):
curl -sk -u "$TWILIO_ACCOUNT_SID:$TWILIO_AUTH_TOKEN" \
  "https://api.twilio.com/2010-04-01/Accounts/$TWILIO_ACCOUNT_SID/Messages/<SID>.json"
```

**7. Reconciliation** (a range that has data)

```bash
curl -sk -G https://localhost:10703/api/notifications/reconciliation -H "Authorization: Bearer $AD" \
  --data-urlencode "from=2026-08-17T00:00:00Z" --data-urlencode "to=2026-08-17T23:59:59Z"
```

Shows `matched` (both know), `providerOnly` (Twilio knows, eShop doesn't) and `eShopOnly` (eShop recorded a
message Twilio's sent-range doesn't return, e.g. a still-scheduled follow-up).
