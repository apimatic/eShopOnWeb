# SMS order notifications (Twilio)

Adds SMS notifications to eShopOnWeb as orders move, with Twilio as the provider. It is **additive**
— the existing catalog/basket/order flow is untouched. Everything is exposed on **`src/PublicApi`**
(JWT-authenticated), routed under `/api/`.

## What was added

| Layer | Files |
| --- | --- |
| Domain | `ApplicationCore/Entities/NotificationAggregate/` (`ContactNumber`, `Notification`, `NotificationKind`); `OrderAggregate/OrderStatus.cs` + `Order.MarkDispatched/MarkCancelled` |
| Provider port | `ApplicationCore/Interfaces/ISmsProvider.cs` (+ DTOs) |
| Provider adapter | `Infrastructure/Sms/TwilioSmsProvider.cs`, `TwilioSettings.cs` (binds `Twilio:` config) |
| Services | `ApplicationCore/Services/ContactNumberService.cs`, `NotificationService.cs` + specifications |
| API | `PublicApi/ContactNumberEndpoints/`, `OrderEndpoints/`, `NotificationEndpoints/` |

### Endpoints

| Method & route | Who | Purpose |
| --- | --- | --- |
| `POST /api/contact-numbers` | shopper | Register a number (validated + canonicalised by the provider up front). Returns `contactNumberId`. |
| `GET /api/contact-numbers` | shopper | The caller's numbers. |
| `DELETE /api/contact-numbers/{contactNumberId}` | shopper | Remove one (owner-scoped). |
| `POST /api/orders` | shopper | Place an order from catalog items. Returns `orderId`. Shopper is texted "placed". |
| `POST /api/orders/{orderId}/dispatch` | **operator** | Text "on its way" + schedule a delivery follow-up with the provider a few days later. |
| `POST /api/orders/{orderId}/cancel` | **operator** | Text "cancelled" + call off the not-yet-sent follow-up. |
| `GET /api/my-orders` | shopper | Caller's orders + where each notification got to. |
| `GET /api/orders/{orderId}/notifications` | shopper | Notifications for the caller's own order; each has a `notificationId`. |
| `POST /api/notifications/{notificationId}/resend` | **operator** | Re-send, idempotent on a caller-supplied key. Returns the produced `notificationId`. |
| `DELETE /api/notifications/{notificationId}/content` | **operator** | Dispose of the message text at the provider; the record + outcome survive. |
| `GET /api/notifications/reconciliation?from=&to=` | **operator** | Provider's messages from `Twilio:FromNumber` over a range, lined up against eShop's records. |

Operator actions require the existing **Administrators** role. Every other endpoint is shopper-scoped
and acts only on the caller's own data. A message that can't be sent never fails the order operation;
a shopper with no number on file is simply not messaged. Shoppers' numbers are never logged, never
returned in notification bodies, and the auth token is never logged/returned/committed.

### Design notes
- **Provider protocol** is implemented directly against the documented REST API (no SDK): messaging on
  `api.twilio.com/2010-04-01` (overridable by `Twilio:BaseUrl`), lookup on `lookups.twilio.com/v2`
  (a different host, not governed by `BaseUrl`). HTTP Basic auth (AccountSid:AuthToken).
- **Validation/canonicalisation**: Lookup `GET /v2/PhoneNumbers/{n}` — reject unless `valid`, store `phone_number`.
- **Scheduling**: `POST Messages.json` with `MessagingServiceSid` + `ScheduleType=fixed` + `SendAt` (+3 days).
  **Cancel**: `POST Messages/{sid}.json` with `Status=canceled`.
- **Content disposal**: `POST Messages/{sid}.json` with empty `Body` (redaction at the provider).
- **Reconciliation**: `GET Messages.json?From={FromNumber}&DateSent>=…&DateSent<=…`, all pages followed;
  precise `[from,to]` filtering applied client-side.
- **Status** is read back from the provider on demand (`GET Messages/{sid}.json`) because there is no
  publicly reachable callback URL for this app.

---

## How to run it

Prereqs (per this machine): only the .NET 10 SDK is present and there's no LocalDB, so run in-memory
with SDK roll-forward. Secrets are loaded into **.NET user-secrets** (never into the repo).

```bash
# 1. Load Twilio credentials into user-secrets (values from env; never printed/committed)
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
# (Twilio:BaseUrl is optional — leave unset to use the provider's default messaging host.)
cd ../..

# 2. Build + run (roll-forward to the .NET 10 SDK; in-memory DB; your assigned ports)
export DOTNET_ROLL_FORWARD=Major
dotnet build eShopOnWeb.sln
ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true \
  ASPNETCORE_URLS='https://localhost:9623;http://localhost:9624' \
  dotnet run --project src/PublicApi
```

Swagger: `https://localhost:9623/swagger`. Seeded users (password `Pass@word1`):
`admin@microsoft.com` (operator+shopper), `demouser@microsoft.com` (shopper).

> In-memory data lives only for one run; place/dispatch/cancel the orders you create in the same run.

---

## Step-by-step self-verification (curl)

Uses the two designated numbers only: `TWILIO_TEST_TO_NUMBER` (Canadian, reachable) and
`TWILIO_UNREACHABLE_TO_NUMBER` (US, undeliverable by design). `-k` skips the dev-cert check.

```bash
API=https://localhost:9623
tok(){ curl -sk -X POST $API/api/authenticate -H 'Content-Type: application/json' \
  -d "{\"username\":\"$1\",\"password\":\"Pass@word1\"}" | python -c "import sys,json;print(json.load(sys.stdin)['token'])"; }
ADMIN=$(tok admin@microsoft.com); DEMO=$(tok demouser@microsoft.com)
CA="$TWILIO_TEST_TO_NUMBER"; US="$TWILIO_UNREACHABLE_TO_NUMBER"
```

**1 — Contact numbers (Flow 1).** A bad number is rejected up front; a good one is stored canonical.
```bash
curl -sk -o/dev/null -w "invalid=%{http_code}\n" -X POST $API/api/contact-numbers \
  -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' -d '{"phoneNumber":"12345"}'   # 400
curl -sk -X POST $API/api/contact-numbers -H "Authorization: Bearer $ADMIN" \
  -H 'Content-Type: application/json' -d "{\"phoneNumber\":\"$CA\"}"                                     # 201 {contactNumberId, phoneNumber:+1…}
curl -sk $API/api/contact-numbers -H "Authorization: Bearer $ADMIN"                                     # lists it
```

**2 — Order lifecycle + real delivery (Flow 2).** Place → dispatch → cancel as admin.
```bash
O1=$(curl -sk -X POST $API/api/orders -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' \
     -d '{"items":[{"catalogItemId":1,"quantity":2}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
curl -sk $API/api/orders/$O1/notifications -H "Authorization: Bearer $ADMIN"     # placed msg -> sent/delivered (a real text arrives on $CA)
curl -sk -X POST $API/api/orders/$O1/dispatch -H "Authorization: Bearer $ADMIN"  # "on its way" + a DeliveryFollowUp with status "scheduled"
curl -sk -X POST $API/api/orders/$O1/cancel   -H "Authorization: Bearer $ADMIN"  # "cancelled" + the follow-up flips to "canceled"
curl -sk $API/api/orders/$O1/notifications -H "Authorization: Bearer $ADMIN"     # #3 DeliveryFollowUp status == canceled  <-- never sent
curl -sk $API/api/my-orders -H "Authorization: Bearer $ADMIN"
```
Watch for: the placed/dispatched/cancelled messages reach `delivered`, and the follow-up shows
`scheduled` after dispatch then `canceled` after cancel (it never goes out).

**3 — Undeliverable + resend idempotency (Flow 3).** demouser registers the US number and orders.
```bash
curl -sk -X POST $API/api/contact-numbers -H "Authorization: Bearer $DEMO" -H 'Content-Type: application/json' -d "{\"phoneNumber\":\"$US\"}"
O2=$(curl -sk -X POST $API/api/orders -H "Authorization: Bearer $DEMO" -H 'Content-Type: application/json' \
     -d '{"items":[{"catalogItemId":2,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
NID=$(curl -sk $API/api/orders/$O2/notifications -H "Authorization: Bearer $DEMO" \
     | python -c "import sys,json;print(json.load(sys.stdin)['notifications'][0]['notificationId'])")   # status -> undelivered (err 30034)

# operator resend, idempotent on the key:
curl -sk -X POST $API/api/notifications/$NID/resend -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' -d '{"idempotencyKey":"A"}'  # -> notificationId=X
curl -sk -X POST $API/api/notifications/$NID/resend -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' -d '{"idempotencyKey":"A"}'  # -> SAME X, nothing re-sent
curl -sk -X POST $API/api/notifications/$NID/resend -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' -d '{"idempotencyKey":"B"}'  # -> new id, a real second attempt
```

**4 — Content disposal (Flow 3).** The provider's copy is emptied; the record + outcome remain.
```bash
curl -sk -o/dev/null -w "redact=%{http_code}\n" -X DELETE $API/api/notifications/1/content -H "Authorization: Bearer $ADMIN"   # 204
# Optional external proof (uses the account creds directly, verification only):
curl -s -u "$TWILIO_ACCOUNT_SID:$TWILIO_AUTH_TOKEN" \
  "https://api.twilio.com/2010-04-01/Accounts/$TWILIO_ACCOUNT_SID/Messages/<that-sid>.json" \
  | python -c "import sys,json;d=json.load(sys.stdin);print('body=',repr(d['body']),'status=',d['status'])"   # body='' status='delivered'
```

**5 — Reconciliation (Flow 3).** Only `Twilio:FromNumber` traffic is counted; discrepancies surface both ways.
```bash
FROM=$(python -c "import datetime;print(datetime.datetime.now(datetime.UTC).strftime('%Y-%m-%dT00:00:00Z'))")
TO=$(python   -c "import datetime;print((datetime.datetime.now(datetime.UTC)+datetime.timedelta(minutes=5)).strftime('%Y-%m-%dT%H:%M:%SZ'))")
curl -sk "$API/api/notifications/reconciliation?from=$FROM&to=$TO" -H "Authorization: Bearer $ADMIN"
```
Look for `fromNumber` == your `Twilio:FromNumber`, `matched` (SID ↔ notificationId), `onlyAtProvider`
(the account's other traffic eShop didn't create), and `onlyInEShop` (e.g. the canceled follow-up,
which the provider's *sent-in-range* list won't return).

**6 — Access control.**
```bash
curl -sk -o/dev/null -w "cross-order=%{http_code}\n"  $API/api/orders/$O2/notifications -H "Authorization: Bearer $ADMIN"   # 404 (not admin's order)
curl -sk -o/dev/null -w "non-admin-dispatch=%{http_code}\n" -X POST $API/api/orders/$O2/dispatch -H "Authorization: Bearer $DEMO"  # 403
curl -sk -o/dev/null -w "anon=%{http_code}\n" -X POST $API/api/orders -H 'Content-Type: application/json' -d '{"items":[]}'  # 401
```
