# Order notifications by SMS (Twilio) — verification guide

This adds SMS order-notifications to eShopOnWeb via the **PublicApi** project, using the vendored
APIMatic-generated Twilio .NET SDK (`src/TwilioSdk`) for every Twilio interaction. It is additive —
the existing catalog/basket/order flow is untouched.

## What was built

| Endpoint | Role | Purpose |
| --- | --- | --- |
| `POST /api/contact-numbers` | shopper | Register a mobile number (validated + canonicalized via Twilio Lookup; unusable numbers rejected 400). Returns `contactNumberId`. |
| `GET /api/contact-numbers` | shopper | The caller's registered numbers. |
| `DELETE /api/contact-numbers/{id}` | shopper | Remove one of the caller's numbers. |
| `POST /api/orders` | shopper | Place an order from catalog item ids + quantities (reuses the existing Order/OrderItem model). Returns `orderId`. Shopper is texted "placed". |
| `POST /api/orders/{orderId}/dispatch` | operator | Mark dispatched; text "on its way"; queue a delivery follow-up **with Twilio** for 3 days later. |
| `POST /api/orders/{orderId}/cancel` | operator | Cancel; text "cancelled"; **cancel the queued follow-up at Twilio** so it never sends. |
| `GET /api/my-orders` | shopper | The caller's orders, each with its notifications' delivery outcomes (refreshed from Twilio). |
| `GET /api/orders/{orderId}/notifications` | owner or operator | Every message for the order + its provider SID and delivery status; each entry carries `notificationId`. |
| `POST /api/notifications/{notificationId}/resend` | operator | Re-send a failed message; idempotency-key gated. Returns `notificationId`. |
| `DELETE /api/notifications/{notificationId}/content` | operator | Redact the message body at Twilio (status/record survive). |
| `GET /api/notifications/reconciliation?from=&to=` | operator | Twilio's record of `Twilio:FromNumber` traffic in the range, lined up against eShop's. |

Operator endpoints require the `Administrators` role; everything else is shopper-scoped to the caller.

## Configuration

Settings bind from the `Twilio:` section (no values in the repo):
`Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`, and the
optional `Twilio:BaseUrl` (overrides the **messaging** API host only). The host **refuses to start**
if any of the four credentials is missing or blank.

Secrets are held in **.NET user-secrets** for the PublicApi project (already loaded from the
`TWILIO_*` environment variables). To (re)load them:

```bash
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
```

## Run it (this machine)

```bash
# .NET 10 SDK only + no LocalDB → roll forward and use the in-memory store.
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development     # loads user-secrets
export UseOnlyInMemoryDatabase=true
export ASPNETCORE_URLS="https://localhost:36763;http://localhost:36764"
dotnet run --project src/PublicApi/PublicApi.csproj
```

Swagger: <https://localhost:36763/swagger>. In-memory data lives only for the run, and PublicApi has
its own store — so place/dispatch/cancel the orders you create **in the same run**.

## Step-by-step verification (curl)

```bash
B=https://localhost:36763/api
# 1) Bearer token (demo admin is both a shopper and an operator here)
TOKEN=$(curl -sk -X POST "$B/authenticate" -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
AUTH="Authorization: Bearer $TOKEN"

# 2) Register the two safe destinations (CA is deliverable; US is a reserved unreachable number).
#    Never register or text any other real number.
curl -sk -X POST "$B/contact-numbers" -H "$AUTH" -H "Content-Type: application/json" \
  -d "{\"phoneNumber\":\"$TWILIO_TEST_TO_NUMBER\"}"          # -> contactNumberId, canonical E.164
curl -sk -X POST "$B/contact-numbers" -H "$AUTH" -H "Content-Type: application/json" \
  -d "{\"phoneNumber\":\"$TWILIO_UNREACHABLE_TO_NUMBER\"}"
curl -sk "$B/contact-numbers" -H "$AUTH"

# 3) Place an order -> real SMS to both numbers; note the orderId.
curl -sk -X POST "$B/orders" -H "$AUTH" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":1,"quantity":1}]}'         # -> orderId

# 4) See delivery outcomes (refreshed from Twilio): CA -> delivered, US -> undelivered (expected).
curl -sk "$B/orders/1/notifications" -H "$AUTH"

# 5) Dispatch -> "on its way" + two follow-ups scheduled with Twilio (status "scheduled", 3 days out).
curl -sk -X POST "$B/orders/1/dispatch" -H "$AUTH"
curl -sk "$B/orders/1/notifications" -H "$AUTH"

# 6) Cancel -> the queued follow-ups become "canceled" (never sent) + a "cancelled" SMS goes out.
curl -sk -X POST "$B/orders/1/cancel" -H "$AUTH"
curl -sk "$B/orders/1/notifications" -H "$AUTH"

# 7) Operator resend of a failed (undelivered) message. Pick the failed notificationId from step 6.
KEY="resend-$(python -c 'import uuid;print(uuid.uuid4())')"
curl -sk -X POST "$B/notifications/2/resend" -H "$AUTH" -H "Content-Type: application/json" \
  -d "{\"idempotencyKey\":\"$KEY\"}"                         # -> notificationId, replayed:false
curl -sk -X POST "$B/notifications/2/resend" -H "$AUTH" -H "Content-Type: application/json" \
  -d "{\"idempotencyKey\":\"$KEY\"}"                         # SAME key -> same id, replayed:true (no 2nd send)

# 8) Dispose of a message's content (redacts the body at Twilio; status/record survive).
curl -sk -X DELETE "$B/notifications/1/content" -H "$AUTH"  # -> 204; notification shows contentDisposed:true

# 9) Reconciliation over a range with data (only Twilio:FromNumber traffic).
FROM=$(date -u -d '-1 hour' +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || date -u +%Y-%m-%dT00:00:00Z)
TO=$(date -u +%Y-%m-%dT%H:%M:%SZ)
curl -sk "$B/notifications/reconciliation?from=$FROM&to=$TO" -H "$AUTH"
```

### What you should see
- A **real text** arrives at `TWILIO_TEST_TO_NUMBER` for the placed/dispatched messages.
- The `TWILIO_UNREACHABLE_TO_NUMBER` messages settle to **undelivered** (error 30034) — an expected
  outcome of the live account's registration status, not a defect.
- After dispatch, two `DeliveryFollowUp` entries are **scheduled**; after cancel they are **canceled**
  (called off at Twilio before sending).
- Resend under the same key returns the same `notificationId` with `replayed:true` and sends nothing
  more; a fresh key sends again.
- Content disposal returns 204 and flips `contentDisposed` to true while the delivery status remains.
- Reconciliation returns `inBoth`/`providerOnly`/`eshopOnly` counts and per-message entries, filtered
  by `Twilio:FromNumber`.

## Notes
- **Never send to any number other than the two provided** — messages are really sent and cost money.
- A message that cannot be sent never fails the order operation; a shopper with no number on file is
  simply not messaged.
- The follow-up is queued and cancelled **at Twilio** (scheduled message), not by an in-app timer.
- The auth token is never logged, returned, or written to any file; shopper numbers are never logged
  and are masked in responses.
