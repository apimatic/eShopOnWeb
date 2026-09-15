# Verifying the SMS order-notification integration

A step-by-step walkthrough that exercises a real message reaching the destination, a follow-up queued
with the provider and then called off, an operator resend with idempotency, content disposal, and a
reconciliation report over a range that has data.

> **Live account.** Real messages are sent and really cost money. Register and message **only** the
> two destinations provided (`TWILIO_TEST_TO_NUMBER`, `TWILIO_UNREACHABLE_TO_NUMBER`). The full run
> below sends ~8 texts.

## 0. Prerequisites

```bash
# Credentials into user-secrets (values from environment; never committed):
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
```

Run the API (in-memory store, .NET 10 SDK rolling forward, assigned ports):

```bash
cd <repo root>
DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true \
  dotnet run --project src/PublicApi --urls "https://localhost:9263;http://localhost:9264"
```

Handy shell setup (uses `curl -k` for the dev cert):

```bash
BASE=https://localhost:9263
tok(){ curl -sk -X POST $BASE/api/authenticate -H 'Content-Type: application/json' \
  -d "{\"username\":\"$1\",\"password\":\"Pass@word1\"}" | sed -E 's/.*"token":"([^"]+)".*/\1/'; }
ADMIN=$(tok admin@microsoft.com); DEMO=$(tok demouser@microsoft.com)
AH="Authorization: Bearer $ADMIN"; DH="Authorization: Bearer $DEMO"; CT='Content-Type: application/json'
```

## 1. Flow 1 — contact number (deliverable Canadian number, as the shopper)

```bash
# Register (validated + canonicalized by Twilio Lookups). Note the returned contactNumberId.
curl -sk -X POST $BASE/api/contact-numbers -H "$DH" -H "$CT" \
  -d "{\"phoneNumber\":\"$TWILIO_TEST_TO_NUMBER\"}"
curl -sk $BASE/api/contact-numbers -H "$DH"            # lists only the caller's own numbers
```

## 2. Flow 2 — place → dispatch → cancel (real delivery + follow-up called off)

```bash
# Place (shopper). A "placed" SMS is sent to the real number. Note orderId (e.g. 1).
curl -sk -X POST $BASE/api/orders -H "$DH" -H "$CT" \
  -d '{"items":[{"catalogItemId":1,"quantity":1},{"catalogItemId":2,"quantity":2}]}'

# Dispatch (operator). "On its way" SMS + a DeliveryFeedbackRequest queued with the provider.
curl -sk -X POST $BASE/api/orders/1/dispatch -H "$AH"

# Inspect: the follow-up shows status "Scheduled" with a providerMessageId and scheduledFor ~3 days out.
curl -sk $BASE/api/orders/1/notifications -H "$DH"

# Cancel (operator). "Cancelled" SMS + the scheduled follow-up is called off.
curl -sk -X POST $BASE/api/orders/1/cancel -H "$AH"

# Inspect again: DeliveryFeedbackRequest is now "Canceled" — it will never go out.
curl -sk $BASE/api/orders/1/notifications -H "$DH"
curl -sk $BASE/api/my-orders -H "$DH"
```

## 3. Flow 3 — undelivered → operator resend with idempotency (US unreachable number)

```bash
# Register the unreachable US number (as admin, who is also a shopper here) and place an order.
curl -sk -X POST $BASE/api/contact-numbers -H "$AH" -H "$CT" \
  -d "{\"phoneNumber\":\"$TWILIO_UNREACHABLE_TO_NUMBER\"}"
curl -sk -X POST $BASE/api/orders -H "$AH" -H "$CT" -d '{"items":[{"catalogItemId":3,"quantity":1}]}'
# Wait a few seconds, then read notifications; the message settles to "Undelivered" (carrier refused).
curl -sk $BASE/api/orders/2/notifications -H "$AH"     # note the undelivered notificationId (e.g. 5)

# Resend under a key -> new message (replayed=false).
curl -sk -X POST $BASE/api/notifications/5/resend -H "$AH" -H "$CT" -d '{"idempotencyKey":"key-A"}'
# Repeat SAME key -> replayed=true, same notificationId, NO second message.
curl -sk -X POST $BASE/api/notifications/5/resend -H "$AH" -H "$CT" -d '{"idempotencyKey":"key-A"}'
# Fresh key -> genuine new attempt (replayed=false, new notificationId).
curl -sk -X POST $BASE/api/notifications/5/resend -H "$AH" -H "$CT" -d '{"idempotencyKey":"key-B"}'
```

## 4. Flow 3 — content disposal (gone at the provider too)

```bash
# Dispose a delivered message's content (operator). Then confirm body is null but the record survives.
curl -sk -X DELETE $BASE/api/notifications/2/content -H "$AH" -i | head -1   # 204
curl -sk $BASE/api/orders/1/notifications -H "$AH"   # notification 2: contentDisposed=true, body=null, status intact
# Optional provider-side proof (redacted body is empty at Twilio):
#   curl -s -u "$TWILIO_ACCOUNT_SID:$TWILIO_AUTH_TOKEN" \
#     "https://api.twilio.com/2010-04-01/Accounts/$TWILIO_ACCOUNT_SID/Messages/<SID>.json"
```

## 5. Flow 3 — reconciliation

```bash
FROM=2026-08-12T00:00:00Z ; TO=$(date -u +%Y-%m-%dT%H:%M:%SZ)
curl -sk "$BASE/api/notifications/reconciliation?from=$FROM&to=$TO" -H "$AH"
# providerCount/eShopCount/matchedCount/providerOnlyCount/eShopOnlyCount + per-message entries.
# Messages sent from Twilio:FromNumber that eShop recorded appear as "Matched"; other account
# traffic on that number appears as "ProviderOnly".
```

## Negative checks (optional)

```bash
curl -sk -X POST $BASE/api/orders/1/dispatch -H "$AH" -w ' %{http_code}\n' -o /dev/null   # 409 (already cancelled)
curl -sk -X POST $BASE/api/notifications/2/resend -H "$AH" -H "$CT" -d '{"idempotencyKey":"x"}' \
  -w ' %{http_code}\n' -o /dev/null                                                       # 409 (already delivered)
curl -sk $BASE/api/orders/2/notifications -H "$DH" -w ' %{http_code}\n' -o /dev/null       # 404 (not the caller's order)
curl -sk -X POST $BASE/api/orders/1/dispatch -H "$DH" -w ' %{http_code}\n' -o /dev/null    # 403 (not an operator)
```
