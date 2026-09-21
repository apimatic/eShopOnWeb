# SMS order notifications (Twilio)

An additive capability on the **`src/PublicApi`** project: it keeps shoppers informed by text message
as their orders progress, using **Twilio** as the messaging provider. It does not replace the existing
catalog/basket/order flow.

## What it adds

| Endpoint | Who | Purpose |
| --- | --- | --- |
| `POST /api/contact-numbers` | shopper | Register a mobile number. The provider validates it and its **canonical E.164 form** is stored. Returns `contactNumberId`. |
| `GET /api/contact-numbers` | shopper | The caller's registered numbers. |
| `DELETE /api/contact-numbers/{contactNumberId}` | shopper | Remove one of the caller's numbers. |
| `POST /api/orders` | shopper | Place an order from catalog item ids + quantities (reuses the app's Order model). Returns `orderId`. Tells the shopper it was placed. |
| `POST /api/orders/{orderId}/dispatch` | operator | Mark dispatched. Tells the shopper, and **queues a "how did delivery go" follow-up with Twilio for a few days later**. |
| `POST /api/orders/{orderId}/cancel` | operator | Cancel. Tells the shopper, and **calls off the not-yet-sent follow-up** so it never reaches them. |
| `GET /api/my-orders` | shopper | The caller's orders, each showing where its notifications got to. |
| `GET /api/orders/{orderId}/notifications` | shopper | What was sent for this order and what became of each message. Each entry carries its own `notificationId`. Refreshes live delivery status from the provider. |
| `POST /api/notifications/{notificationId}/resend` | operator | Re-send a message that did not reach the shopper. Deduplicated by a caller-supplied idempotency key (`Idempotency-Key` header, body, or `?idempotencyKey=`). Returns the new message's `notificationId`. |
| `DELETE /api/notifications/{notificationId}/content` | operator | Dispose of a message's content: redacted at the provider (no longer retrievable there) and dropped locally, while the fact it was sent and its outcome survive. |
| `GET /api/notifications/reconciliation?from={iso}&to={iso}` | operator | The provider's own record of messages sent **from this app's configured number** over a date range, lined up against what eShop believes it sent. |

Rules honoured: a shopper only ever sees/uses/deletes their own numbers and orders; operator actions
(dispatch, cancel, resend, content disposal, reconciliation) require the **Administrators** role; a
message that cannot be sent never fails the underlying order operation; a shopper with no number on
file is simply not messaged; **the shopper's number is never written to logs.**

## Configuration

Settings bind from the `Twilio:` section (bind these exact keys; hard-code no values):

| Key | Meaning |
| --- | --- |
| `Twilio:AccountSid` | Account SID (basic-auth username; account path segment). |
| `Twilio:AuthToken` | Auth token (basic-auth password). **Secret** — never logged/returned/committed. |
| `Twilio:FromNumber` | Sending number for immediate messages; the number reconciliation filters by. |
| `Twilio:MessagingServiceSid` | Messaging Service SID — required by Twilio to schedule the follow-up. |
| `Twilio:BaseUrl` | *Optional.* Overrides the base URL of the **messaging** API only (send/read/reconcile). Does **not** affect number lookup, which Twilio serves from a different host. |

The host **fails fast at startup** if `AccountSid`, `AuthToken`, `FromNumber`, or `MessagingServiceSid`
is missing or blank.

Secrets are loaded via **.NET user-secrets** (they must never be written into any file in the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
# Leave Twilio:BaseUrl unset to talk to the real Twilio messaging API.
```

## Design notes

- The Twilio SDK is the APIMatic-generated **`twilio-platforms-team`** plugin SDK, vendored under
  `src/TwilioSdk/` (it is not published to NuGet). `src/Infrastructure/Messaging/` adapts it behind an
  `ISmsGateway` port so the application layer never depends on the vendor.
- New aggregates `ContactNumber` and `OrderNotification` live in `src/ApplicationCore`; EF config +
  `DbSet`s are on `CatalogContext`.
- Delivery status is obtained by **asking the provider** (`FetchMessage`) on read — there is no public
  callback URL for the provider to reach.
- The follow-up is a **provider-scheduled** message (`schedule_type=fixed`, `send_at`, via the Messaging
  Service); cancellation uses the provider's message-cancel; content disposal uses the provider's
  redaction (empty-body update).

See `twilio-plan.md` at the repo root for the full contract sheet and production-readiness decisions.

## Verifying it yourself

Prerequisites on this machine: only the .NET 10 SDK is installed and there is no SQL LocalDB, so the app
runs with roll-forward and the in-memory database.

1. **Load credentials into user-secrets** (see above). The two safe destinations to verify against are
   the Canadian number `TWILIO_TEST_TO_NUMBER` (deliverable) and the US number
   `TWILIO_UNREACHABLE_TO_NUMBER` (accepted then refused by the carrier — an expected undelivered
   outcome). Register/message only those two.

2. **Run the API** (Development loads user-secrets; in-memory DB):

   ```bash
   export DOTNET_ROLL_FORWARD=Major
   cd src/PublicApi
   ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true \
   ASPNETCORE_URLS="https://localhost:36343;http://localhost:36344" \
   dotnet run
   ```

3. **Get a bearer token** (the seeded admin can act as both shopper and operator):

   ```bash
   B=https://localhost:36343
   TOKEN=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
     -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | jq -r .token)
   AUTH="Authorization: Bearer $TOKEN"
   ```

4. **Happy path (real delivery + scheduled follow-up + cancel):**

   ```bash
   # Register the deliverable number, place an order, dispatch it.
   curl -sk -X POST $B/api/contact-numbers -H "$AUTH" -H "Content-Type: application/json" \
     -d "{\"number\":\"$TWILIO_TEST_TO_NUMBER\"}"
   OID=$(curl -sk -X POST $B/api/orders -H "$AUTH" -H "Content-Type: application/json" \
     -d '{"items":[{"catalogItemId":1,"quantity":1}]}' | jq .orderId)
   curl -sk -X POST $B/api/orders/$OID/dispatch -H "$AUTH"
   # See placed=delivered, dispatched=delivered, follow-up=scheduled (a few days out):
   curl -sk $B/api/orders/$OID/notifications -H "$AUTH" | jq
   # Cancel -> the not-yet-sent follow-up flips to "canceled":
   curl -sk -X POST $B/api/orders/$OID/cancel -H "$AUTH"
   curl -sk $B/api/orders/$OID/notifications -H "$AUTH" | jq
   ```

5. **Undelivered + operator resend (idempotent):**

   ```bash
   # Point the on-file number at the unreachable US number, place an order.
   curl -sk -X DELETE $B/api/contact-numbers/1 -H "$AUTH"
   curl -sk -X POST $B/api/contact-numbers -H "$AUTH" -H "Content-Type: application/json" \
     -d "{\"number\":\"$TWILIO_UNREACHABLE_TO_NUMBER\"}"
   OID2=$(curl -sk -X POST $B/api/orders -H "$AUTH" -H "Content-Type: application/json" \
     -d '{"items":[{"catalogItemId":2,"quantity":1}]}' | jq .orderId)
   NID=$(curl -sk $B/api/orders/$OID2/notifications -H "$AUTH" | jq '.notifications[0].notificationId')
   # (status becomes "undelivered" with a provider error code)
   curl -sk -X POST $B/api/notifications/$NID/resend -H "$AUTH" -H "Idempotency-Key: recover-001"  # new
   curl -sk -X POST $B/api/notifications/$NID/resend -H "$AUTH" -H "Idempotency-Key: recover-001"  # deduplicated:true
   curl -sk -X POST $B/api/notifications/$NID/resend -H "$AUTH" -H "Idempotency-Key: recover-002"  # new again
   ```

6. **Content disposal & reconciliation:**

   ```bash
   curl -sk -X DELETE $B/api/notifications/$NID/content -H "$AUTH"      # 204; record survives, content gone
   FROM=$(date -u -d '-2 hours' +%Y-%m-%dT%H:%M:%SZ); TO=$(date -u -d '+1 hour' +%Y-%m-%dT%H:%M:%SZ)
   curl -sk "$B/api/notifications/reconciliation?from=$FROM&to=$TO" -H "$AUTH" | jq
   ```

7. **Run the automated tests** (offline; a fake gateway replaces Twilio):

   ```bash
   export DOTNET_ROLL_FORWARD=Major
   dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj \
     --filter "FullyQualifiedName~NotificationEndpoints"
   ```
