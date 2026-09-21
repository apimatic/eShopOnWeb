# Order notifications by SMS (Twilio) — PublicApi

Additive capability: eShopOnWeb keeps shoppers informed by SMS as their orders progress, using **Twilio**
as the messaging provider. It does not change the existing catalog/basket/order flow.

## What was added

- **Domain** (`ApplicationCore`)
  - `Order` gains an additive `OrderStatus` (`Placed` → `Dispatched`/`Cancelled`) with guarded transitions.
  - New aggregates `ContactNumber` and `Notification` (the latter carries the provider's message SID and its
    current delivery outcome, so later requests can act on and report about a message).
  - Port `ISmsProvider` + plain DTOs (no SDK types leak into the domain); services `ContactNumberService`
    and `OrderNotificationService`.
- **Infrastructure**
  - `TwilioSmsProvider` — the only Twilio-facing code, over the vendored APIMatic-generated SDK
    (`src/TwilioSdk`, built from source).
  - `TwilioSettings` bound from the `Twilio:` config section with **fail-fast** startup validation.
  - `AddSmsNotifications(...)` DI extension: a long-lived `TwilioSdkClient` singleton over a named
    `HttpClient`; the `Twilio:BaseUrl` override is applied **only** to the messaging node (Lookups keeps its
    own host); request-body logging is off and the logger factory is set explicitly so the number/body are
    never logged.
- **PublicApi** — the endpoints below (JWT; identity from the token; operator actions restricted to the
  administrator role).

| Method & route | Who | Purpose |
| --- | --- | --- |
| `POST /api/contact-numbers` | shopper | Register a mobile number (validated & canonicalized via Twilio Lookups). Returns `contactNumberId`. |
| `GET /api/contact-numbers` | shopper | The caller's own numbers. |
| `DELETE /api/contact-numbers/{contactNumberId}` | shopper | Remove one of the caller's numbers. |
| `POST /api/orders` | shopper | Place an order from catalog items. Returns `orderId`. Sends "order placed". |
| `POST /api/orders/{orderId}/dispatch` | operator | Mark dispatched. Sends "on its way" + queues a delivery follow-up ~3 days out with the provider. |
| `POST /api/orders/{orderId}/cancel` | operator | Cancel. Sends "cancelled" + calls off any follow-up not yet sent. |
| `GET /api/my-orders` | shopper | The caller's orders, each with its notifications' delivery outcomes. |
| `GET /api/orders/{orderId}/notifications` | shopper | Notifications for the caller's own order; each carries a `notificationId`. |
| `POST /api/notifications/{notificationId}/resend` | operator | Re-send; idempotent on the `Idempotency-Key` header. Returns the new `notificationId`. |
| `DELETE /api/notifications/{notificationId}/content` | operator | Dispose message content at the provider (fact/outcome survive). |
| `GET /api/notifications/reconciliation?from=&to=` | operator | Provider's messages (from `Twilio:FromNumber` only) vs what eShop believes it sent, over an ISO-8601 range. |

A message that cannot be sent never fails the order operation; a shopper with no number on file is not messaged.

## Prerequisites

- .NET SDK present. This machine has only the .NET 10 SDK and the ASP.NET Core 8.0 runtime, so build/run with
  `DOTNET_ROLL_FORWARD=Major` (lets the 8.0-pinned `global.json` roll forward to the 10 SDK).
- No SQL Server LocalDB here → run with `UseOnlyInMemoryDatabase=true`. (In-memory data lives only for the run,
  so create/dispatch/cancel the orders you test within one run.)
- Dev HTTPS cert trusted (`dotnet dev-certs https --check`), or use `curl -k` / the `http://` port.

## 1. Load the Twilio credentials into user-secrets (values from env, never committed)

```bash
cd src/PublicApi
dotnet user-secrets set "Twilio:AccountSid"          "$TWILIO_ACCOUNT_SID"
dotnet user-secrets set "Twilio:AuthToken"           "$TWILIO_AUTH_TOKEN"
dotnet user-secrets set "Twilio:FromNumber"          "$TWILIO_FROM_NUMBER"
dotnet user-secrets set "Twilio:MessagingServiceSid" "$TWILIO_MESSAGING_SERVICE_SID"
cd ../..
```

`Twilio:BaseUrl` is optional (messaging-API base override); leave unset for the default `api.twilio.com`.

## 2. Run PublicApi

```bash
export DOTNET_ROLL_FORWARD=Major
export UseOnlyInMemoryDatabase=true
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="https://localhost:36703;http://localhost:36704"
dotnet run --project src/PublicApi --no-launch-profile
```

Swagger: <https://localhost:36703/swagger>. (If a credential is missing/blank the host refuses to start —
that is the fail-fast working.)

## 3. Verify the flows (curl)

Only the two supplied destinations are ever registered/messaged: `TWILIO_TEST_TO_NUMBER` (Canadian, delivers)
and `TWILIO_UNREACHABLE_TO_NUMBER` (US, accepted then refused by the carrier — an expected outcome, not a bug).

```bash
BASE=https://localhost:36703

# Tokens (shopper + operator)
DEMO=$(curl -sk -X POST $BASE/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
ADMIN=$(curl -sk -X POST $BASE/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")

# Flow 1 — register the shopper's number (canonicalized; invalid numbers are rejected here)
curl -sk -X POST $BASE/api/contact-numbers -H "Authorization: Bearer $DEMO" -H 'Content-Type: application/json' \
  -d "{\"phoneNumber\":\"$TWILIO_TEST_TO_NUMBER\"}"
curl -sk $BASE/api/contact-numbers -H "Authorization: Bearer $DEMO"

# Flow 2 — place an order (note the orderId), then read its notifications (real SMS to the CA number)
curl -sk -X POST $BASE/api/orders -H "Authorization: Bearer $DEMO" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":5,"quantity":1}]}'
curl -sk $BASE/api/orders/1/notifications -H "Authorization: Bearer $DEMO"      # OrderPlaced -> delivered

# Operator dispatches: "on its way" + a follow-up queued with the provider (status "scheduled")
curl -sk -X POST $BASE/api/orders/1/dispatch -H "Authorization: Bearer $ADMIN"
curl -sk $BASE/api/orders/1/notifications -H "Authorization: Bearer $DEMO"

# Operator cancels: "cancelled" + the follow-up is called off (its status becomes "canceled")
curl -sk -X POST $BASE/api/orders/1/cancel -H "Authorization: Bearer $ADMIN"
curl -sk $BASE/api/orders/1/notifications -H "Authorization: Bearer $DEMO"

# Flow 3 — resend (idempotent on the key). Same key => same notificationId, no second send; new key => new id.
curl -sk -X POST $BASE/api/notifications/1/resend -H "Authorization: Bearer $ADMIN" -H 'Idempotency-Key: key-1'
curl -sk -X POST $BASE/api/notifications/1/resend -H "Authorization: Bearer $ADMIN" -H 'Idempotency-Key: key-1'
curl -sk -X POST $BASE/api/notifications/1/resend -H "Authorization: Bearer $ADMIN" -H 'Idempotency-Key: key-2'

# Reconciliation over a range with data (ISO-8601). Provider's FromNumber messages vs eShop's record.
FROM=$(python -c "import datetime;print((datetime.datetime.now(datetime.timezone.utc)-datetime.timedelta(hours=1)).isoformat())")
TO=$(python   -c "import datetime;print((datetime.datetime.now(datetime.timezone.utc)+datetime.timedelta(hours=1)).isoformat())")
curl -sk "$BASE/api/notifications/reconciliation?from=$FROM&to=$TO" -H "Authorization: Bearer $ADMIN"

# Content disposal: text removed at the provider; the fact + outcome survive in eShop.
curl -sk -X DELETE $BASE/api/notifications/1/content -H "Authorization: Bearer $ADMIN"

# Undeliverable outcome demo: register the US number, place an order, watch it settle to "undelivered".
curl -sk -X POST $BASE/api/contact-numbers -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' \
  -d "{\"phoneNumber\":\"$TWILIO_UNREACHABLE_TO_NUMBER\"}"
curl -sk -X POST $BASE/api/orders -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":5,"quantity":1}]}'
curl -sk $BASE/api/my-orders -H "Authorization: Bearer $ADMIN"                  # status -> undelivered (err 30034)

# Delete a number: afterwards it is gone and nothing is sent to it again.
curl -sk -X DELETE $BASE/api/contact-numbers/1 -H "Authorization: Bearer $DEMO"
curl -sk $BASE/api/contact-numbers -H "Authorization: Bearer $DEMO"            # []
```

(`GET /api/orders/{orderId}/notifications` refreshes each message's outcome from Twilio, so re-running it a
moment later shows `queued` → `sent` → `delivered`/`undelivered`.)

## Notes

- The Twilio SDK is vendored under `src/TwilioSdk` and built from source (it is not on NuGet). All Twilio
  calls go through it; nothing else talks to Twilio.
- `twilio-plan.md` (repo root) is the SDK contract sheet / plan used to build this.
