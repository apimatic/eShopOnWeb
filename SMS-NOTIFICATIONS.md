# Order Notifications by SMS (Twilio)

An **additive** capability on eShopOnWeb: shoppers put a mobile number on file, receive SMS as their
orders move (placed → dispatched → cancelled), and operators can resend, dispose of message content, and
reconcile against the provider. It does not change the existing catalog/basket/order flow. Everything is
exposed as JWT-authenticated HTTP endpoints on **`src/PublicApi`**, routed under `/api/`.

Twilio is reached exclusively through the **AsadAli.TwilioSdk** package (the `twilio-sdk` plugin). The
contract used is captured in [`twilio-plan.md`](./twilio-plan.md).

## Endpoints

| Method & route | Who | What |
|---|---|---|
| `POST /api/contact-numbers` | shopper | Register a mobile number. Rejected here if the provider does not consider it usable; the provider's canonical (E.164) form is stored. Returns `contactNumberId`. |
| `GET /api/contact-numbers` | shopper | The caller's own registered numbers. |
| `DELETE /api/contact-numbers/{contactNumberId}` | shopper | Remove one. Afterwards nothing may be sent to it again. |
| `POST /api/orders` | shopper | Place an order from catalog item ids + quantities (reuses the existing Order model). Shopper is told it was placed. Returns `orderId`. |
| `POST /api/orders/{orderId}/dispatch` | operator | Mark dispatched: tell the shopper it's on its way and **queue a follow-up with Twilio for a few days later**. |
| `POST /api/orders/{orderId}/cancel` | operator | Cancel: tell the shopper and **call off any not-yet-sent follow-up** so it can never reach them. |
| `GET /api/my-orders` | shopper | The caller's orders, each with where its notifications got to. |
| `GET /api/orders/{orderId}/notifications` | owner/operator | What was sent for the order and what became of each message. Each entry carries its own `notificationId`. |
| `POST /api/notifications/{notificationId}/resend` | operator | Re-send a message that didn't reach the shopper. Carries an idempotency key (`idempotencyKey` in the JSON body, or the `Idempotency-Key` header); a repeat under the same key does not send again. Returns the produced `notificationId`. |
| `DELETE /api/notifications/{notificationId}/content` | operator | Dispose of a message's content — redacted at Twilio too; the record and outcome survive. |
| `GET /api/notifications/reconciliation?from={iso}&to={iso}` | operator | Provider's record for the range (only `Twilio:FromNumber`'s messages) lined up against what eShop believes it sent. |

Operator endpoints require the existing **Administrators** role. Every other endpoint is shopper-scoped and
acts only on the caller's own data.

## Configuration

Bound from the `Twilio:` section (values come from configuration only — never committed):

| Key | From env var |
|---|---|
| `Twilio:AccountSid` | `TWILIO_ACCOUNT_SID` |
| `Twilio:AuthToken` | `TWILIO_AUTH_TOKEN` (secret — never logged/returned/committed) |
| `Twilio:FromNumber` | `TWILIO_FROM_NUMBER` |
| `Twilio:MessagingServiceSid` | `TWILIO_MESSAGING_SERVICE_SID` |
| `Twilio:BaseUrl` | *(optional)* override for the messaging API base address only |

`Notifications:FollowUpDelayDays` (default `3`) sets how far ahead the delivery follow-up is scheduled.

Load the credentials into **.NET user-secrets** for `src/PublicApi` (run once; reads the env var values):

```bash
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
```

The app validates these at startup and refuses to boot if a required one is missing.

## Run it (this machine)

Only the .NET 10 SDK is installed (the pinned 8.0 runtime is absent) and there is no LocalDB, so:

```bash
cd src/PublicApi
DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true \
  dotnet run --no-launch-profile --urls "https://localhost:10123"
```

> In-memory data is per-run: dispatch/cancel the orders you create **in that same run**. `curl -k` skips the
> dev-cert check (`dotnet dev-certs https --check --trust` if needed).

## Verify end to end

Use the two provided fixtures only: `TWILIO_TEST_TO_NUMBER` (Canadian, really reachable) and
`TWILIO_UNREACHABLE_TO_NUMBER` (US, accepted by the API then refused by the carrier — an expected outcome,
not a defect). `admin@microsoft.com` / `Pass@word1` is both a shopper and an Administrator.

```bash
BASE=https://localhost:10123

# 1) Bearer token
TOKEN=$(curl -sk -X POST $BASE/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")

# 2) Register both fixtures (Lookup validates + canonicalizes). A bad number is rejected 400.
curl -sk -X POST $BASE/api/contact-numbers -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d "{\"phoneNumber\":\"$TWILIO_TEST_TO_NUMBER\"}"
curl -sk -X POST $BASE/api/contact-numbers -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d "{\"phoneNumber\":\"$TWILIO_UNREACHABLE_TO_NUMBER\"}"
curl -sk $BASE/api/contact-numbers -H "Authorization: Bearer $TOKEN"

# 3) Place an order (real "placed" SMS to both numbers). Note the orderId.
curl -sk -X POST $BASE/api/orders -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":3,"units":2},{"catalogItemId":5,"units":1}]}'

# 4) After a few seconds, statuses refresh live from Twilio:
#    the Canadian number -> delivered, the US number -> undelivered (err 30034).
curl -sk $BASE/api/orders/1/notifications -H "Authorization: Bearer $TOKEN"

# 5) Dispatch -> "on its way" + two follow-ups appear as status=scheduled (queued with Twilio).
curl -sk -X POST $BASE/api/orders/1/dispatch -H "Authorization: Bearer $TOKEN"
curl -sk $BASE/api/orders/1/notifications -H "Authorization: Bearer $TOKEN"

# 6) Resend the undelivered notification; idempotency: same key = same id (no 2nd send), new key = new send.
curl -sk -X POST $BASE/api/notifications/1/resend -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"idempotencyKey":"key-A"}'
curl -sk -X POST $BASE/api/notifications/1/resend -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"idempotencyKey":"key-A"}'   # replayed
curl -sk -X POST $BASE/api/notifications/1/resend -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"idempotencyKey":"key-B"}'   # new

# 7) Cancel -> the scheduled follow-ups flip to status=canceled (called off before sending).
curl -sk -X POST $BASE/api/orders/1/cancel -H "Authorization: Bearer $TOKEN"
curl -sk $BASE/api/orders/1/notifications -H "Authorization: Bearer $TOKEN"

# 8) Dispose content -> body cleared here and redacted at Twilio; record + outcome survive.
curl -sk -X DELETE $BASE/api/notifications/2/content -H "Authorization: Bearer $TOKEN"

# 9) Reconciliation over a range with data (only Twilio:FromNumber's traffic).
FROM=$(python -c "import datetime;print((datetime.datetime.now(datetime.UTC)-datetime.timedelta(hours=1)).strftime('%Y-%m-%dT%H:%M:%SZ'))")
TO=$(python   -c "import datetime;print((datetime.datetime.now(datetime.UTC)+datetime.timedelta(hours=1)).strftime('%Y-%m-%dT%H:%M:%SZ'))")
curl -sk "$BASE/api/notifications/reconciliation?from=$FROM&to=$TO" -H "Authorization: Bearer $TOKEN"
```

What you should see: a genuine delivery to the Canadian number; the US number reported `undelivered`; two
follow-ups `scheduled` then `canceled`; resend idempotency (same key returns the same `notificationId`); a
reconciliation report whose `matched` set lines up eShop's sends with Twilio's record, `eShopOnly` shows the
cancelled-before-send follow-ups, and `providerOnly` surfaces the account's other traffic.
