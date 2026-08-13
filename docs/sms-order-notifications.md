# SMS Order Notifications (Twilio)

An **additive** capability on `src/PublicApi`: shoppers put a mobile number on file, get texted as
their order moves (placed → dispatched → cancelled), and operators can resend, dispose of message
content, and reconcile against the provider. Twilio is the messaging provider; every Twilio
interaction goes through the classic Messaging API and the Lookup v2 API.

## Endpoints

| Method & route | Role | Purpose |
|---|---|---|
| `POST /api/contact-numbers` | shopper | Register a mobile number (validated + canonicalised via Lookup). Returns `contactNumberId`. |
| `GET /api/contact-numbers` | shopper | The caller's registered numbers. |
| `DELETE /api/contact-numbers/{contactNumberId}` | shopper | Remove one of the caller's numbers. |
| `POST /api/orders` | shopper | Place an order from catalog item ids + quantities. Returns `orderId`. Texts "placed". |
| `POST /api/orders/{orderId}/dispatch` | **operator** | Mark dispatched. Texts "on its way" + schedules a delivery follow-up ~3 days out with the provider. |
| `POST /api/orders/{orderId}/cancel` | **operator** | Cancel. Texts "cancelled" + calls off any not-yet-sent follow-up. |
| `GET /api/my-orders` | shopper | The caller's orders, each with its notifications. |
| `GET /api/orders/{orderId}/notifications` | shopper (own order) | Messages for one of the caller's orders; each entry carries its `notificationId`. |
| `POST /api/notifications/{notificationId}/resend` | **operator** | Resend a message that didn't reach the shopper. Body/header `idempotencyKey`. Returns the new `notificationId`. |
| `DELETE /api/notifications/{notificationId}/content` | **operator** | Dispose of a message's content at the provider (redact) and here. |
| `GET /api/notifications/reconciliation?from={iso}&to={iso}` | **operator** | Provider's messages from the configured number vs what eShop believes it sent. |

Operator = the `Administrators` role the project already uses. Every other endpoint is shopper-scoped
and acts only on the caller's own data (identity taken from the JWT).

## How it maps to Twilio (all via the twilio-docs MCP reference)

- **Register number** → Lookup v2 `GET https://lookups.twilio.com/v2/PhoneNumbers/{number}`: reject when
  `valid=false`; store the canonical `phone_number`. Lookup is a **separate host** and is *not* governed
  by `Twilio:BaseUrl`.
- **Send / placed / dispatched / cancelled** → `POST /2010-04-01/Accounts/{Sid}/Messages.json` with
  `From={Twilio:FromNumber}`, `To`, `Body`.
- **Follow-up (queued with the provider)** → same create with `MessagingServiceSid`, `ScheduleType=fixed`,
  `SendAt` (ISO-8601, ~3 days out) → status `scheduled`. The provider holds and sends it, not this app.
- **Call off the follow-up** → `POST .../Messages/{Sid}.json` with `Status=canceled`.
- **Delivery outcome** → `GET .../Messages/{Sid}.json` (there is no public callback URL, so status is
  pulled from the provider on read).
- **Dispose content** → `POST .../Messages/{Sid}.json` with an empty `Body` (redaction) — text is gone at
  the provider, the record and outcome survive.
- **Reconciliation** → `GET .../Messages.json?From={Twilio:FromNumber}&DateSent>=…&DateSent<=…`, following
  `next_page_uri` to cover the whole range. The `From` filter is asked of the provider directly, so other
  traffic on the account is never counted.

Messaging calls use `Twilio:BaseUrl` when set, otherwise `https://api.twilio.com`. Auth is HTTP Basic
(Account SID / Auth Token). The auth token and shopper numbers are never logged.

## Configuration

Bind from the `Twilio:` section (no values are hard-coded):
`Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber`, `Twilio:MessagingServiceSid`,
and the optional `Twilio:BaseUrl`. Load them into **.NET user-secrets** (never into the repo):

```bash
P=src/PublicApi/PublicApi.csproj
dotnet user-secrets --project $P set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets --project $P set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets --project $P set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets --project $P set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
# Twilio:BaseUrl is optional; leave unset to use the default messaging host.
```

## Run it (this machine)

`global.json` is pinned to the 8.0.x SDK but only the .NET 10 SDK is installed and the ASP.NET Core 8
runtime is missing, so roll forward to major. `global.json` is already set to `rollForward: latestMajor`;
run with `DOTNET_ROLL_FORWARD=Major`, in-memory DB, on the assigned ports:

```bash
DOTNET_ROLL_FORWARD=Major UseOnlyInMemoryDatabase=true ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="https://localhost:10423;http://localhost:10424" \
  dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

> In-memory data lives only for one run; dispatch/cancel the orders you create in the same run.
> Ensure the HTTPS dev cert is trusted (`dotnet dev-certs https --check`); curl below uses `-k`.

## Step-by-step verification

Seeded users: shopper `demouser@microsoft.com`, operator `admin@microsoft.com`, both `Pass@word1`.
Text **only** `TWILIO_TEST_TO_NUMBER` (Canadian, deliverable) and `TWILIO_UNREACHABLE_TO_NUMBER`
(US, accepted then carrier-refused).

```bash
B=https://localhost:10423
tok(){ curl -sk -X POST "$B/api/authenticate" -H "Content-Type: application/json" \
  -d "{\"username\":\"$1\",\"password\":\"Pass@word1\"}" | python -c "import sys,json;print(json.load(sys.stdin)['token'])"; }
DEMO=$(tok demouser@microsoft.com); ADMIN=$(tok admin@microsoft.com)

# 1) Register the deliverable number (stores the provider's canonical form). An unusable number → 400.
curl -sk -X POST "$B/api/contact-numbers" -H "Authorization: Bearer $DEMO" -H "Content-Type: application/json" \
  -d "{\"phoneNumber\":\"$TWILIO_TEST_TO_NUMBER\"}"

# 2) Place an order (catalog item ids from GET /api/catalog-items) → a real "placed" text is delivered.
OID=$(curl -sk -X POST "$B/api/orders" -H "Authorization: Bearer $DEMO" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":5,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")

# 3) Dispatch (operator): "on its way" text + a follow-up scheduled ~3 days out (status "scheduled").
curl -sk -X POST "$B/api/orders/$OID/dispatch" -H "Authorization: Bearer $ADMIN"
curl -sk "$B/api/orders/$OID/notifications" -H "Authorization: Bearer $DEMO"   # see the scheduled follow-up

# 4) Cancel (operator): "cancelled" text + the follow-up is called off (status "canceled") before it sends.
curl -sk -X POST "$B/api/orders/$OID/cancel" -H "Authorization: Bearer $ADMIN"
curl -sk "$B/api/orders/$OID/notifications" -H "Authorization: Bearer $DEMO"   # follow-up now "canceled"

# 5) Undelivered + resend + idempotency: register the US number, place an order, wait until the "placed"
#    message reads "undelivered", then resend. Same key = no second send; a fresh key sends again.
curl -sk -X POST "$B/api/contact-numbers" -H "Authorization: Bearer $DEMO" -H "Content-Type: application/json" \
  -d "{\"phoneNumber\":\"$TWILIO_UNREACHABLE_TO_NUMBER\"}"
# ... place an order, find the undelivered notificationId N via GET /api/orders/{id}/notifications, then:
curl -sk -X POST "$B/api/notifications/$N/resend" -H "Authorization: Bearer $ADMIN" -H "Content-Type: application/json" -d '{"idempotencyKey":"k1"}'  # → new id
curl -sk -X POST "$B/api/notifications/$N/resend" -H "Authorization: Bearer $ADMIN" -H "Content-Type: application/json" -d '{"idempotencyKey":"k1"}'  # → same id, no send
curl -sk -X POST "$B/api/notifications/$N/resend" -H "Authorization: Bearer $ADMIN" -H "Content-Type: application/json" -d '{"idempotencyKey":"k2"}'  # → new id, new send

# 6) Dispose content (operator): text is redacted at the provider and here; fact + outcome survive.
curl -sk -X DELETE "$B/api/notifications/1/content" -H "Authorization: Bearer $ADMIN"

# 7) Reconciliation (operator) over today: matched = our sends, providerOnly = other account traffic.
FROM=$(python -c "import datetime;print(datetime.datetime.now(datetime.UTC).replace(hour=0,minute=0,second=0,microsecond=0).strftime('%Y-%m-%dT%H:%M:%SZ'))")
TO=$(python -c "import datetime;print((datetime.datetime.now(datetime.UTC)+datetime.timedelta(minutes=5)).strftime('%Y-%m-%dT%H:%M:%SZ'))")
curl -sk "$B/api/notifications/reconciliation?from=$FROM&to=$TO" -H "Authorization: Bearer $ADMIN"
```

Role/ownership checks worth a glance: a shopper token on any operator route → **403**; a shopper viewing
another shopper's order notifications → **404**; an unauthenticated call → **401**.
